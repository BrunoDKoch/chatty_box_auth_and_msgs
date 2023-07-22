using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Services;
using ChattyBox.Database;
using ChattyBox.Models.AdditionalModels;
using ChattyBox.Hubs;
using ChattyBox.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Security.Claims;

namespace ChattyBox.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public class UserController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly IWebHostEnvironment _webHostEnvironment;
  private readonly IHubContext<MessagesHub> _hubContext;
  private readonly IStringLocalizer<UserController> _localizer;
  private readonly EmailService _emailService;
  private readonly UserDB _userDB;
  private readonly LoginAttemptHelper _loginAttemptHelper;
  private readonly FileService _fileService;
  private readonly CachingService _cachingService;

  public UserController(
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      IWebHostEnvironment webHostEnvironment,
      IHubContext<MessagesHub> hubContext,
      IStringLocalizer<UserController> localizer,
      EmailService emailService,
      UserDB userDb,
      LoginAttemptHelper loginAttemptHelper,
      FileService fileService,
      CachingService cachingService) {
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _webHostEnvironment = webHostEnvironment;
    _hubContext = hubContext;
    _localizer = localizer;
    _emailService = emailService;
    _userDB = userDb;
    _loginAttemptHelper = loginAttemptHelper;
    _fileService = fileService;
    _cachingService = cachingService;
  }

  async private Task SendAvatarUpdateMessage(string userId, string avatar, IEnumerable<string> usersToNotify) {
    await _hubContext.Clients
      .Groups(usersToNotify)
      .SendAsync("newAvatar", new { userId, avatar }, default);
  }

  async private Task<Microsoft.AspNetCore.Identity.SignInResult> AttemptSignIn(User user, LogInInfo logInInfo) {
    var signInResult = await _signInManager.CheckPasswordSignInAsync(user, logInInfo.Password, logInInfo.Remember);
    if (signInResult.RequiresTwoFactor && !string.IsNullOrEmpty(logInInfo.MFACode)) {
      signInResult = await _signInManager.TwoFactorAuthenticatorSignInAsync(logInInfo.MFACode, logInInfo.Remember, logInInfo.RememberMultiFactor);
    }
    return signInResult;
  }

  

  // Auth
  [AllowAnonymous]
  [HttpPost("Register")]
  async public Task<ActionResult<string?>> RegisterUser([FromBody] UserInitialData data) {
    var user = await _userDB.CreateUser(data);
    var otpClaim = OTPClaimUtil.CreateOTPClaim("OTP");
    await _userDB.AddClaim(user, otpClaim);
    await _emailService.SendEmail(data.Email, EmailType.EmailConfirmation, otpClaim.Value);

    return Ok();
  }

  [AllowAnonymous]
  [HttpPost("Login")]
  async public Task<IActionResult> LogInUser([FromBody] LogInInfo data) {
    var user = await _userDB.LogInUser(data);
    var signInResult = await AttemptSignIn(user, data);
    var loginAttempt = await _loginAttemptHelper.CreateLoginAttempt(user.Id, HttpContext);
    var suspiciousLocation = UserDB.CheckLocation(user, loginAttempt);

    if (suspiciousLocation || !signInResult.Succeeded) {
      loginAttempt.Success = false;
      if (signInResult.RequiresTwoFactor) throw new MFACodeRequiredException("");
      if (suspiciousLocation) {
        throw new SuspiciousLocationException(_localizer.GetString("403"));
      }
      throw new InvalidCredentialsException(_localizer.GetString("401Auth"));
    }
    if (!signInResult.Succeeded) throw new InvalidCredentialsException(_localizer.GetString("401Auth"));
    await _signInManager.SignInAsync(user, data.Remember);
    return StatusCode(StatusCodes.Status302Found);
  }

  [HttpPut("Change/Email")]
  async public Task<ActionResult> ChangeEmail([FromBody] ChangeEmailRequest body) {
    var token = await _userDB.ChangeEmail(body, HttpContext);
    // Send undo token to old email
    await _emailService.SendEmail(body.CurrentEmail, EmailType.EmailChangedWarning, itemAndToken: $"?email={body.CurrentEmail}&token={token}");
    return Ok();
  }

  [HttpPatch("Change/Email")]
  async public Task<RedirectResult> UndoChangeEmail([FromQuery] string email, [FromQuery] string token) {
    var user = await _userDB.UndoChangeEmail(email, token);
    HttpContext.Abort();
    await _hubContext.Clients.User(user.Id).SendAsync("forceLogOut", default);
    return Redirect(_configuration.GetValue<string>("WebsiteUrl")!);
  }

  [HttpPut("Change/Password")]
  async public Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest body) {
    var result = await _userDB.ChangePassword(body, HttpContext);
    if (result.Succeeded) return Ok();
    return Unauthorized();
  }

  [HttpGet]
  async public Task<ActionResult<UserPersonalInfo>> GetUser() {
    /*ArgumentNullException.ThrowIfNull(HttpContext.User);
    var id = HttpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(id);
    var cachedUser = await _cachingService.GetCache<UserPersonalInfo>(id.Value);
    if (cachedUser is not null) return Ok(cachedUser);*/
    UserPersonalInfo user = await _userDB.GetUserPersonalInfo(HttpContext);
    //await _cachingService.SetCache(user.Id, user);
    return Ok(user);
  }

  [HttpPost("Logout")]
  async public Task<IActionResult> LogOut([FromQuery] bool invalidateAllSessions = false) {
    await _userDB.SignOut(HttpContext, invalidateAllSessions);
    await _signInManager.SignOutAsync();
    return SignOut();
  }

  [AllowAnonymous]
  [HttpGet("LoggedIn")]
  public ActionResult<bool?> CheckIfLoggedIn() {
    return _signInManager.IsSignedIn(HttpContext.User) ? Ok() : Unauthorized();
  }

  [AllowAnonymous]
  [HttpPost("Validate/Email")]
  async public Task<IActionResult> ValidateEmail([FromBody] EmailValidationRequest request) {
    var user = await _userDB.GetUser(request.Email);
    var claims = await _userDB.GetClaims(user);
    var otpClaim = claims.FirstOrDefault(u => u.Type == "OTP");
    ArgumentNullException.ThrowIfNull(otpClaim);
    var valid = otpClaim is not null && otpClaim.Value == request.Code;
    if (!valid) throw new InvalidCredentialsException("Invalid code");
    user.EmailConfirmed = true;
    await _signInManager.SignInAsync(user, false);
    await _userDB.RemoveClaim(user, otpClaim!);
    return Ok();
  }

  // Verify location after alert
  [AllowAnonymous]
  [HttpPost("Validate/Location")]
  async public Task<IActionResult> ValidadeLocation([FromBody] LocationValidationRequest request) {
    var user = await _userDB.GetUser(request.Email);
    var claims = await _userDB.GetClaims(user);
    var newLocationVerificationClaim = claims.FirstOrDefault(u => u.Type == "NewLocation");
    var valid = newLocationVerificationClaim != null && newLocationVerificationClaim.Value == request.Code;
    if (!valid) return Unauthorized("Invalid code");
    await _userDB.UpdateLoginAttempt(user.Id);
    return Ok();
  }

  [AllowAnonymous]
  [HttpPost("Recovery")]
  async public Task<ActionResult<string>> GetPasswordToken([FromBody] PasswordRecoveryTokenRequest request) {
    var user = await _userDB.GetUser(request.Email);
    ArgumentNullException.ThrowIfNull(user);
    var token = await _userDB.GeneratePasswordResetToken(user);
    await _emailService.SendEmail(
      user.Email!, EmailType.PasswordResetConfirmation, itemAndToken: $"?email={user.Email!}&token={token}"
    );
    return Ok(token);
  }

  [AllowAnonymous]
  [HttpPut("Recovery")]
  async public Task<ActionResult<string>> RecoverPassword([FromBody] PasswordResetRequest request) {
    var user = await _userDB.GetUser(request.Email);
    var result = await _userDB.ResetPassword(user, request);
    if (!result.Succeeded) return Unauthorized();
    return Ok("Password reset");
  }

  // Handle MFA disabling
  [HttpPut("MFA/Disable")]
  async public Task<IActionResult> StartDisableMFA() {
    var user = await _userDB.GetUser(HttpContext);
    // This will force open a modal to get the user's credentials, which will then call the POST method
    if (!user.TwoFactorEnabled) throw new InvalidOperationException();
    await _emailService.SendEmail(user.Email!, EmailType.MFADisabledWarning);
    return Accepted();
  }

  [HttpPost("MFA/Disable")]
  async public Task<IActionResult> FinishDisableMFA([FromBody] MFADisableRequest request) {
    await _userDB.ToggleMFA(request, HttpContext);
    return Ok();
  }

  // Images
  [HttpPost("Avatar")]
  async public Task<ActionResult<string>> SaveAvatar([FromForm] IFormFile file) {
    FileService.CheckFileSize(file);
    var avatarAndGroups = await _userDB.ChangeAvatar(file, HttpContext);
    var userId = avatarAndGroups.Item1;
    var avatar = avatarAndGroups.Item2;
    var groupsToNotify = avatarAndGroups.Item3;
    await SendAvatarUpdateMessage(userId, avatar, groupsToNotify);
    return Ok(avatar);
  }

  [HttpDelete("Avatar")]
  async public Task<IActionResult> DeleteAvatar() {
    var user = await _userDB.SetAvatarToDefault(HttpContext);
    var groupsToNotify = user.Chats.Select(c => c.Id).Concat(new List<string> { $"{user.Id}_friends" });
    await SendAvatarUpdateMessage(user.Id, avatar: string.Empty, groupsToNotify);
    return Ok();
  }

  [HttpPost("Upload/{chatId}")]
  async public Task<ActionResult<ChatMessage>> UploadImage(string chatId, [FromForm] IFormFile file) {
    FileService.CheckFileSize(file);
    var user = await _userDB.GetUser(HttpContext);
    string filePath;
    if (file.ContentType.StartsWith("image")) {
      filePath = await _fileService.SaveImage(file, user, chatId);
    } else if (file.ContentType.StartsWith("video") || file.ContentType.StartsWith("audio")) {
      filePath = await _fileService.SaveFile(
        file,
        chatId,
        user.Id,
        file.ContentType.StartsWith("image") ? FileType.Audio : FileType.Video
      );
    } else {
      filePath = await _fileService.SaveFile(file, chatId, user.Id, FileType.Other);
    }
    await _hubContext.Clients.User(user.Id).SendAsync("fileAdded", new { chatId, filePath });
    return Ok(filePath);
  }

  [HttpDelete("Upload/{chatId}")]
  async public Task<ActionResult<ChatMessage>> RemoveImage(string chatId, [FromBody] FileDeletionRequest fileDeletionRequest) {
    var user = await _userDB.GetUser(HttpContext);
    if (!fileDeletionRequest.FilePath.Contains(chatId) || !fileDeletionRequest.FilePath.Contains(user.Id))
      throw new InvalidOperationException(_localizer.GetString("403"));
    await _fileService.DeleteFile(fileDeletionRequest.FilePath);
    return Ok(fileDeletionRequest.FilePath);
  }
}