using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Context;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MaxMind.GeoIP2;
using System.Net;
using Microsoft.EntityFrameworkCore;

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

  public UserController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      WebServiceClient maxMindClient) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _maxMindClient = maxMindClient;
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

  async private Task<UserLoginAttempt> CreateLoginAttempt(string userId, IPAddress ipAddress) {
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
    createdUser.TwoFactorEnabled = true;
    var otp = new Random().Next(100000, 999999).ToString();
    var otpClaim = new Claim("OTP", otp);
    await _userManager.AddClaimAsync(createdUser, otpClaim);
    await _signInManager.PasswordSignInAsync(createdUser, data.Password, false, false);
    return Ok(otp);
  }

  [HttpPost("Login")]
  async public Task<IActionResult> LogInUser([FromBody] LogInInfo data) {
    var user = await _userManager.FindByEmailAsync(data.Email);
    if (user == null) return Unauthorized();
    try {
      // TODO: when ready for deployment, remove if statement
      IPAddress ipAddress;
      if (HttpContext.Connection.RemoteIpAddress == null || HttpContext.Connection.RemoteIpAddress.ToString() == "::1") {
        var rand = new Random();
        var octets = new byte[4];
        rand.NextBytes(octets);
        ipAddress = new IPAddress(octets);
      } else {
        ipAddress = HttpContext.Connection.RemoteIpAddress;
      }
      var loginAttempt = await CreateLoginAttempt(user.Id, ipAddress);

      using var ctx = new ChattyBoxContext();
      var previousAttempts = await ctx.UserLoginAttempts.Where(l => l.UserId == user.Id).ToListAsync();
      var suspiciousLocation = previousAttempts.Count() > 0 && previousAttempts
        .Any(
          l =>
            l.Success &&
            CalculateDistance(l.Latitude, l.Longitude, loginAttempt.Latitude, loginAttempt.Longitude) > 200
        );
      
      if (suspiciousLocation) {
        var newLocationVerification = new Random().Next(100000, 999999).ToString();
        var newLocationVerificationClaim = new Claim("NewLocation", newLocationVerification);
        await _userManager.AddClaimAsync(user, newLocationVerificationClaim);
        loginAttempt.Success = false;
      } else {
        var signInSuccess = await _signInManager.PasswordSignInAsync(user, data.Password, data.Remember, user.AccessFailedCount > 4);
        loginAttempt.Success = signInSuccess.Succeeded;
      }

      await ctx.UserLoginAttempts.AddAsync(loginAttempt);
      await ctx.SaveChangesAsync();
      if (!loginAttempt.Success) return Unauthorized();
      
      await _userManager.UpdateSecurityStampAsync(user);
      return Ok();
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
    return Ok();
  }

  [HttpGet("Current")]
  async public Task<IActionResult> GetCurrentUser() {
    if (!_signInManager.IsSignedIn(HttpContext.User)) return Unauthorized();
    var user = await _userManager.GetUserAsync(HttpContext.User);
    if (user == null) return Unauthorized();
    return Ok(new {
      email = user.Email,
      userName = user.UserName,
    });
  }

  [Authorize]
  [HttpPost("Validate/2fa")]
  async public Task<IActionResult> ValidadeTwoFactorCode([FromBody] string code) {
    var userClaim = HttpContext.User;
    var user = await _userManager.GetUserAsync(userClaim);
    if (user == null) return BadRequest("Usuário não logado");
    var valid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
    if (!valid) return Unauthorized("Código inválido");
    return Ok();
  }

  [HttpPost("Validate/Email")]
  async public Task<IActionResult> ValidateEmail([FromBody] EmailValidationRequest request) {
    var userClaim = HttpContext.User;
    var user = await _userManager.GetUserAsync(userClaim);
    if (user == null) return BadRequest("User not found");
    var claims = await _userManager.GetClaimsAsync(user);
    var otpClaim = claims.FirstOrDefault(u => u.Type == "OTP");
    var valid = otpClaim != null && otpClaim.Value == request.Code;
    if (!valid) return Unauthorized("Invalid code");
    user.EmailConfirmed = true;
    await _userManager.RemoveClaimAsync(user, user.UserClaims.First(u => u.ClaimType == "OTP").ToClaim());
    return Ok();
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

  // Misc
  [HttpGet("Friends")]
  async public Task<IActionResult> GetFriends() {
    if (!_signInManager.IsSignedIn(HttpContext.User)) return Unauthorized();
    var user = await _userManager.GetUserAsync(HttpContext.User);
    if (user == null) return Unauthorized();
    return Ok(user.Friends);
  }
}