using ChattyBox.Models;
using ChattyBox.Models.AdditionalModels;
using ChattyBox.Context;
using ChattyBox.Services;
using ChattyBox.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.Extensions.Localization;
using Humanizer;

namespace ChattyBox.Database;

public class UserDB {

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly EmailService _emailService;
  private readonly LoginAttemptHelper _loginAttemptHelper;
  private readonly IStringLocalizer<UserDB> _localizer;
  private readonly FileService _fileService;

  public UserDB(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      EmailService emailService,
      IStringLocalizer<UserDB> localizer,
      LoginAttemptHelper loginAttemptHelper,
      FileService fileService) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _emailService = emailService;
    _localizer = localizer;
    _loginAttemptHelper = loginAttemptHelper;
    _fileService = fileService;
  }

  static private User EnsureUserIsNotNull(User? user) {
    ArgumentNullException.ThrowIfNull(user);
    return user!;
  }

  // Private auth-related methods
  async private Task RemoveOTPClaimFromUser(User user) {
    var claims = await _userManager.GetClaimsAsync(user);
    var claim = claims.FirstOrDefault(c => c.Type == "OTP");
    ArgumentNullException.ThrowIfNull(claim);
    await _userManager.RemoveClaimAsync(user, claim);
  }

  async private Task<(bool, Claim?)> CheckIfEmailIsVerified(User user) {
    var isVerified = await _userManager.IsEmailConfirmedAsync(user);
    Claim? otpClaim = null;
    if (!isVerified) {
      await RemoveClaim(user, "OTP");
      otpClaim = OTPClaimUtil.CreateOTPClaim("OTP");
      await _userManager.AddClaimAsync(user, otpClaim);
    }
    return (isVerified, otpClaim);
  }

  // Public auth methods
  static public bool CheckLocation(User user, UserLoginAttempt loginAttempt) {
    var suspiciousLocation = user.UserLoginAttempts.Count > 0 && user.UserLoginAttempts
      .Any(
        l =>
          l.Success &&
          LoginAttemptHelper.CalculateDistance(l.Latitude, l.Longitude, loginAttempt.Latitude, loginAttempt.Longitude) > 1000
      );
    return suspiciousLocation;
  }

  async public Task<string> GetLoginFailureReason(User user) {
    string failureReason = $"Invalid credentials.\n{_localizer.GetString("401Auth").Value}";
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
    return failureReason;
  }

  // Create
  async public Task<FriendRequestFiltered?> CreateFriendRequest(string addingId, string addedId) {
    using var ctx = new ChattyBoxContext();
    try {
      var friendRequest = new FriendRequest {
        UserAddingId = addingId,
        UserBeingAddedId = addedId,
        SentAt = DateTime.UtcNow,
      };
      await ctx.FriendRequests.AddAsync(friendRequest);
      await ctx.SaveChangesAsync();
      return new FriendRequestFiltered {
        UserAdding = new UserPartialResponse((await _userManager.FindByIdAsync(addingId))!)
      };
    } catch (Exception e) {
      Console.Error.Write(e);
      return null;
    }
  }

  async public Task<User> CreateUser(UserInitialData data) {
    var createdUser = new UserCreate(data);
    createdUser.Avatar = await FileService.GetDefaultAvatar(createdUser);
    var result = await _userManager.CreateAsync(createdUser);
    if (result.Errors.Any()) {
      var duplicateErrors = result.Errors.Where(e => e.Code.ToLower().StartsWith("duplicate")).ToList();
      if (duplicateErrors is null || duplicateErrors.Count == 0)
        throw new InvalidCredentialsException(string.Join("\n", result.Errors.Select(e => e.Description)));
      throw new ConflictException(string.Join("\n", result.Errors.Select(e => e.Description)));
    }
    var otpClaim = OTPClaimUtil.CreateOTPClaim("OTP");
    await _userManager.AddClaimAsync(createdUser, otpClaim);
    return createdUser;
  }

  // Read
  async public Task<User> GetUser(string emailOrId, bool isEmailQuery = false) {
    User? user;
    if (!isEmailQuery) user = await _userManager.FindByIdAsync(emailOrId);
    else user = await _userManager.FindByEmailAsync(emailOrId);
    return EnsureUserIsNotNull(user);
  }

  async public Task<User> GetUser(HttpContext httpContext, bool getConnections = false) {
    var user = getConnections ?
      await _userManager.GetUserAsync(httpContext.User) :
      await _userManager.Users
        .Include(u => u.ClientConnections)
        .FirstOrDefaultAsync(u => u.Id == httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
    return EnsureUserIsNotNull(user);
  }

  async public Task<List<Claim>> GetClaims(User user) {
    return (await _userManager.GetClaimsAsync(user)).ToList();
  }

  async public Task<List<string>> GetRoles(string userId) {
    using var ctx = new ChattyBoxContext();
    return await ctx.Roles
      .Include(r => r.Users)
      .Where(r => r.Users.Any(u => u.Id == userId))
      .Select(r => r.NormalizedName!)
      .ToListAsync();
  }

  async public Task<string?> GetUserStatus(string userId) {
    var user = await GetUser(userId);
    return user.Status;
  }

  async public Task<List<string>> GetChatIds(string userId) {
    using var ctx = new ChattyBoxContext();
    var chatIds = await ctx.Chats.Where(c => c.Users.Any(u => u.Id == userId)).Select(c => c.Id).ToListAsync();
    return chatIds;
  }

  async public Task<List<UserIdAndConnections>> GetActiveFriendIds(string userId) {
    var friendIdsAndConnections = await _userManager.Users
      .Include(u => u.ClientConnections)
      .Where(u => u.Friends.Any(f => f.Id == userId || u.IsFriendsWith.Any(f => f.Id == userId)))
      .Select(u => new UserIdAndConnections {
        Id = u.Id,
        ConnectionIds = u.ClientConnections.Where(c => c.Active != null && (bool)c.Active!).Select(c => c.ConnectionId).ToList() ?? new List<string>()
      })
      .ToListAsync();
    return friendIdsAndConnections;
  }

  async public Task<User> LogInUser(LogInInfo logInInfo) {
    var user =
      await _userManager.Users
        .Include(u => u.ReportsAgainstUser)
          .AsSplitQuery()
        .Include(u => u.UserLoginAttempts)
          .AsSplitQuery()
        .FirstOrDefaultAsync(u => u.Email == logInInfo.Email.Trim());
    ArgumentNullException.ThrowIfNull(user);
    var emailIsConfirmed = await CheckIfEmailIsVerified(user);

    // If email is not confirmed, re-send confirmation and prevent login
    if (!emailIsConfirmed.Item1) {
      await _emailService.SendEmail(user.Email!, EmailType.EmailConfirmation, otpCode: emailIsConfirmed.Item2!.Value!);
      throw new EmailConfirmationException();
    }
    return user;
  }

  async public Task<User> GetPreliminaryConnectionCallInfo(string userId) {
    var user = await _userManager.Users
      .Include(u => u.Friends)
        .ThenInclude(f => f.ClientConnections)
        .AsSplitQuery()
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.ClientConnections)
        .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Messages)
          .ThenInclude(m => m.From)
          .AsSplitQuery()
      .Include(c => c.Chats)
        .ThenInclude(c => c.SystemMessages)
        .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Messages)
          .ThenInclude(m => m.ReadBy)
          .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Users.Where(u => u.Id != userId))
        .AsSplitQuery()
      .Include(u => u.Blocking)
      .Include(u => u.FriendRequestsReceived)
        .ThenInclude(f => f.UserAdding)
        .AsSplitQuery()
      .Include(u => u.Roles)
      .FirstOrDefaultAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    return user;
  }

  async public Task<List<User>> GetAnUsersFriends(string userId) {
    var user = await _userManager.Users
      .Include(u => u.Friends)
        .ThenInclude(f => f.ClientConnections)
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.ClientConnections)
      .FirstAsync(u => u.Id == userId);
    List<User> userFriends = new();
    userFriends = user.Friends.Concat(user.IsFriendsWith).ToList();
    return userFriends;
  }

  async public Task<List<UserPartialResponse>> GetBlockedUsers(string userId) {
    var user = await _userManager.Users
      .Include(u => u.Blocking)
      .FirstAsync(u => u.Id == userId);
    if (user == null) return new List<UserPartialResponse>();
    return user.Blocking.Select(b => new UserPartialResponse(b, userId)).ToList();
  }

  async public Task<List<UserPartialResponse>> GetUsers(string userId, UserSearchCall searchCall) {
    var searchTerm = searchCall.UserName.ToUpper();
    var users = await _userManager.Users
      .Include(u => u.ClientConnections)
      .Include(u => u.Friends)
      .Include(u => u.IsFriendsWith)
      .Include(u => u.Chats)
      .Where(
        u => (
          u.NormalizedUserName!.StartsWith(searchTerm) ||
          u.NormalizedUserName!.Contains($" {searchTerm}") ||
          u.NormalizedUserName!.Contains($"_{searchTerm}") ||
          u.NormalizedUserName!.Contains($"-{searchTerm}")
        ) &&
        !u.Blocking.Any(b => b.Id == userId) &&
        (!u.Chats.Any() || !u.Chats.Any(c => c.Id == searchCall.ChatId)) &&
        u.Id != userId && (  // Ensuring the user doesn't somehow search for themselves by adding an ID filter
          u.PrivacyLevel == 1 || (
            u.PrivacyLevel == 2 && (
              u.Chats.Any(c => c.Users.Any(user => user.Id == userId)) ||
              u.Friends.Any(f => f.Id == userId) ||
              u.IsFriendsWith.Any(f => f.Id == userId)
            )
          ) || (
            u.PrivacyLevel == 3 && (
              u.Friends.Any(f => f.Id == userId) || u.IsFriendsWith.Any(f => f.Id == userId)
            )
          )
        )
      )
      .Select(u => new UserPartialResponse(u))
      .ToListAsync();
    return users;
  }

  async public Task<List<FriendRequest>> GetFriendRequests(string userId) {
    using var ctx = new ChattyBoxContext();
    var requests = await ctx.FriendRequests.Where(f => f.UserBeingAddedId == userId).Include(f => f.UserAdding).ToListAsync();
    return requests;
  }

  async public Task<User> GetAddingUser(string addingId) {
    var addingUser = await _userManager
      .Users
      .Include(u => u.ClientConnections
        .Where(c => (bool)c.Active!)
      )
      .FirstOrDefaultAsync(u => u.Id == addingId);
    ArgumentNullException.ThrowIfNull(addingUser);
    return addingUser;
  }

  async public Task<User?> GetSpecificUser(string userId) {
    return await _userManager.FindByIdAsync(userId);
  }

  async public Task<UserNotificationSetting> GetNotificationSettings(string userId) {
    using var ctx = new ChattyBoxContext();
    var user = await ctx.Users.Include(u => u.UserNotificationSetting).FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    if (user.UserNotificationSetting == null) {
      user.UserNotificationSetting = new UserNotificationSetting {
        PlaySound = true,
        ShowOSNotification = true,
      };
      await ctx.SaveChangesAsync();
    }
    return user.UserNotificationSetting;
  }

  async public Task<LoginAttemptsResponse> GetUserLoginAttempts(string userId, int page = 1) {
    using var ctx = new ChattyBoxContext();
    var attempts = await ctx.UserLoginAttempts
      .Where(ul => ul.UserId == userId)
      .OrderByDescending(ul => ul.AttemptedAt)
      .Skip(15 * (page - 1))
      .Take(15)
      .ToListAsync();
    var total = await ctx.UserLoginAttempts.Where(ul => ul.UserId == userId).CountAsync();
    return new LoginAttemptsResponse {
      UserLoginAttempts = attempts.Select(a => new LoginAttemptPartial(a)).ToList(),
      Count = total
    };
  }

  async public Task<UserDetailedResponse> GetDetailedUserInfo(string requestingUserId, string userId) {
    var user = await _userManager.Users
      .Include(u => u.Friends)
        .ThenInclude(f => f.Friends)
      .Include(u => u.Friends)
        .ThenInclude(f => f.IsFriendsWith)
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.Friends)
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.IsFriendsWith)
      .Include(u => u.FriendRequestsSent)
        .ThenInclude(f => f.UserBeingAdded)
      .Include(u => u.FriendRequestsReceived)
        .ThenInclude(f => f.UserAdding)
      .Include(u => u.Roles)
      .Include(u => u.Blocking)
      .Include(u => u.BlockedBy)
      .Include(u => u.Chats.Where(c => !string.IsNullOrEmpty(c.ChatName)))
        .ThenInclude(c => c.Users)
      .FirstOrDefaultAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    var response = new UserDetailedResponse(user, requestingUserId);
    return response;
  }

  async public Task<User?> GetCompleteUserInfo(string id) {
    return await _userManager.Users
      .Include(u => u.Roles)
      .Include(u => u.Chats)
      .AsSplitQuery()
      .Include(u => u.Friends)
        .ThenInclude(f => f.ClientConnections)
      .AsSplitQuery()
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.ClientConnections)
      .AsSplitQuery()
      .FirstOrDefaultAsync(u => u.Id == id);
  }

  async public Task<UserPersonalInfo> GetUserPersonalInfo(HttpContext httpContext) {
    var userClaim = httpContext.User;
    ArgumentNullException.ThrowIfNull(userClaim);
    var userId = userClaim.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentException.ThrowIfNullOrEmpty(userId);
    var user = await _userManager.Users
      .Include(u => u.Friends)
        .ThenInclude(f => f.ClientConnections)
        .AsSplitQuery()
      .Include(u => u.IsFriendsWith)
        .ThenInclude(f => f.ClientConnections)
        .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Messages)
          .ThenInclude(m => m.From)
          .AsSplitQuery()
      .Include(c => c.Chats)
        .ThenInclude(c => c.SystemMessages)
        .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Messages)
          .ThenInclude(m => m.ReadBy)
          .AsSplitQuery()
      .Include(u => u.Chats)
        .ThenInclude(c => c.Users.Where(u => u.Id != userId))
        .AsSplitQuery()
      .Include(u => u.Blocking)
      .Include(u => u.FriendRequestsReceived)
        .ThenInclude(f => f.UserAdding)
        .AsSplitQuery()
      .Include(u => u.Roles)
      .FirstOrDefaultAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    return new UserPersonalInfo(user);
  }

  async public Task<bool> GetUserMFAEnabled(string userId) {
    var user = await GetUser(userId);
    return await _userManager.GetTwoFactorEnabledAsync(user);
  }

  // Update
  async public Task AddClaim(User user, Claim claim) {
    await _userManager.AddClaimAsync(user, claim);
  }

  async public Task<string> ChangeEmail(ChangeEmailRequest body, HttpContext context) {
    var user = await GetUser(context);
    if (user.NormalizedEmail != body.CurrentEmail.ToUpper())
      throw new InvalidCredentialsException(_localizer.GetString("401Auth"));
    var passwordIsValid = await _userManager.CheckPasswordAsync(user, body.Password);
    if (!passwordIsValid) throw new InvalidCredentialsException(_localizer.GetString("401Auth"));
    // Generate a token for undoing this
    var token = await _userManager.GenerateChangeEmailTokenAsync(user, body.CurrentEmail);
    await _userManager.SetEmailAsync(user, body.NewEmail);
    return token;
  }

  async public Task<User> UndoChangeEmail(string email, string token) {
    var user = await GetUser(email);
    await _userManager.ChangeEmailAsync(user, email, token);
    await _userManager.UpdateSecurityStampAsync(user);
    return user;
  }

  async public Task ToggleMFA(MFADisableRequest request, HttpContext context) {
    var user = await GetUser(context);
    var result = await _userManager.CheckPasswordAsync(user, request.Password);
    if (!result) throw new ForbiddenException(_localizer.GetString("403"));
    var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
    await _userManager.ResetAuthenticatorKeyAsync(user);
    await _userManager.SetTwoFactorEnabledAsync(user, !isEnabled);
  }

  async public Task ToggleMFA(User user, bool enable) {
    await _userManager.SetTwoFactorEnabledAsync(user, enable);
  }

  async public Task<(string?, string[]?)> GenerateMFACodes(User user) {
    var key = await _userManager.GetAuthenticatorKeyAsync(user);
    var results = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
    if (results is null) return (null, null);
    var token = $"otpauth://totp/ChattyBox:{user.Email}?secret={key}";
    var recoveryCodes = results.Where(r => r != token).ToArray();
    return (token, recoveryCodes);
  }

  async public Task<User> UpdatePrivateLevel(string userId, int privacyLevel) {
    var user = await GetUser(userId);
    user.PrivacyLevel = privacyLevel;
    await _userManager.UpdateAsync(user);
    return user;
  }

  async public Task<User> SetAvatarToDefault(HttpContext context) {
    var user = await GetUser(context);
    ArgumentException.ThrowIfNullOrEmpty(user.Avatar);
    await _fileService.DeleteFile(user.Avatar);
    using (var ctx = new ChattyBoxContext()) {
      user.Chats = await ctx.Chats.Where(c => c.Users.Any(u => u.Id == user.Id)).ToListAsync();
    }

    user.Avatar = await FileService.GetDefaultAvatar(user);
    await _userManager.UpdateAsync(user);
    return user;
  }

  async public Task<(string, string, List<string>)> ChangeAvatar(IFormFile file, HttpContext context) {
    var user = await GetUser(context);
    var avatar = await _fileService.SaveImage(file, user, isAvatar: true);
    user.Avatar = avatar;
    await _userManager.UpdateAsync(user);
    using var ctx = new ChattyBoxContext();
    var groupsToNotify = await ctx.Chats.Where(c => c.Users.Any(u => u.Id == user.Id)).Select(c => c.Id).ToListAsync();
    groupsToNotify.Add($"{user.Id}_friends");
    return (user.Id, avatar, groupsToNotify);
  }

  async public Task SignOut(HttpContext context, bool invalidateAllSessions) {
    var user = await GetUser(context);
    if (invalidateAllSessions) await _userManager.UpdateSecurityStampAsync(user);
  }

  async public Task<IdentityResult> ChangePassword(ChangePasswordRequest body, HttpContext context) {
    var user = await GetUser(context);
    var result = await _userManager.ChangePasswordAsync(user, body.CurrentPassword, body.NewPassword);
    return result;
  }

  async public Task UpdateLoginAttempt(string userId) {
    using var ctx = new ChattyBoxContext();
    var loginAttempt = await ctx.UserLoginAttempts.OrderBy(l => l.AttemptedAt).FirstAsync(l => l.UserId == userId);
    loginAttempt.Success = true;
    await ctx.SaveChangesAsync();
  }

  async public Task<string> GeneratePasswordResetToken(User user) {
    return await _userManager.GeneratePasswordResetTokenAsync(user);
  }

  async public Task<IdentityResult> ResetPassword(User user, PasswordResetRequest request) {
    return await _userManager.ResetPasswordAsync(user, request.Token, request.Password);
  }

  async public Task<FriendResponse?> HandleFriendRequest(string userId, string addingId, bool accepting) {
    using var ctx = new ChattyBoxContext();
    ctx.FriendRequests.Remove(
      await ctx.FriendRequests.FirstAsync(f => f.UserBeingAddedId == userId && f.UserAddingId == addingId)
    );
    FriendResponse? FriendResponse = null;
    if (accepting) {
      var adding = await ctx.Users.FirstAsync(u => u.Id == addingId);
      var user = await ctx.Users.FirstAsync(u => u.Id == userId);
      adding.Friends.Add(user);
      user.IsFriendsWith.Add(adding);
      FriendResponse = new FriendResponse(user, true, addingId);
    }
    await ctx.SaveChangesAsync();
    return FriendResponse;
  }

  async public Task<UserNotificationSetting> UpdateUserNotificationSettings(string userId, bool playSound, bool showOSNotification, bool showAlert) {
    using var ctx = new ChattyBoxContext();
    var settings = await ctx.UserNotificationSettings.FirstAsync(n => n.UserId == userId);
    settings.PlaySound = playSound;
    settings.ShowOSNotification = showOSNotification;
    settings.ShowAlert = showAlert;
    await ctx.SaveChangesAsync();
    return settings;
  }

  async public Task<UserDetailedResponse> ToggleUserBlocked(string mainUserId, string userBeingBlockedId) {
    var mainUser = await _userManager.Users
      .Include(u => u.Blocking)
      .Include(u => u.Friends)
      .FirstAsync(u => u.Id == mainUserId);
    ArgumentNullException.ThrowIfNull(mainUser);
    var userBeingBlocked = await _userManager.Users.Include(u => u.Friends).FirstAsync(u => u.Id == userBeingBlockedId);
    ArgumentNullException.ThrowIfNull(userBeingBlocked);
    if (mainUser.Blocking.Contains(userBeingBlocked)) mainUser.Blocking.Remove(userBeingBlocked);
    else {
      mainUser.Blocking.Add(userBeingBlocked);
      if (mainUser.Friends.Contains(userBeingBlocked))
        mainUser.Friends.Remove(userBeingBlocked);
      else if (userBeingBlocked.Friends.Contains(mainUser))
        userBeingBlocked.Friends.Remove(mainUser);
    };
    await _userManager.UpdateAsync(mainUser);
    await _userManager.UpdateAsync(userBeingBlocked);
    return new UserDetailedResponse(userBeingBlocked, mainUserId);
  }

  async public Task RemoveFriend(string userId, string friendId) {
    var user = await _userManager.Users
      .Include(u => u.Friends)
      .Include(u => u.IsFriendsWith)
      .FirstAsync(u => u.Id == userId);
    var friend = await _userManager.FindByIdAsync(friendId);
    ArgumentNullException.ThrowIfNull(friend);
    user.Friends.Remove(friend);
    user.IsFriendsWith.Remove(friend);
    await _userManager.UpdateAsync(user);
  }

  async public Task<string?> UpdateStatus(string userId, string? status) {
    var user = await _userManager.FindByIdAsync(userId);
    ArgumentNullException.ThrowIfNull(user);
    user.Status = status;
    await _userManager.UpdateAsync(user);
    return status;
  }

  async public Task<IdentityResult> AddRole(string userId, string roleName) {
    var user = await GetUser(userId);
    if (!await _roleManager.RoleExistsAsync(roleName)) {
      await _roleManager.CreateAsync(new Role { Name = roleName });
    }
    return await _userManager.AddToRoleAsync(user, roleName);
  }

  async public Task<IdentityResult> AddRole(User user, string roleName) {
    if (!await _roleManager.RoleExistsAsync(roleName)) {
      await _roleManager.CreateAsync(new Role { Name = roleName });
    }
    return await _userManager.AddToRoleAsync(user, roleName);
  }

  async public Task RemoveClaim(string userId, Claim claim) {
    var user = await GetUser(userId);
    await _userManager.RemoveClaimAsync(user, claim);
  }

  async public Task RemoveClaim(User user, Claim claim) {
    await _userManager.RemoveClaimAsync(user, claim);
  }

  async public Task RemoveClaim(User user, string claimName) {
    var claims = await _userManager.GetClaimsAsync(user);
    var claim = claims.FirstOrDefault(c => c.Type == claimName);
    ArgumentNullException.ThrowIfNull(claim);
    await _userManager.RemoveClaimAsync(user, claim);
  }

  async public Task<User> ChangeUsername(string userId, string userName) {
    var user =
      await _userManager.Users
      .Include(u => u.Chats)
      .FirstOrDefaultAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    user.UserName = userName;
    await _userManager.UpdateAsync(user);
    return user;
  }
}