using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;
using ChattyBox.Misc;
using ChattyBox.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;

namespace ChattyBox.Hubs;

public class MessagesHub : Hub {
  private List<string> _validLetters = ValidCharacters.GetLetters().Split().ToList();

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  private MessagesDB _messagesDB;
  private UserDB _userDB;

  public MessagesHub(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _userDB = new UserDB(_userManager, _roleManager, _configuration, _signInManager);
    _messagesDB = new MessagesDB(_userManager, _roleManager, _configuration, _signInManager);
  }

  private void EnsureUserIdNotNull(string userId) {
    if (string.IsNullOrEmpty(userId)) {
      throw new ArgumentNullException(nameof(userId));
    }
  }

  async private Task HandleException(Func<Task> action, Func<Task>? finalAction = null) {
    if (finalAction == null) {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        throw;
      }
    } else {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        throw;
      } finally {
        await finalAction();
      }
    }
  }

  async private Task HandleTyping(string fromId, string chatId, bool isTyping) {
    var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
    if (connections == null || connections.Count() == 0) return;
    var from = await _userManager.FindByIdAsync(fromId);
    ArgumentNullException.ThrowIfNull(from);
    foreach (var connection in connections) {
      if (connection.UserId == fromId) continue;
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { from = from.UserName, isTyping, chatId }, default);
    }
  }

  async public override Task OnConnectedAsync() {
    await HandleException(
      async () => {
        var id = this.Context.UserIdentifier;
        ArgumentNullException.ThrowIfNullOrEmpty(id);
        var userConnection = await _messagesDB.CreateConnection(id, this.Context.ConnectionId);
        var friends = await _userDB.GetAnUsersFriends(id);
        await Clients.Caller.SendAsync("friends", friends, default);
        foreach (var friend in friends) {
          var connection = await _messagesDB.GetClientConnection(friend.Id);
          if (connection == null) continue;
          await Clients.Client(connection.ConnectionId).SendAsync("updateStatus", id, default);
        }
      },
      async () => await base.OnConnectedAsync()
    );
  }

  async public override Task OnDisconnectedAsync(Exception? exception) {
    if (exception != null) {
      Console.Error.WriteLine(exception);
    }
    var userId = this.Context.UserIdentifier;
    ArgumentNullException.ThrowIfNullOrEmpty(userId);
    var relevantConnections = await _messagesDB.DeleteConnection(userId);
    if (relevantConnections != null) {
      foreach (var connection in relevantConnections) {
        await Clients.Client(connection).SendAsync("updateStatus", userId, default);
      }
    }
    await base.OnDisconnectedAsync(exception);
  }

  // Start up
  async public Task InitialCall() {
    await this.GetChatPreviews();
    await this.GetFriends();
    await this.GetFriendRequests();
    await this.GetNotificationSettings();
    await this.GetBlockedUsers();
  }

  async public Task GetCallerInfo() {
    try {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      await Clients.Caller.SendAsync("userInfo", new { UserName = user.UserName, Avatar = user.Avatar }, default);
    } catch (ArgumentNullException) {
      return;
    }
  }

  async public Task GetUnreadCount() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var count = await _messagesDB.CountUnreadMessages(userId);
      await Clients.Client(this.Context.ConnectionId).SendAsync("unread", count, default);
    });
  }

  async public Task StartTyping(string chatId) {
    await HandleException(async () => {
      var fromId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, true);
    });
  }

  async public Task StopTyping(string chatId) {
    await HandleException(async () => {
      var fromId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, false);
    });
  }

  async public Task SendMessage(string chatId, string text, string? replyToId) {
    var fromId = this.Context.UserIdentifier;
    ArgumentNullException.ThrowIfNullOrEmpty(fromId);
    try {
      var message = await _messagesDB.CreateMessage(fromId, chatId, text, replyToId);
      ArgumentNullException.ThrowIfNull(message);
      var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
      foreach (var connection in connections) {
        if (connection.UserId == fromId) {
          message.IsFromCaller = true;
        } else {
          message.IsFromCaller = false;
        }
        await Clients.Client(connection.ConnectionId).SendAsync("newMessage", message, default);
      }
    } catch (Exception e) {
      Console.WriteLine(e);
      await Clients.Caller.SendAsync("msgError", default);
    }
  }

  async public Task MarkAsRead(string id) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      await _messagesDB.MarkAsRead(id, userId);
      await Clients.Client(this.Context.ConnectionId).SendAsync("read", id, default);
    });
  }

  async public Task GetChatPreviews() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var messages = await _messagesDB.GetChatPreview(userId);
      await Clients.Client(this.Context.ConnectionId).SendAsync("previews", messages, default);
    });
  }
  // Blocked users
  async public Task GetBlockedUsers() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var blocked = await _userDB.GetBlockedUsers(userId);
      await Clients.Caller.SendAsync("blockedUsers", blocked, default);
    });
  }

  // Friends logic
  async public Task GetFriends() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var friends = await _userDB.GetAnUsersFriends(userId);
      await Clients.Caller.SendAsync("friends", friends, default);
    });
  }

  async public Task SearchUser(string userName) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var results = await _userDB.GetUsers(userId, userName);
      await Clients.Caller.SendAsync("searchResults", results, default);
    });
  }

  async public Task SendFriendRequest(string addedId) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var friendRequest = await _userDB.CreateFriendRequest(userId, addedId);
      if (friendRequest == null) return;
      await Clients.Caller.SendAsync("added", friendRequest, default);
      var addedConnection = await _messagesDB.GetClientConnection(addedId);
      if (addedConnection == null) return;
      await Clients.Client(addedConnection.ConnectionId).SendAsync("newFriendRequest", friendRequest, default);
    });
  }

  async public Task GetFriendRequests() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var requests = await _userDB.GetFriendRequests(userId);
      await Clients.Caller.SendAsync("pendingRequests", requests, default);
    });
  }

  async public Task RespondToFriendRequest(string addingId, bool accept) {
    await HandleException(async () => {
        var userId = this.Context.UserIdentifier;
        ArgumentNullException.ThrowIfNullOrEmpty(userId);
        await _userDB.HandleFriendRequest(userId, addingId, accept);
        if (accept) {
          var friends = await _userDB.GetAnUsersFriends(userId);
          await Clients.Caller.SendAsync("friends", friends, default);
        }
    });
  }

  // User details
  async public Task GetUserDetails(string userId) {
    await HandleException(async () => {
      var requestingUserId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNull(requestingUserId);
      var details = await _userDB.GetDetailedUserInfo(requestingUserId, userId);
      ArgumentNullException.ThrowIfNull(details);
      await Clients.Caller.SendAsync("userDetails", details, default);
    });
  }

  // Chat creation
  async public Task CreateNewChat(List<string> userIds, string? name, int? maxUsers) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var chat = await _messagesDB.CreateChat(userId, userIds, name, maxUsers);
      foreach (var connection in await _messagesDB.GetAllConnectionsToChat(chat.Id)) {
        await Clients.Client(connection.ConnectionId).SendAsync("newChat", chat, default);
      }
    });
  }

  // Fetching chat
  async public Task GetChat(string chatId, int skip = 0) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var chat = await _messagesDB.GetChatDetails(userId, chatId, skip);
      await Clients.Caller.SendAsync("chat", chat, default);
    });
  }

  // Searching chat
  async public Task SearchChat(string chatId, string? search, DateTime? startDate, DateTime? endDate, List<string> userIds, int skip) {
    await HandleException(async () => {
      var mainUserId = this.Context.UserIdentifier;
      if (mainUserId == null) return;
      var results = await _messagesDB.GetChatMessagesFromSearch(chatId, search, startDate, endDate, userIds, skip, mainUserId);
      if (results == null) return;
      await Clients.Caller.SendAsync("chatSearchResults", new { messages = results.Messages, messageCount = results.MessageCount }, default);
    });
  }

  // User settings
  async public Task GetNotificationSettings() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var settings = await _userDB.GetNotificationSettings(userId);
      await Clients.Caller.SendAsync("notificationSettings", settings, default);
    });
  }

  async public Task UpdateNotificationSettings(bool playSound, bool showOSNotification) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var settings = await _userDB.UpdateUserNotificationSettings(userId, playSound, showOSNotification);
      await Clients.Caller.SendAsync("notificationSettings", settings, default);
    });
  }

  // Security
  async public Task GetLoginAttempts(int page = 1) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var attempts = await _userDB.GetUserLoginAttempts(userId, page);
      await Clients.Caller.SendAsync("loginAttempts", new { attempts = attempts.UserLoginAttempts, count = attempts.Count }, default);
    });
  }

  async public Task GetMFASettings() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      if (user == null) return;
      var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
      var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
      await Clients.Caller.SendAsync("currentMFAOptions", new { isEnabled, providers }, default);
    });
  }

  async public Task SetMFASettings(bool enable) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      if (user == null) return;
      await _userManager.ResetAuthenticatorKeyAsync(user);
      if (enable) {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        var results = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (results == null) return;
        var token = $"otpauth://totp/ChattyBox:{user.Email}?secret={key}";
        var recoveryCodes = results.Where(r => r != token).ToArray();
        await Clients.Caller.SendAsync("mfaToken", new { token, recoveryCodes }, default);
      }
      var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
      await _userManager.SetTwoFactorEnabledAsync(user, enable);
      await Clients.Caller.SendAsync("currentMFAOptions", new { isEnabled = enable, providers }, default);
    });
  }

  // Privacy
  async public Task SetPrivacySettings(int privacyLevel) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      if (user == null) return;
      user.PrivacyLevel = privacyLevel;
      await _userManager.UpdateAsync(user);
      await Clients.Caller.SendAsync("privacyLevel", privacyLevel, default);
    });
  }

  async public Task GetPrivacySettings() {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      if (user == null) return;
      if (user.PrivacyLevel == 0) {
        user.PrivacyLevel = 1;
        await _userManager.UpdateAsync(user);
      }
      await Clients.Caller.SendAsync("privacyLevel", user.PrivacyLevel, default);
    });
  }

  // Blocking
  async public Task ToggleBlock(string userToBlockId) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var blocked = await _userDB.ToggleUserBlocked(userId, userToBlockId);
      await Clients.Caller.SendAsync("blockToggle", new { id = userToBlockId, blocked }, default);
    });
  }

  // Change username
  async public Task ChangeUserName(string userName) {
    await HandleException(async () => {
      var userId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(userId);
      var user = await _userManager.FindByIdAsync(userId);
      if (user == null) return;
      user.UserName = userName;
      await _userManager.UpdateAsync(user);
      await Clients.Caller.SendAsync("userName", user.UserName, default);
    });
  }

  // Add & remove users
  async public Task AddUserToChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNull(requestingUserId);
      var chat = await _messagesDB.AddUserToChat(userId, requestingUserId, chatId);
      await Clients.Caller.SendAsync("chat", chat, default);
    });
  }
  async public Task RemoveUserFromChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNull(requestingUserId);
      var chat = await _messagesDB.RemoveUserFromChat(userId, requestingUserId, chatId);
      await Clients.Caller.SendAsync("chat", chat, default);
    });
  }
}