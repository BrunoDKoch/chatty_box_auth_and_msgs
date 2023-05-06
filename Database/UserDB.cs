using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Database;

public class UserDB {

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  public UserDB(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
  }
  // Create
  async public Task<FriendRequest?> CreateFriendRequest(string addingId, string addedId) {
    using var ctx = new ChattyBoxContext();
    try {
      var friendRequest = new FriendRequest {
        UserAddingId = addingId,
        UserBeingAddedId = addedId,
        SentAt = DateTime.UtcNow,
      };
      await ctx.FriendRequests.AddAsync(friendRequest);
      await ctx.SaveChangesAsync();
      return friendRequest;
    } catch (Exception e) {
      Console.Error.Write(e);
      return null;
    }
  }

  // Read
  async public Task<List<FriendsResponse>> GetAnUsersFriends(string userId) {
    var user = await _userManager.Users
      .Include(u => u.Friends).ThenInclude(f => f.Connection)
      .Include(u => u.IsFriendsWith).ThenInclude(f => f.Connection)
      .FirstAsync(u => u.Id == userId);
    List<FriendsResponse> friends = new List<FriendsResponse>();
    List<User> userFriends = new List<User>();
    userFriends = user.Friends.Concat(user.IsFriendsWith).ToList();
    foreach (var friend in userFriends) {
      if (friend.Connection != null) {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          Id = friend.Id,
          IsOnline = true,
          Avatar = friend.Avatar,
        });
      } else {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          Id = friend.Id,
          IsOnline = false,
          Avatar = friend.Avatar,
        });
      }
    }
    return friends;
  }

  async public Task<List<UserPartialResponse>> GetUsers(string userId, string userName) {
    // Ensuring the user doesn't somehow search for themselves by adding an ID filter
    var currentUser = await _userManager.FindByIdAsync(userId);
    var users = await _userManager.Users
      .Include(u => u.Connection)
      .Include(u => u.Friends)
      .Include(u => u.IsFriendsWith)
      .Include(u => u.Chats)
      .Where(
        u => u.NormalizedUserName!.StartsWith(userName.ToUpper()) &&
        u.Id != userId && (
          u.PrivacyLevel == 1 || (
            u.PrivacyLevel == 2 && (
              u.Chats.Any(c => c.Users.Contains(currentUser!)) ||
              u.Friends.Contains(currentUser!) ||
              u.IsFriendsWith.Contains(currentUser!)
            )
          ) || (
            u.PrivacyLevel == 3 && (
              u.Friends.Contains(currentUser!) || u.IsFriendsWith.Contains(currentUser!)
            )
          )
        )
      )
      .Select(u => new UserPartialResponse {
        Id = u.Id,
        UserName = u.UserName!,
        Avatar = u.Avatar
      })
      .ToListAsync();
    return users;
  }

  async public Task<List<FriendRequest>> GetFriendRequests(string userId) {
    using var ctx = new ChattyBoxContext();
    var requests = await ctx.FriendRequests.Where(f => f.UserBeingAddedId == userId).Include(f => f.UserAdding).ToListAsync();
    return requests;
  }

  async public Task<User?> GetSpecificUser(string userId) {
    return await _userManager.FindByIdAsync(userId);
  }

  async public Task<UserNotificationSetting?> GetNotificationSettings(string userId) {
    using var ctx = new ChattyBoxContext();
    var user = await ctx.Users.Include(u => u.UserNotificationSetting).FirstAsync(u => u.Id == userId);
    if (user == null) return null;
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
      .OrderBy(ul => ul.AttemptedAt)
      .Skip(15 * (page - 1))
      .Take(15)
      .ToListAsync();
    var total = await ctx.UserLoginAttempts.Where(ul => ul.UserId == userId).CountAsync();
    return new LoginAttemptsResponse {
      UserLoginAttempts = attempts,
      Count = total
    };
  }

  async public Task<UserDetailedResponse?> GetDetailedUserInfo(string requestingUserId, string userId) {
    var user = await _userManager.Users
      .Include(u => u.Friends)
      .ThenInclude(f => f.Friends)
      .Include(u => u.Friends)
      .ThenInclude(f => f.IsFriendsWith)
      .Include(u => u.IsFriendsWith)
      .ThenInclude(f => f.Friends)
      .Include(u => u.IsFriendsWith)
      .ThenInclude(f => f.IsFriendsWith)
      .Include(u => u.Blocking)
      .Include(u => u.Chats)
      .FirstAsync(u => u.Id == userId && !u.Blocking.Any(b => b.Id == requestingUserId));
    if (user == null) return null;
    return new UserDetailedResponse(user, requestingUserId);
  }

  // Update

  async public Task HandleFriendRequest(string userId, string addingId, bool accepting) {
    using var ctx = new ChattyBoxContext();
    ctx.FriendRequests.Remove(
      await ctx.FriendRequests.FirstAsync(f => f.UserBeingAddedId == userId && f.UserAddingId == addingId)
    );
    if (accepting) {
      var adding = await ctx.Users.FirstAsync(u => u.Id == addingId);
      var user = await ctx.Users.FirstAsync(u => u.Id == userId);
      adding.Friends.Add(user);
      user.IsFriendsWith.Add(adding);
    }
    await ctx.SaveChangesAsync();
  }

  async public Task<UserNotificationSetting> UpdateUserNotificationSettings(string userId, bool playSound, bool showOSNotification) {
    using var ctx = new ChattyBoxContext();
    var settings = await ctx.UserNotificationSettings.FirstAsync(n => n.UserId == userId);
    settings.PlaySound = playSound;
    settings.ShowOSNotification = showOSNotification;
    await ctx.SaveChangesAsync();
    return settings;
  }

  async public Task<bool?> ToggleUserBlocked(string mainUserId, string userBeingBlockedId) {
    var mainUser = await _userManager.Users.Include(u => u.Blocking).FirstAsync(u => u.Id == mainUserId);
    var userBeingBlocked = await _userManager.FindByIdAsync(userBeingBlockedId);
    if (mainUser == null || userBeingBlocked == null) return null;
    if (mainUser.Blocking.Contains(userBeingBlocked)) mainUser.Blocking.Remove(userBeingBlocked);
    else mainUser.Blocking.Add(userBeingBlocked);
    await _userManager.UpdateAsync(mainUser);
    return mainUser.Blocking.Contains(userBeingBlocked);
  }
}