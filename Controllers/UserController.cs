using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Context;
using ChattyBox.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MaxMind.GeoIP2;
using System.Net;
using ChattyBox.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;

namespace ChattyBox.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly WebServiceClient _maxMindClient;
  private readonly IWebHostEnvironment _webHostEnvironment;

  public UserController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      WebServiceClient maxMindClient,
      IWebHostEnvironment webHostEnvironment) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _maxMindClient = maxMindClient;
    _webHostEnvironment = webHostEnvironment;
  }

  private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2) {
    const double R = 6371; // Radius of the Earth in km
    var dLat = ToRadians(lat2 - lat1);
    var dLon = ToRadians(lon2 - lon1);
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    var distance = R * c; // Distance in km
    return distance;
  }

  private static double ToRadians(double degrees) {
    return degrees * Math.PI / 180;
  }

  async private Task<bool> CheckLocation(string userId, UserLoginAttempt loginAttempt) {
    using var ctx = new ChattyBoxContext();
    var previousAttempts = await ctx.UserLoginAttempts.Where(l => l.UserId == userId).ToListAsync();
    var suspiciousLocation = previousAttempts.Count() > 0 && previousAttempts
      .Any(
        l =>
          l.Success &&
          CalculateDistance(l.Latitude, l.Longitude, loginAttempt.Latitude, loginAttempt.Longitude) > 1000
      );
    return suspiciousLocation;
  }

  async private Task<JwtSecurityToken> CreateAccessToken(User user) {
    await _userManager.UpdateSecurityStampAsync(user);
    var jwtSection = _configuration.GetSection("JsonWebToken")!;
    var key = jwtSection.GetValue<string>("Key")!;
    var issuer = jwtSection.GetValue<string>("Issuer")!;
    var audience = jwtSection.GetValue<string>("Audience")!;
    var claims = new List<Claim>();
    claims.Add(new Claim(JwtRegisteredClaimNames.GivenName, user.UserName!));
    claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email!));
    claims.Add(new Claim("securityStamp", user.SecurityStamp!));
    var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(30));
    return token;
  }

  async private Task<UserLoginAttempt> CreateLoginAttempt(string userId, HttpContext context) {
    IPAddress ipAddress;
    // TODO: when ready for deployment, remove if statement
    if (HttpContext.Connection.RemoteIpAddress == null || new List<string> { "::1", "127.0.0.1" }.Contains(HttpContext.Connection.RemoteIpAddress.ToString())) {
      ipAddress = IPAddress.Parse(_configuration.GetValue<string>("TestIP")!);
    } else {
      ipAddress = HttpContext.Connection.RemoteIpAddress;
    }
    var city = await _maxMindClient.CityAsync(ipAddress);
    var loginAttempt = new UserLoginAttempt {
      Id = Guid.NewGuid().ToString(),
      UserId = userId,
      IpAddress = ipAddress.ToString(),
      CityName = city.City.Name ?? "unknown",
      GeoNameId = city.City.GeoNameId != null ? city.City.GeoNameId.ToString()! : "unknown",
      CountryName = city.Country.Name ?? "unknown",
      CountryIsoCode = city.Country.IsoCode ?? "unknown",
      Latitude = (double)city.Location.Latitude!,
      Longitude = (double)city.Location.Longitude!,
    };
    return loginAttempt;
  }

  // Auth
  [HttpPost("Register")]
  async public Task<IActionResult> RegisterUser([FromBody] UserInitialData data) {
    var createdUser = new UserCreate(data);
    var result = await _userManager.CreateAsync(createdUser);
    if (result.Errors.Count() > 0) return Conflict();
    foreach (var err in result.Errors) {
      Console.WriteLine(err);
    }
    // TODO: Add email confirmation logic
    // For now, we'll just send the email confirmation code to the client
    var otp = new Random().Next(100000, 999999).ToString();
    var otpClaim = new Claim("OTP", otp);
    await _userManager.AddClaimAsync(createdUser, otpClaim);
    return Ok(otp);
  }

  [HttpPost("Login")]
  async public Task<IActionResult> LogInUser([FromBody] LogInInfo data) {
    var user = await _userManager.FindByEmailAsync(data.Email);
    if (user == null) return Unauthorized();

    try {
      var loginAttempt = await CreateLoginAttempt(user.Id, HttpContext);

      var suspiciousLocation = await CheckLocation(user.Id, loginAttempt);

      if (suspiciousLocation) {
        var newLocationVerification = new Random().Next(100000, 999999).ToString();
        var newLocationVerificationClaim = new Claim("NewLocation", newLocationVerification);
        await _userManager.AddClaimAsync(user, newLocationVerificationClaim);
        loginAttempt.Success = false;
      } else {
        var signInSuccess = await _signInManager.PasswordSignInAsync(user, data.Password, data.Remember, user.AccessFailedCount > 4);
        // Check if requires 2FA (will return false if device is remembered)
        if (signInSuccess.RequiresTwoFactor) {
          // If yes, but no code is given, return status 400
          if (data.MFACode == null || String.IsNullOrEmpty(data.MFACode)) return BadRequest();
          signInSuccess = await _signInManager.TwoFactorAuthenticatorSignInAsync(data.MFACode, data.Remember, data.RememberMultiFactor);
          await _userManager.ResetAccessFailedCountAsync(user);
        }
        loginAttempt.Success = signInSuccess.Succeeded;
      }
      using var ctx = new ChattyBoxContext();
      await ctx.UserLoginAttempts.AddAsync(loginAttempt);
      await ctx.SaveChangesAsync();
      if (!loginAttempt.Success) {
        await _userManager.AccessFailedAsync(user);
        return suspiciousLocation ? Forbid() : Unauthorized();
      };
      await _userManager.ResetAccessFailedCountAsync(user);
      var userClaims = await _userManager.GetClaimsAsync(user);
      if (userClaims.Where(c => c.Type == "stampExpiry").Count() > 1) {
        await _userManager.RemoveClaimsAsync(user, userClaims.Where(c => c.Type == "stampExpiry"));
        await _userManager.AddClaimAsync(user, new Claim("stampExpiry", DateTime.UtcNow.AddDays(30).ToString()));
      }
      var token = await CreateAccessToken(user);
      return Ok(new { token = $"{token.EncodedHeader}.{token.EncodedPayload}.{token.RawEncryptedKey}" });
    } catch (Exception e) {
      Console.Error.WriteLine(e);
      return Unauthorized();
    }
  }

  [HttpHead("Logout")]
  async public Task<IActionResult> LogOut() {
    var userClaim = HttpContext.User;
    if (userClaim == null) return BadRequest();
    var user = await _userManager.GetUserAsync(userClaim);
    if (user == null) return BadRequest();
    await _signInManager.SignOutAsync();
    return SignOut();
  }

  [HttpGet("LoggedIn")]
  async public Task<IActionResult> CheckIfLoggedIn() {
    try {
      var bearer = HttpContext.Request.Headers.Authorization.ToString();
      if (bearer == null || String.IsNullOrEmpty(bearer)) return Ok(false);
      var jwt = bearer.Replace("Bearer ", "");
      var token = new JwtSecurityToken(jwt);
      var email = token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email);
      var user = await _userManager.FindByEmailAsync(email.Value);
      ArgumentNullException.ThrowIfNull(user);
      ArgumentNullException.ThrowIfNull(token.Payload.Exp);
      var userClaims = await _userManager.GetClaimsAsync(user);
      var expiry = DateTimeOffset.FromUnixTimeMilliseconds((int)token.Payload.Exp);
      var stampExpiry = DateTime.Parse(userClaims.First(c => c.Type == "stampExpiry").Value);
      if (stampExpiry < DateTime.UtcNow) throw new InvalidOperationException();
      if (expiry < DateTime.UtcNow)
        token = await CreateAccessToken(user);
      await _signInManager.SignInAsync(user, true);
      return Ok(
        new { token = $"{token.EncodedHeader}.{token.EncodedPayload}.{token.RawEncryptedKey}" }
      );
    } catch {
      return Unauthorized();
    }
  }

  [HttpPost("Validate/Email")]
  async public Task<IActionResult> ValidateEmail([FromBody] EmailValidationRequest request) {
    try {
      var user = await _userManager.FindByEmailAsync(request.Email);
      if (user == null) return BadRequest("User not found");
      var claims = await _userManager.GetClaimsAsync(user);
      var otpClaim = claims.FirstOrDefault(u => u.Type == "OTP");
      var valid = otpClaim != null && otpClaim.Value == request.Code;
      if (!valid) return Unauthorized("Invalid code");
      user.EmailConfirmed = true;
      await _userManager.RemoveClaimAsync(user, user.UserClaims.First(u => u.ClaimType == "OTP").ToClaim());
      return Ok();
    } catch (Exception e) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.Error.WriteLine(e);
      Console.ForegroundColor = ConsoleColor.Green;
      return StatusCode(500);
    }
  }

  // Verify location after alert
  [HttpPost("Validate/Location")]
  async public Task<IActionResult> ValidadeLocation([FromBody] LocationValidationRequest request) {
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user == null) return BadRequest("User not found");
    var claims = await _userManager.GetClaimsAsync(user);
    var newLocationVerificationClaim = claims.FirstOrDefault(u => u.Type == "NewLocation");
    var valid = newLocationVerificationClaim != null && newLocationVerificationClaim.Value == request.Code;
    if (!valid) return Unauthorized("Invalid code");
    using var ctx = new ChattyBoxContext();
    var loginAttempt = await ctx.UserLoginAttempts.OrderBy(l => l.AttemptedAt).FirstAsync(l => l.UserId == user.Id);
    loginAttempt.Success = true;
    await ctx.SaveChangesAsync();
    return Ok();
  }

  [HttpPost("Recovery")]
  async public Task<IActionResult> GetPasswordToken([FromBody] PasswordRecoveryTokenRequest request) {
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user == null) return BadRequest("User not found");
    // TODO: handle this via email
    var token = _userManager.GeneratePasswordResetTokenAsync(user);
    return Ok(token);
  }

  [HttpPut("Recovery")]
  async public Task<IActionResult> RecoverPassword([FromBody] PasswordResetRequest request) {
    var user = await _userManager.FindByEmailAsync(request.Email);
    if (user == null) return BadRequest("User not found");
    var result = await _userManager.ResetPasswordAsync(user, request.Token, request.Password);
    if (!result.Succeeded) return Unauthorized();
    return Ok("Password reset");
  }

  // Images
  [Authorize]
  [HttpPost("Avatar")]
  async public Task<IActionResult> SaveAvatar([FromForm] IFormFile file) {
    var user = await _userManager.GetUserAsync(HttpContext.User);
    if (user == null) return Unauthorized();
    try {
      var avatar = await ImageService.SaveImage(file, user, _webHostEnvironment);
      user.Avatar = avatar;
      await _userManager.UpdateAsync(user);
      return Ok(avatar);
    } catch (Exception e) {
      Console.Error.WriteLine(e);
      return StatusCode(500);
    }
  }
}