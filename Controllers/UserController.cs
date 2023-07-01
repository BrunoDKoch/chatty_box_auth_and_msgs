using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Context;
using ChattyBox.Services;
using ChattyBox.Database;
using ChattyBox.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MaxMind.GeoIP2;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Humanizer;
using Microsoft.Extensions.Localization;

namespace ChattyBox.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public class UserController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly WebServiceClient _maxMindClient;
  private readonly IWebHostEnvironment _webHostEnvironment;
  private readonly IHubContext<MessagesHub> _hubContext;
  private readonly IStringLocalizer<UserController> _localizer;
  private readonly EmailService _emailService;
  private readonly UserDB _userDb;

  public UserController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      WebServiceClient maxMindClient,
      IWebHostEnvironment webHostEnvironment,
      IHubContext<MessagesHub> hubContext,
      IStringLocalizer<UserController> localizer,
      EmailService emailService) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _maxMindClient = maxMindClient;
    _webHostEnvironment = webHostEnvironment;
    _hubContext = hubContext;
    _localizer = localizer;
    _emailService = emailService;
    _userDb = new UserDB(_userManager, _roleManager, _configuration, _signInManager);
  }

  async private Task CreateCodeClaim(User user, string claimName) {
    var otp = new Random().Next(100000, 999999).ToString();
    var otpClaim = new Claim(claimName, otp);
    await _userManager.AddClaimAsync(user, otpClaim);
  }

  async private Task<User> GetUser(string email) {
    var user = await _userManager.FindByEmailAsync(email);
    ArgumentNullException.ThrowIfNull(user);
    return user;
  }

  async private Task<User> GetUser(HttpContext httpContext) {
    var user = await _userManager.GetUserAsync(httpContext.User);
    ArgumentNullException.ThrowIfNull(user);
    return user;
  }

  async private Task<User> GetUser(HttpContext httpContext, bool getConnections) {
    var user = await _userManager.Users
      .Include(u => u.ClientConnections)
      .FirstOrDefaultAsync(u => u.Id == httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
    ArgumentNullException.ThrowIfNull(user);
    return user;
  }

  async private Task<bool> CheckLocation(string userId, UserLoginAttempt loginAttempt) {
    using var ctx = new ChattyBoxContext();
    var previousAttempts = await ctx.UserLoginAttempts.Where(l => l.UserId == userId).ToListAsync();
    var suspiciousLocation = previousAttempts.Count() > 0 && previousAttempts
      .Any(
        l =>
          l.Success &&
          DistanceService.CalculateDistance(l.Latitude, l.Longitude, loginAttempt.Latitude, loginAttempt.Longitude) > 1000
      );
    return suspiciousLocation;
  }

  async private Task<UserLoginAttempt> CreateLoginAttempt(string userId, HttpContext context) {
    IPAddress ipAddress;
    // TODO: when ready for deployment, remove if statement
    if (HttpContext.Connection.RemoteIpAddress == null || new List<string> { "::1", "127.0.0.1" }.Contains(HttpContext.Connection.RemoteIpAddress.ToString())) {
      ipAddress = IPAddress.Parse(_configuration.GetValue<string>("TestIP")!);
    } else {
      ipAddress = HttpContext.Connection.RemoteIpAddress;
    }
    var clientInfo = ParsingService.ParseContext(context);
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
      OS = string.Join(' ', $"{clientInfo.OS.Family} {clientInfo.OS.Major}.{clientInfo.OS.Minor}".Split(' ').Distinct()),
      Device = string.Join(' ', $"{clientInfo.Device.Brand} {clientInfo.Device.Family} {clientInfo.Device.Model}".Split(' ').Distinct()),
      Browser = string.Join(' ', $"{clientInfo.UA.Family} {clientInfo.UA.Major}.{clientInfo.UA.Minor}".Split(' ').Distinct())
    };
    return loginAttempt;
  }

  async private Task<UserLoginAttempt?> VerifyLoginAttempt(UserLoginAttempt loginAttempt, User user, LogInInfo data, bool suspiciousLocation) {
    // Handle login attempt from suspicious location
    if (suspiciousLocation) {
      var newLocationVerification = new Random().Next(100000, 999999).ToString();
      var newLocationVerificationClaim = new Claim("NewLocation", newLocationVerification);
      await _userManager.AddClaimAsync(user, newLocationVerificationClaim);
      loginAttempt.Success = false;
      return loginAttempt;
    }
    var signInSuccess = await _signInManager.PasswordSignInAsync(user, data.Password, data.Remember, user.AccessFailedCount > 4);
    // Check if requires 2FA (will return false if device is remembered)
    if (signInSuccess.RequiresTwoFactor) {
      // If yes, but no code is given, return status 400
      if (data.MFACode == null || String.IsNullOrEmpty(data.MFACode)) return null;
      signInSuccess = await _signInManager.TwoFactorAuthenticatorSignInAsync(data.MFACode, data.Remember, data.RememberMultiFactor);
      await _userManager.ResetAccessFailedCountAsync(user);
    }

    loginAttempt.Success = signInSuccess.Succeeded;
    return loginAttempt;
  }

  async private Task SendAvatarUpdateMessage(string userId, string avatar, IEnumerable<string> usersToNotify) {
    await _hubContext.Clients
      .Groups(usersToNotify)
      .SendAsync("newAvatar", new { userId, avatar }, default);
  }

  private void CheckFileSize(IFormFile file) {
    if (file.Length.Bytes() > (20).Megabytes()) {
      throw new InvalidOperationException($"file size {file.Length.Megabytes()} greater than 20MB");
    }
  }

  // Auth
  [AllowAnonymous]
  [HttpPost("Register")]
  async public Task<ActionResult<string>> RegisterUser([FromBody] UserInitialData data) {
    // TODO: Add email confirmation logic
    // For now, we'll just send the email confirmation code to the client
    var otp = new Random().Next(100000, 999999).ToString();
    await _emailService.SendEmail(data.Email, EmailType.EmailConfirmation, otp);
    var createdUser = new UserCreate(data);
    var result = await _userManager.CreateAsync(createdUser);
    if (result.Errors.Count() > 0) {
      var duplicateErrors = result.Errors.Where(e => e.Code.ToLower().StartsWith("duplicate")).ToList();
      if (duplicateErrors is null || duplicateErrors.Count == 0)
        return Unauthorized(string.Join("\n", result.Errors.Select(e => e.Description)));
      return Conflict(string.Join("\n", result.Errors.Select(e => e.Description)));
    }
    var otpClaim = new Claim("OTP", otp);
    await _userManager.AddClaimAsync(createdUser, otpClaim);


    return Ok(otp);
  }

  [AllowAnonymous]
  [HttpPost("Login")]
  async public Task<IActionResult> LogInUser([FromBody] LogInInfo data) {

    var user = await _userManager.Users.Include(u => u.ReportsAgainstUser).FirstOrDefaultAsync(u => u.Email == data.Email);
    try {
      ArgumentNullException.ThrowIfNull(user);
    } catch {
      return Unauthorized(_localizer.GetString("401Auth").Value);
    }

    var loginAttempt = await CreateLoginAttempt(user.Id, HttpContext);

    var suspiciousLocation = await CheckLocation(user.Id, loginAttempt);
    string failureReason = $"Invalid credentials.\n{_localizer.GetString("401Auth").Value}";

    loginAttempt = await VerifyLoginAttempt(loginAttempt, user, data, suspiciousLocation);
    if (loginAttempt is null) return BadRequest();
    using var ctx = new ChattyBoxContext();
    await ctx.UserLoginAttempts.AddAsync(loginAttempt);
    await ctx.SaveChangesAsync();
    // Tell the user if their account is locked out
    if (await _userManager.IsLockedOutAsync(user)) {
      string failureReasonStart;
      if (user.LockoutEnd == DateTimeOffset.MaxValue) {
        failureReasonStart = _localizer.GetString("PermanentSuspension").Value;
      } else {
        failureReasonStart = $"{_localizer.GetString("TemporarySuspension").Value} " +
          $"{TimeSpan.FromMinutes((DateTime.UtcNow - user.LockoutEnd!).Value.TotalMinutes).Humanize(2)}";
      }
      string failureReasonEnd = $"{_localizer.GetString("Reasons")}: " +
        string.Join(',', (
          user.ReportsAgainstUser.Select(r => _localizer.GetString(r.ReportReason.Replace("report.", "").Pascalize()))
          )
        );

      failureReason = $"{failureReasonStart}\n{failureReasonEnd}\n{_localizer.GetString("SupportMistake").Value}";
    };
    if (!loginAttempt.Success) {
      await _userManager.AccessFailedAsync(user);
      return suspiciousLocation ? StatusCode(StatusCodes.Status403Forbidden, failureReason) : Unauthorized(failureReason);
    };
    await _userManager.ResetAccessFailedCountAsync(user);
    return StatusCode(StatusCodes.Status302Found);
  }

  [HttpPut("Change/Email")]
  async public Task<ActionResult> ChangeEmail([FromBody] ChangeEmailRequest body) {
    var user = await GetUser(HttpContext);
    if (user.NormalizedEmail != body.CurrentEmail.ToUpper())
      return Unauthorized();
    var passwordIsValid = await _userManager.CheckPasswordAsync(user, body.Password);
    if (!passwordIsValid) return Unauthorized();

    // Generate a token for undoing this, and send it to old email
    var token = await _userManager.GenerateChangeEmailTokenAsync(user, body.NewEmail);
    await _emailService.SendEmail(user.Email!, EmailType.EmailChangedWarning, itemAndToken: $"?email={user.Email!}&token={token}");

    // Then, set the new email
    await _userManager.SetEmailAsync(user, body.NewEmail);
    return Ok();
  }

  [HttpPatch("Change/Email")]
  async public Task<RedirectResult> UndoChangeEmail([FromQuery] string email, [FromQuery] string token) {
    var user = await GetUser(email);
    await _userManager.ChangeEmailAsync(user, email, token);
    await _userManager.UpdateSecurityStampAsync(user);
    await _hubContext.Clients.User(user.Id).SendAsync("forceLogOut", default);
    return Redirect(_configuration.GetValue<string>("WebsiteUrl")!);
  }

  [HttpPut("Change/Password")]
  async public Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest body) {
    var user = await GetUser(HttpContext);
    var result = await _userManager.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword);
    if (result.Succeeded) return Ok();
    return Unauthorized();
  }

  [HttpGet("Login")]
  async public Task<IActionResult> RefreshLogin([FromQuery] string? ReturnUrl) {
    var userClaim = HttpContext.User;
    var user = await _userManager.GetUserAsync(userClaim);
    ArgumentNullException.ThrowIfNull(user);
    await _signInManager.RefreshSignInAsync(user);
    if (!String.IsNullOrEmpty(ReturnUrl)) return Redirect(ReturnUrl);
    return Ok();
  }

  [HttpGet]
  async public Task<ActionResult<UserPersonalInfo>> GetUser() {
    var user = await _userDb.GetUserPersonalInfo(HttpContext);
    return Ok(user);
  }

  [HttpHead("Logout")]
  async public Task<IActionResult> LogOut([FromQuery] bool invalidateAllSessions = false) {
    var user = await GetUser(HttpContext);
    await _signInManager.SignOutAsync();
    if (invalidateAllSessions) await _userManager.UpdateSecurityStampAsync(user);
    return SignOut();
  }

  [AllowAnonymous]
  [HttpGet("LoggedIn")]
  async public Task<ActionResult<bool?>> CheckIfLoggedIn() {
    try {
      var userClaim = HttpContext.User;
      ArgumentNullException.ThrowIfNull(userClaim);
      if (_signInManager.IsSignedIn(userClaim)) return Ok(true);
      var user = await _userManager.GetUserAsync(userClaim);
      ArgumentNullException.ThrowIfNull(user);
      await _signInManager.RefreshSignInAsync(user);
      return Ok(_signInManager.IsSignedIn(userClaim));
    } catch (Exception e) {
      Console.Error.WriteLine(e);
      return Unauthorized();
    }
  }

  [AllowAnonymous]
  [HttpPost("Validate/Email")]
  async public Task<IActionResult> ValidateEmail([FromBody] EmailValidationRequest request) {
    try {
      var user = await GetUser(request.Email);
      var claims = await _userManager.GetClaimsAsync(user);
      var otpClaim = claims.FirstOrDefault(u => u.Type == "OTP");
      var valid = otpClaim != null && otpClaim.Value == request.Code;
      if (!valid) return Unauthorized("Invalid code");
      user.EmailConfirmed = true;
      await _signInManager.SignInAsync(user, false);
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
  [AllowAnonymous]
  [HttpPost("Validate/Location")]
  async public Task<IActionResult> ValidadeLocation([FromBody] LocationValidationRequest request) {
    var user = await GetUser(request.Email);
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

  [AllowAnonymous]
  [HttpPost("Recovery")]
  async public Task<ActionResult<string>> GetPasswordToken([FromBody] PasswordRecoveryTokenRequest request) {
    var user = await GetUser(request.Email);
    ArgumentNullException.ThrowIfNull(user);
    // TODO: handle this via email
    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
    await _emailService.SendEmail(
      user.Email!, EmailType.PasswordResetConfirmation, itemAndToken: $"?email={user.Email!}&token={token}"
    );
    return Ok(token);
  }

  [AllowAnonymous]
  [HttpPut("Recovery")]
  async public Task<ActionResult<string>> RecoverPassword([FromBody] PasswordResetRequest request) {
    var user = await GetUser(request.Email);
    var result = await _userManager.ResetPasswordAsync(user, request.Token, request.Password);
    if (!result.Succeeded) return Unauthorized();
    return Ok("Password reset");
  }

  // Handle MFA disabling
  [HttpPut("MFA/Disable")]
  async public Task<IActionResult> StartDisableMFA() {
    var user = await GetUser(HttpContext);
    if (!user.TwoFactorEnabled) throw new InvalidOperationException();
    // This will force open a modal to get the user's credentials, which will then call the POST method
    // TODO: inform user via email
    await _emailService.SendEmail(user.Email!, EmailType.MFADisabledWarning);
    return Accepted();
  }

  [HttpPost("MFA/Disable")]
  async public Task<IActionResult> FinishDisableMFA([FromBody] MFADisableRequest request) {
    var user = await GetUser(HttpContext);
    var result = await _userManager.CheckPasswordAsync(user, request.Password);
    if (!result) return Forbid();
    var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
    await _userManager.SetTwoFactorEnabledAsync(user, false);
    await _hubContext.Clients.User(user.Id).SendAsync("currentMFAOptions", new { isEnabled = false, providers }, default);
    return Ok();
  }

  // Images
  [HttpPost("Avatar")]
  async public Task<ActionResult<string>> SaveAvatar([FromForm] IFormFile file) {
    CheckFileSize(file);
    var user =
      await _userManager.Users
        .Include(u => u.Chats)
        .FirstOrDefaultAsync(u => u.Id == HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
    ArgumentNullException.ThrowIfNull(user);
    var avatar = await ImageService.SaveImage(file, user, _webHostEnvironment, isAvatar: true);
    user.Avatar = avatar;
    await _userManager.UpdateAsync(user);

    var groupsToNotify = user.Chats.Select(c => c.Id).Concat(new List<string> { $"{user.Id}_friends" });
    await SendAvatarUpdateMessage(user.Id, avatar, groupsToNotify);
    return Ok(avatar);
  }

  [HttpDelete("Avatar")]
  async public Task<IActionResult> DeleteAvatar() {
    var user = await _userManager.GetUserAsync(HttpContext.User);
    ArgumentNullException.ThrowIfNull(user);
    ArgumentNullException.ThrowIfNullOrEmpty(user.Avatar);
    ImageService.DeleteImage(user.Avatar);
    user.Avatar = String.Empty;
    await _userManager.UpdateAsync(user);

    var groupsToNotify = user.Chats.Select(c => c.Id).Concat(new List<string> { $"{user.Id}_friends" });
    await SendAvatarUpdateMessage(user.Id, avatar: String.Empty, groupsToNotify);
    return Ok();
  }

  [HttpPost("Upload/{chatId}")]
  async public Task<ActionResult<ChatMessage>> UploadImage(string chatId, [FromForm] IFormFile file) {
    CheckFileSize(file);
    var user = await _userManager.GetUserAsync(HttpContext.User);
    ArgumentNullException.ThrowIfNull(user);
    string[] validFileTypes = { "image", "video", "audio" };
    if (!validFileTypes.Contains(file.ContentType.Split("/").First()))
      throw new InvalidOperationException("invalid file type");
    var messagesDB = new MessagesDB(_userManager, _roleManager, _configuration, _signInManager, _maxMindClient);
    string filePath;
    if (file.ContentType.StartsWith("image")) {
      filePath = await ImageService.SaveImage(file, user, _webHostEnvironment, chatId);
    } else {
      filePath = await AudioAndVideoService.SaveFile(file, chatId, user.Id);
    }
    var message = await messagesDB.CreateMessage(user.Id, chatId, filePath);
    await _hubContext.Clients.Group(chatId).SendAsync("newMessage", message, default);
    return Ok(message);
  }
}