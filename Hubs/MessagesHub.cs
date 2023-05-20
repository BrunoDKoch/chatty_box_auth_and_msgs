using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;
using ChattyBox.Misc;
using ChattyBox.Models;
using Microsoft.AspNetCore.Identity;

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

  async private Task HandleTyping(string fromId, string chatId, bool isTyping) {
    var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
    if (connections == null || connections.Count() == 0) return;
    var from = await _userManager.FindByIdAsync(fromId);
    if (from == null) return;
    foreach (var connection in connections) {
      if (connection.UserId == fromId) continue;
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { from = from.UserName, isTyping, chatId }, default);
    }
  }

  async public override Task OnConnectedAsync() {
    try {
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
    } catch (ArgumentNullException) {
      return;
    } finally {
      await base.OnConnectedAsync();
    }
  }

  async public override Task OnDisconnectedAsync(Exception? exception) {
    if (exception != null) {
      Console.Error.WriteLine(exception);
    }
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
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
    var userId = this.Context.UserIdentifier;
    if (!_signInManager.IsSignedIn(this.Context.User)) {
      await Clients.Caller.SendAsync("notConnected", default);
      return;
    }
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
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var count = await _messagesDB.CountUnreadMessages(userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("unread", count, default);
  }

  async public Task StartTyping(string chatId) {
    var fromId = this.Context.UserIdentifier;
    if (fromId == null) return;
    await HandleTyping(fromId, chatId, true);
  }

  async public Task StopTyping(string chatId) {
    var fromId = this.Context.UserIdentifier;
    if (fromId == null) return;
    await HandleTyping(fromId, chatId, false);
  }

  async public Task SendMessage(string chatId, string text, string? replyToId) {
    var fromId = this.Context.UserIdentifier;
    if (fromId == null) return;
    try {
      var message = await _messagesDB.CreateMessage(fromId, chatId, text, replyToId);
      if (message == null) throw new Exception();
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
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    await _messagesDB.MarkAsRead(id, userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("read", id, default);
  }

  async public Task GetChatPreviews() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var messages = await _messagesDB.GetChatPreview(userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("previews", messages, default);
  }
  // Blocked users
  async public Task GetBlockedUsers() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var blocked = await _userDB.GetBlockedUsers(userId);
    await Clients.Caller.SendAsync("blockedUsers", blocked, default);
  }

  // Friends logic
  async public Task GetFriends() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var friends = await _userDB.GetAnUsersFriends(userId);
    await Clients.Caller.SendAsync("friends", friends, default);
  }

  async public Task SearchUser(string userName) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var results = await _userDB.GetUsers(userId, userName);
    await Clients.Caller.SendAsync("searchResults", results, default);
  }

  async public Task SendFriendRequest(string addedId) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var friendRequest = await _userDB.CreateFriendRequest(userId, addedId);
    if (friendRequest == null) return;
    await Clients.Caller.SendAsync("added", friendRequest, default);
    var addedConnection = await _messagesDB.GetClientConnection(addedId);
    if (addedConnection == null) return;
    try {
      await Clients.Client(addedConnection.ConnectionId).SendAsync("newFriendRequest", friendRequest, default);
    } catch {
      return;
    }
  }

  async public Task GetFriendRequests() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var requests = await _userDB.GetFriendRequests(userId);
    await Clients.Caller.SendAsync("pendingRequests", requests, default);
  }

  async public Task RespondToFriendRequest(string addingId, bool accept) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    await _userDB.HandleFriendRequest(userId, addingId, accept);
    if (accept) {
      var friends = await _userDB.GetAnUsersFriends(userId);
      await Clients.Caller.SendAsync("friends", friends, default);
    }
  }

  // User details
  async public Task GetUserDetails(string userId) {
    var requestingUserId = this.Context.UserIdentifier;
    if (requestingUserId == null) return;
    var details = await _userDB.GetDetailedUserInfo(requestingUserId, userId);
    if (details == null) return;
    await Clients.Caller.SendAsync("userDetails", details, default);
  }

  // Chat creation
  async public Task CreateNewChat(List<string> userIds, string? name, int? maxUsers) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var chat = await _messagesDB.CreateChat(userId, userIds, name, maxUsers);
    foreach (var connection in await _messagesDB.GetAllConnectionsToChat(chat.Id)) {
      await Clients.Client(connection.ConnectionId).SendAsync("newChat", chat, default);
    }
  }

  // Fetching chat
  async public Task GetChat(string chatId, int skip = 0) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var chat = await _messagesDB.GetChatDetails(userId, chatId, skip);
    await Clients.Caller.SendAsync("chat", chat, default);
  }

  // Searching chat
  async public Task SearchChat(string chatId, string? search, DateTime? startDate, DateTime? endDate, List<string> userIds, int skip) {
    var mainUserId = this.Context.UserIdentifier;
    if (mainUserId == null) return;
    var results = await _messagesDB.GetChatMessagesFromSearch(chatId, search, startDate, endDate, userIds, skip, mainUserId);
    if (results == null) return;
    await Clients.Caller.SendAsync("chatSearchResults", new { messages = results.Messages, messageCount = results.MessageCount }, default);
  }

  // User settings
  async public Task GetNotificationSettings() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var settings = await _userDB.GetNotificationSettings(userId);
    await Clients.Caller.SendAsync("notificationSettings", settings, default);
  }

  async public Task UpdateNotificationSettings(bool playSound, bool showOSNotification) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var settings = await _userDB.UpdateUserNotificationSettings(userId, playSound, showOSNotification);
    await Clients.Caller.SendAsync("notificationSettings", settings, default);
  }

  // Security
  async public Task GetLoginAttempts(int page = 1) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var attempts = await _userDB.GetUserLoginAttempts(userId, page);
    await Clients.Caller.SendAsync("loginAttempts", new { attempts = attempts.UserLoginAttempts, count = attempts.Count }, default);
  }

  async public Task GetMFASettings() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var user = await _userManager.FindByIdAsync(userId);
    if (user == null) return;
    var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
    var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
    await Clients.Caller.SendAsync("currentMFAOptions", new { isEnabled, providers }, default);
  }

  async public Task SetMFASettings(bool enable) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
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
  }

  // Privacy
  async public Task SetPrivacySettings(int privacyLevel) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var user = await _userManager.FindByIdAsync(userId);
    if (user == null) return;
    user.PrivacyLevel = privacyLevel;
    await _userManager.UpdateAsync(user);
    await Clients.Caller.SendAsync("privacyLevel", privacyLevel, default);
  }

  async public Task GetPrivacySettings() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var user = await _userManager.FindByIdAsync(userId);
    if (user == null) return;
    if (user.PrivacyLevel == 0) {
      user.PrivacyLevel = 1;
      await _userManager.UpdateAsync(user);
    }
    await Clients.Caller.SendAsync("privacyLevel", user.PrivacyLevel, default);
  }

  // Blocking
  async public Task ToggleBlock(string userToBlockId) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var blocked = await _userDB.ToggleUserBlocked(userId, userToBlockId);
    await Clients.Caller.SendAsync("blockToggle", new { id = userToBlockId, blocked }, default);
  }

  // Change username
  async public Task ChangeUserName(string userName) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var user = await _userManager.FindByIdAsync(userId);
    if (user == null) return;
    user.UserName = userName;
    await _userManager.UpdateAsync(user);
    await Clients.Caller.SendAsync("userName", user.UserName, default);
  }
}