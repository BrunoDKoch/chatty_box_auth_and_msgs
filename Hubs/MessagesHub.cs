using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;
using ChattyBox.Misc;
using ChattyBox.Models;
using ChattyBox.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using UAParser;

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
      SignInManager<User> signInManager,
      MaxMind.GeoIP2.WebServiceClient maxMindClient) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _userDB = new UserDB(_userManager, _roleManager, _configuration, _signInManager);
    _messagesDB = new MessagesDB(_userManager, _roleManager, _configuration, _signInManager, maxMindClient);
  }

  private string EnsureUserIdNotNull(string? userId) {
    ArgumentNullException.ThrowIfNull(userId);
    return userId;
  }

  async private Task AddToFriendsGroup(string userId, List<string> clientConnectionIds) {
    foreach (var clientConnection in clientConnectionIds)
      await Groups.AddToGroupAsync(clientConnection, $"{userId}_friends", default);
  }

  async private Task HandleException(Func<Task> action, Func<Task>? finalAction = null, ExceptionActionType actionType = ExceptionActionType.OTHER) {
    string errorToSend = actionType == ExceptionActionType.MESSAGE ? "msgError" : "error";
    if (finalAction is null) {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        await Clients.Caller.SendAsync(errorToSend, e.Message, default);
      }
    } else {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        await Clients.Caller.SendAsync(errorToSend, e.Message, default);
      } finally {
        await finalAction();
      }
    }
  }

  async private Task<T?> HandleException<T>(Func<Task<T>> action) {
    try {
      var result = await action();
      return result;
    } catch (Exception e) {
      Console.ForegroundColor = ConsoleColor.DarkRed;
      Console.Error.WriteLine(e);
      Console.ForegroundColor = ConsoleColor.White;
      await Clients.Caller.SendAsync("error", e.Message, default);
      return default(T);
    }
  }

  async private Task HandleTyping(string fromId, string chatId, string connectionId, bool isTyping) {
    await HandleException(async () => {
      var from = await _userManager.FindByIdAsync(fromId);
      ArgumentNullException.ThrowIfNull(from);
      await Clients.GroupExcept(chatId, connectionId).SendAsync("typing", new { from = from.UserName, isTyping, chatId }, default);
    });
  }

  async public override Task OnConnectedAsync() {
    await HandleException(
      async () => {
        var id = EnsureUserIdNotNull(this.Context.UserIdentifier);
        var httpContext = this.Context.GetHttpContext();
        ArgumentNullException.ThrowIfNull(httpContext);
        var userConnection = await _messagesDB.CreateConnection(id, this.Context.ConnectionId, httpContext);
        // Create a group to send messages to all of a client's connected devices
        await Groups.AddToGroupAsync(this.Context.ConnectionId, $"{id}_connections", default);
        var friends = await _userDB.GetAnUsersFriends(id);
        var status = await GetStatus();
        await this.GetChatPreviews();
        await this.GetFriends(status);
        await this.GetFriendRequests();
        await this.GetNotificationSettings();
        await this.GetBlockedUsers();
        await Clients.Caller.SendAsync(
          "friends",
          friends.Select(f => new FriendsResponse(f, id)),
          default
        );


      },
      async () => await base.OnConnectedAsync()
    );
  }

  async public override Task OnDisconnectedAsync(Exception? exception) {
    await HandleException(async () => {
      if (exception != null) {
        Console.Error.WriteLine(exception);
      }
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      await _messagesDB.MarkConnectionAsInactive(userId, this.Context.ConnectionId);
      await Clients.Group($"{userId}_friends").SendAsync("updateStatus", new { id = userId, status = String.Empty, online = false }, default);
      await Groups.RemoveFromGroupAsync(this.Context.ConnectionId, $"{userId}_connections", default);
    },
      async () => await base.OnDisconnectedAsync(exception)
    );
  }

  async public Task GetCallerInfo() {
    try {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      await Clients.Caller.SendAsync("userInfo", new { UserName = user.UserName, Avatar = user.Avatar }, default);
    } catch (ArgumentNullException) {
      return;
    }
  }

  async public Task GetUnreadCount() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var count = await _messagesDB.CountUnreadMessages(userId);
      await Clients.Client(this.Context.ConnectionId).SendAsync("unread", count, default);
    });
  }

  // Handle messages
  async public Task StartTyping(string chatId) {
    await HandleException(async () => {
      var fromId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, this.Context.ConnectionId, true);
    });
  }

  async public Task StopTyping(string chatId) {
    await HandleException(async () => {
      var fromId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, this.Context.ConnectionId, false);
    });
  }

  async public Task SendMessage(string chatId, string text, string? replyToId) {
    await HandleException(async () => {
      var fromId = this.Context.UserIdentifier;
      ArgumentNullException.ThrowIfNullOrEmpty(fromId);
      var message = await _messagesDB.CreateMessage(fromId, chatId, text, replyToId);
      ArgumentNullException.ThrowIfNull(message);
      await Clients.Caller.SendAsync("newMessage", message, default);
      message.IsFromCaller = false;
      await Clients.GroupExcept(chatId, this.Context.ConnectionId).SendAsync("newMessage", message, default);

    },
      actionType: ExceptionActionType.MESSAGE
    );
  }

  async public Task MarkAsRead(string id) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var readMessage = await _messagesDB.MarkAsRead(id, userId);
      if (readMessage is null || readMessage.ConnectionIds is null || readMessage.ConnectionIds.Count == 0) return;
      await Clients.Clients(readMessage.ConnectionIds).SendAsync("read", new { id, readBy = readMessage.ReadMessage }, default);
    });
  }

  async public Task EditMessage(string messageId, string text) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var message = await _messagesDB.EditMessage(userId, messageId, text);
      await Clients.Group(message.ChatId).SendAsync("editedMessage", message, default);
    });
  }

  async public Task GetSpecificMessage(string messageId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var message = await _messagesDB.GetSpecificMessage(userId, messageId);
      await Clients.Group(message.ChatId).SendAsync("specificMessage", message, default);
    });
  }

  async public Task<bool> DeleteMessage(string messageId, string chatId) {
    return await HandleException<bool>(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      await _messagesDB.DeleteMessage(messageId, chatId, userId);
      return true;
    });
  }

  // Get previews
  async public Task GetChatPreviews() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var previews = await _messagesDB.GetChatPreview(userId);
      foreach (var preview in previews) {
        await Groups.AddToGroupAsync(this.Context.ConnectionId, preview.Id);
      }
      await Clients.Client(this.Context.ConnectionId).SendAsync("previews", previews, default);
    });
  }
  // Blocked users
  async public Task GetBlockedUsers() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var blocked = await _userDB.GetBlockedUsers(userId);
      await Clients.Caller.SendAsync("blockedUsers", blocked, default);
    });
  }

  // Friends logic
  async public Task GetFriends(string? status) {
    await HandleException(async () => {
      var id = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var friends = await _userDB.GetAnUsersFriends(id);
      await Clients.Caller.SendAsync(
        "friends",
        friends.Select(f => new FriendsResponse(f, id)),
        default
      );
      foreach (var friend in friends) {
        await AddToFriendsGroup(id, friend.ClientConnections.Select(c => c.ConnectionId).ToList());
        await AddToFriendsGroup(friend.Id, new List<string> { this.Context.ConnectionId });
      }
      await Clients.Group($"{id}_friends").SendAsync("updateStatus", new { id, status, online = true }, default);
    });
  }

  async public Task SearchUser(string userName) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var results = await _userDB.GetUsers(userId, userName);
      await Clients.Caller.SendAsync("searchResults", results, default);
    });
  }

  async public Task SendFriendRequest(string addedId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var friendRequest = await _userDB.CreateFriendRequest(userId, addedId);
      if (friendRequest == null) return;
      await Clients.Caller.SendAsync("added", friendRequest, default);
      var addedConnections = await _messagesDB.GetClientConnections(addedId);
      if (addedConnections.Count == 0) return;
      await Clients.Group($"{addedId}_connections").SendAsync("newFriendRequest", friendRequest, default);
    });
  }

  async public Task GetFriendRequests() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var requests = await _userDB.GetFriendRequests(userId);
      await Clients.Caller.SendAsync("pendingRequests", requests, default);
    });
  }

  async public Task RespondToFriendRequest(string addingId, bool accept) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager
        .Users
        .Include(u => u.ClientConnections
          .Where(c => c.Active)
        )
        .FirstOrDefaultAsync(u => u.Id == addingId);
      ArgumentNullException.ThrowIfNull(user);
      var response = await _userDB.HandleFriendRequest(userId, addingId, accept);
      if (accept) {
        var connection = await _messagesDB.GetClientConnections(addingId);
        if (connection is not null) {
          await AddToFriendsGroup(userId, connection.Select(c => c.ConnectionId).ToList());
          await AddToFriendsGroup(addingId, user.ClientConnections.Select(c => c.ConnectionId).ToList());
          await Clients.Group($"{addingId}_connections").SendAsync("newFriend", response, default);
        }
        await Clients.Group($"{userId}_connections").SendAsync("newFriend", new FriendsResponse(user, true, userId), default);
      }
    });
  }

  async public Task RemoveFriend(string friendId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      await _userDB.RemoveFriend(userId, friendId);
      var connections = await _messagesDB.GetClientConnections(friendId);
      var clients = new List<string> { this.Context.ConnectionId };
      if (connections.Count > 0) clients = clients.Concat(connections.Select(c => c.ConnectionId)).ToList();
      await Clients
        .Clients(clients)
        .SendAsync("removeFriend", new { userId, friendId });
    });
  }

  // User details
  async public Task<UserDetailedResponse?> GetUserDetails(string userId) {
    var result = await HandleException<UserDetailedResponse>(async () => {
      var requestingUserId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var details = await _userDB.GetDetailedUserInfo(requestingUserId, userId);
      ArgumentNullException.ThrowIfNull(details);
      return details;
    });
    return result;
  }

  // Chat creation
  async public Task CreateNewChat(List<string> userIds, string? name, int? maxUsers) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var chat = await _messagesDB.CreateChat(userId, userIds, name, maxUsers);
      foreach (var connection in await _messagesDB.GetAllConnectionsToChat(chat.Id)) {
        await Groups.AddToGroupAsync(connection.ConnectionId, chat.Id, default);
        await Clients.Group(chat.Id).SendAsync("newChat", chat, default);
      }
    });
  }

  // Fetching chat
  async public Task GetChat(string chatId, int skip = 0) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var chat = await _messagesDB.GetChatDetails(userId, chatId, skip);
      await Clients.Caller.SendAsync("chat", chat, default);
    });
  }

  // Searching chat
  async public Task SearchChat(string chatId, string? search, DateTime? startDate, DateTime? endDate, List<string> userIds, int skip) {
    await HandleException(async () => {
      var mainUserId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var results = await _messagesDB.GetChatMessagesFromSearch(chatId, search, startDate, endDate, userIds, skip, mainUserId);
      if (results is null) return;
      await Clients.Caller.SendAsync("chatSearchResults", new { messages = results.Messages, messageCount = results.MessageCount }, default);
    });
  }

  // Chat settings
  async public Task UpdateChatNotificationSettings(string chatId, bool showOSNotification, bool playSound) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var settings = await _messagesDB.UpdateChatNotificationSettings(userId, chatId, showOSNotification, playSound);
      await Clients.Caller.SendAsync("chatNotificationSettings", settings, default);
    });
  }

  // User settings
  async public Task GetNotificationSettings() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var settings = await _userDB.GetNotificationSettings(userId);
      await Clients.Caller.SendAsync("notificationSettings", settings, default);
    });
  }

  async public Task UpdateNotificationSettings(bool playSound, bool showOSNotification, bool showAlert) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var settings = await _userDB.UpdateUserNotificationSettings(userId, playSound, showOSNotification, showAlert);
      await Clients.Caller.SendAsync("notificationSettings", settings, default);
    });
  }

  // Status
  async public Task<string?> GetStatus() {
    return await HandleException<string?>(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      return user.Status;
    });
  }
  async public Task<string?> UpdateStatus(string? status) {
    return await HandleException<string?>(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var result = await _userDB.UpdateStatus(userId, status);
      await Clients.Group($"{userId}_friends").SendAsync("updateStatus", new { id = userId, status, online = true }, default);
      return result;
    });
  }

  // Security
  async public Task GetLoginAttempts(int page = 1) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var attempts = await _userDB.GetUserLoginAttempts(userId, page);
      await Clients.Caller.SendAsync("loginAttempts", new { attempts = attempts.UserLoginAttempts, count = attempts.Count }, default);
    });
  }

  async public Task GetMFASettings() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
      var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
      await Clients.Caller.SendAsync("currentMFAOptions", new { isEnabled, providers }, default);
    });
  }

  async public Task SetMFASettings(bool enable) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
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
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      user.PrivacyLevel = privacyLevel;
      await _userManager.UpdateAsync(user);
      await Clients.Caller.SendAsync("privacyLevel", privacyLevel, default);
    });
  }

  async public Task GetPrivacySettings() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      if (user.PrivacyLevel == 0) {
        user.PrivacyLevel = 1;
        await _userManager.UpdateAsync(user);
      }
      await Clients.Caller.SendAsync("privacyLevel", user.PrivacyLevel, default);
    });
  }

  // Blocking
  async public Task<UserDetailedResponse?> ToggleBlock(string userToBlockId) {
    var result = await HandleException<UserDetailedResponse>(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var blocked = await _userDB.ToggleUserBlocked(userId, userToBlockId);
      var blockedConnections = await _messagesDB.GetClientConnections(userToBlockId);
      //await Clients.Caller.SendAsync("blockToggle", new { id = userToBlockId, blocked }, default);
      if (blockedConnections.Count > 0)
        await Clients.Group($"{userToBlockId}_connections").SendAsync("blocked", new { id = userId, blocked }, default);
      return blocked;
    });
    return result;
  }

  // Change username
  async public Task ChangeUserName(string userName) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var user = await _userManager.FindByIdAsync(userId);
      ArgumentNullException.ThrowIfNull(user);
      user.UserName = userName;
      await _userManager.UpdateAsync(user);
      await Clients.Caller.SendAsync("userName", user.UserName, default);
    });
  }

  // Add & remove users
  async public Task AddUserToChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var chat = await _messagesDB.AddUserToChat(userId, requestingUserId, chatId);
      var systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: requestingUserId,
        chatId,
        eventType: "user added",
        affectedUserId: userId
      );
      var connections = await _messagesDB.GetClientConnections(userId);
      if (connections.Count > 0) {
        var connectionIds = connections.Select(c => c.ConnectionId).ToList();
        await Clients.Group($"{userId}_connections").SendAsync("removedFromChat", chatId, default);
        foreach (var connectionId in connectionIds)
          await Groups.AddToGroupAsync(connectionId, chatId);
      }
      await Clients.Caller.SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
    });
  }
  async public Task RemoveUserFromChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var chat = await _messagesDB.RemoveUserFromChat(userId, requestingUserId, chatId);
      ArgumentNullException.ThrowIfNull(chat);
      var systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: requestingUserId,
        chatId,
        eventType: "user removed",
        affectedUserId: userId
      );
      var connections = await _messagesDB.GetClientConnections(userId);
      if (connections.Count > 0) {
        var connectionIds = connections.Select(c => c.ConnectionId).ToList();
        await Clients.Group($"{userId}_connections").SendAsync("removedFromChat", chatId, default);
        foreach (var connectionId in connectionIds)
          await Groups.RemoveFromGroupAsync(connectionId, chatId);
      }
      await Clients.Group(chatId).SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
    });
  }
  async public Task<string?> LeaveChat(string chatId) {
    return await HandleException<string>(async () => {
      var userId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      var systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: userId,
        chatId,
        eventType: "user left"
      );
      await _messagesDB.LeaveChat(userId, chatId);
      await Groups.RemoveFromGroupAsync(this.Context.ConnectionId, chatId);
      await Clients.Group(chatId).SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
      return chatId;
    });
  }

  // Add admins
  async public Task<UserPartialResponse> AddAdmin(string userId, string chatId) {
    var result = await HandleException<UserPartialResponse>(async () => {
      string requestingUserId = EnsureUserIdNotNull(this.Context.UserIdentifier);
      User admin = await _messagesDB.AddAdminToChat(userId, requestingUserId, chatId);
      SystemMessage systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: requestingUserId,
        chatId,
        affectedUserId: userId,
        eventType: "added admin"
      );
      await Clients.Group(chatId).SendAsync("systemMessage", systemMessage, default);
      if (admin.ClientConnections is not null)
        await Clients.Group($"{userId}_connections").SendAsync("addedAsAdmin", chatId, default);
      return new UserPartialResponse(admin, requestingUserId);
    });
    return result!;
  }
}