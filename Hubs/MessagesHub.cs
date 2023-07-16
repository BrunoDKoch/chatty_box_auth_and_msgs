using Microsoft.AspNetCore.SignalR;
using ChattyBox.Database;
using ChattyBox.Models;
using ChattyBox.Models.AdditionalModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Humanizer;

namespace ChattyBox.Hubs;

[Authorize]
public class MessagesHub : Hub {
  private readonly IStringLocalizer<MessagesHub> _localizer;
  private readonly MessagesDB _messagesDB;
  private readonly UserDB _userDB;
  private readonly AdminDB _adminDB;

  public MessagesHub(
      IStringLocalizer<MessagesHub> localizer,
      UserDB userDB,
      MessagesDB messagesDB,
      AdminDB adminDB) {
    _userDB = userDB;
    _messagesDB = messagesDB;
    _adminDB = adminDB;
    _localizer = localizer;
  }

  static private string EnsureUserIdNotNull(string? userId) {
    ArgumentNullException.ThrowIfNull(userId);
    return userId;
  }

  async private Task AddToFriendsGroup(string userId, List<string> clientConnectionIds) {
    foreach (var clientConnection in clientConnectionIds)
      await Groups.AddToGroupAsync(clientConnection, $"{userId}_friends", default);
  }

  async private Task SendErrorMessage(ExceptionActionType actionType, Exception exception) {
    var errorId = Guid.NewGuid().ToString();
    exception.AddSentryTag("source", "SignalR");
    exception.AddSentryTag("id", errorId);
    string errorToSend = actionType == ExceptionActionType.MESSAGE ? "msgError" : "error";
    string message = exception switch {
      Microsoft.Data.SqlClient.SqlException sqlException => $"{_localizer.GetString("DatabaseError").Value} {sqlException.Number}",
      _ => $"{_localizer.GetString("Error").Value.Titleize()} {exception.Message}",
    };
    message += $"\n{_localizer.GetString("ErrorLogged").Value} {errorId}";
    await Clients.Caller.SendAsync(errorToSend, message, default);
  }

  async private Task HandleException(Func<Task> action, Func<Task>? finalAction = null, ExceptionActionType actionType = ExceptionActionType.OTHER) {
    if (finalAction is null) {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        await SendErrorMessage(actionType, e);
      }
    } else {
      try {
        await action();
      } catch (Exception e) {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.Error.WriteLine(e);
        Console.ForegroundColor = ConsoleColor.White;
        await SendErrorMessage(actionType, e);
      } finally {
        await finalAction();
      }
    }
  }

  async private Task<T?> HandleException<T>(Func<Task<T>> action, ExceptionActionType actionType = ExceptionActionType.OTHER) {
    try {
      var result = await action();
      return result;
    } catch (Exception e) {
      Console.ForegroundColor = ConsoleColor.DarkRed;
      Console.Error.WriteLine(e);
      Console.ForegroundColor = ConsoleColor.White;
      await SendErrorMessage(actionType, e);
      return default;
    }
  }

  async private Task HandleTyping(string fromId, string chatId, string connectionId, bool isTyping) {
    await HandleException(async () => {
      var from = await _userDB.GetUser(fromId);
      ArgumentNullException.ThrowIfNull(from);
      await Clients.GroupExcept(chatId, connectionId).SendAsync("typing", new { from = from.UserName, isTyping, chatId }, default);
    });
  }

  async public override Task OnConnectedAsync() {
    await HandleException(
      async () => {
        var id = EnsureUserIdNotNull(Context.UserIdentifier);
        var httpContext = Context.GetHttpContext();
        ArgumentNullException.ThrowIfNull(httpContext);
        var userConnection = await _messagesDB.CreateConnection(id, Context.ConnectionId, httpContext);
        var user = await _userDB.GetCompleteUserInfo(id);
        ArgumentNullException.ThrowIfNull(user);

        // Add to admin group
        if (user.Roles.Any() && user.Roles.Any(r => r.NormalizedName == "ADMIN" || r.NormalizedName == "OWNER"))
          await Groups.AddToGroupAsync(Context.ConnectionId, "admins", default);

        // Add to chats
        foreach (var chat in user.Chats) {
          await Groups.AddToGroupAsync(Context.ConnectionId, chat.Id, default);
        }

        // Add to friend groups
        var friends =
          user.Friends.Concat(user.IsFriendsWith).ToList();
        foreach (var friend in friends) {
          if (friend.ClientConnections.Count == 0) continue;
          await AddToFriendsGroup(id, friend.ClientConnections.Select(c => c.ConnectionId).ToList());
          await AddToFriendsGroup(friend.Id, new List<string> { Context.ConnectionId });
        }
        // Inform friends of connection
        await Clients.Group($"{id}_friends").SendAsync("updateStatus", new { id, status = user.Status, online = true }, default);

        await Clients.Caller.SendAsync("connectionSuccessful", default);
      },
      async () => await base.OnConnectedAsync()
    );
  }

  async public override Task OnDisconnectedAsync(Exception? exception) {
    await HandleException(async () => {
      if (exception != null) {
        Console.Error.WriteLine(exception);
      }
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      await _messagesDB.MarkConnectionAsInactive(userId, Context.ConnectionId);
      await Clients.Group($"{userId}_friends").SendAsync("updateStatus", new { id = userId, status = String.Empty, online = false }, default);
    },
      async () => await base.OnDisconnectedAsync(exception)
    );
  }

  async public Task GetCallerInfo() {
    try {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetUser(userId);
      await Clients.Caller.SendAsync("userInfo", new { user.UserName, user.Avatar }, default);
    } catch (ArgumentNullException) {
      return;
    }
  }

  async public Task GetUnreadCount() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var count = await _messagesDB.CountUnreadMessages(userId);
      await Clients.Client(Context.ConnectionId).SendAsync("unread", count, default);
    });
  }

  // Handle messages
  async public Task StartTyping(string chatId) {
    await HandleException(async () => {
      var fromId = Context.UserIdentifier;
      ArgumentException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, Context.ConnectionId, true);
    });
  }

  async public Task StopTyping(string chatId) {
    await HandleException(async () => {
      var fromId = Context.UserIdentifier;
      ArgumentException.ThrowIfNullOrEmpty(fromId);
      await HandleTyping(fromId, chatId, Context.ConnectionId, false);
    });
  }

  async public Task<bool> SendMessage(string chatId, string text, string? replyToId) {
    return await HandleException(async () => {
      var fromId = Context.UserIdentifier;
      ArgumentException.ThrowIfNullOrEmpty(fromId);
      var message = await _messagesDB.CreateMessage(fromId, chatId, text, replyToId);
      ArgumentNullException.ThrowIfNull(message);
      await Clients.Caller.SendAsync("newMessage", message, default);
      message.IsFromCaller = false;
      await Clients.GroupExcept(chatId, Context.ConnectionId).SendAsync("newMessage", message, default);
      return true;
    },
      actionType: ExceptionActionType.MESSAGE
    );
  }

  async public Task MarkAsRead(string id) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var readMessage = await _messagesDB.MarkAsRead(id, userId);
      if (readMessage is null || readMessage.ConnectionIds is null || readMessage.ConnectionIds.Count == 0) return;
      await Clients.Clients(readMessage.ConnectionIds).SendAsync("read", new { id, readBy = readMessage.ReadMessage }, default);
    });
  }

  async public Task EditMessage(string messageId, string text) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var message = await _messagesDB.EditMessage(userId, messageId, text);
      await Clients.Group(message.ChatId).SendAsync("editedMessage", message, default);
    });
  }

  async public Task GetSpecificMessage(string messageId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var message = await _messagesDB.GetSpecificMessage(userId, messageId);
      await Clients.Group(message.ChatId).SendAsync("specificMessage", message, default);
    });
  }

  async public Task<bool> DeleteMessage(string messageId, string chatId) {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      await _messagesDB.DeleteMessage(messageId, chatId, userId);
      await Clients.GroupExcept(chatId, Context.ConnectionId).SendAsync("messageDeleted", messageId, default);
      return true;
    });
  }

  // Get previews
  async public Task GetChatPreviews() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var previews = await _messagesDB.GetChatPreview(userId);
      foreach (var preview in previews) {
        await Groups.AddToGroupAsync(Context.ConnectionId, preview.Id);
      }
      await Clients.Client(Context.ConnectionId).SendAsync("previews", previews, default);
    });
  }
  // Blocked users
  async public Task GetBlockedUsers() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var blocked = await _userDB.GetBlockedUsers(userId);
      await Clients.Caller.SendAsync("blockedUsers", blocked, default);
    });
  }

  // Friends logic
  async public Task GetFriends(string? status) {
    await HandleException(async () => {
      var id = EnsureUserIdNotNull(Context.UserIdentifier);
      var friends = await _userDB.GetAnUsersFriends(id);
      await Clients.Caller.SendAsync(
        "friends",
        friends.Select(f => new FriendResponse(f, id)),
        default
      );
      foreach (var friend in friends) {
        await AddToFriendsGroup(id, friend.ClientConnections.Select(c => c.ConnectionId).ToList());
        await AddToFriendsGroup(friend.Id, new List<string> { Context.ConnectionId });
      }
      await Clients.Group($"{id}_friends").SendAsync("updateStatus", new { id, status, online = true }, default);
    });
  }

  async public Task<List<UserPartialResponse>> SearchUser(UserSearchCall searchCall) {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var results = await _userDB.GetUsers(userId, searchCall);
      return results;
    }) ?? new List<UserPartialResponse>();
  }

  async public Task SendFriendRequest(string addedId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var friendRequest = await _userDB.CreateFriendRequest(userId, addedId);
      if (friendRequest == null) return;
      await Clients.Caller.SendAsync("added", friendRequest, default);
      var addedConnections = await _messagesDB.GetClientConnections(addedId);
      if (addedConnections.Count == 0) return;
      await Clients.User(addedId).SendAsync("newFriendRequest", friendRequest, default);
    });
  }

  async public Task GetFriendRequests() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var requests = await _userDB.GetFriendRequests(userId);
      await Clients.Caller.SendAsync("pendingRequests", requests, default);
    });
  }

  async public Task RespondToFriendRequest(string addingId, bool accept) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetAddingUser(addingId);
      var response = await _userDB.HandleFriendRequest(userId, addingId, accept);
      if (accept) {
        var connection = await _messagesDB.GetClientConnections(addingId);
        if (connection is not null) {
          await AddToFriendsGroup(userId, connection.Select(c => c.ConnectionId).ToList());
          await AddToFriendsGroup(addingId, user.ClientConnections.Select(c => c.ConnectionId).ToList());
          await Clients.User(addingId).SendAsync("newFriend", response, default);
        }
        await Clients.User(userId).SendAsync("newFriend", new FriendResponse(user, true, userId), default);
      }
    });
  }

  async public Task RemoveFriend(string friendId) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      await _userDB.RemoveFriend(userId, friendId);
      var connections = await _messagesDB.GetClientConnections(friendId);
      var clients = new List<string> { Context.ConnectionId };
      if (connections.Count > 0) clients = clients.Concat(connections.Select(c => c.ConnectionId)).ToList();
      await Clients
        .Clients(clients)
        .SendAsync("removeFriend", new { userId, friendId });
    });
  }

  // User details
  async public Task<UserDetailedResponse?> GetUserDetails(string userId) {
    var result = await HandleException<UserDetailedResponse>(async () => {
      var requestingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
      var details = await _userDB.GetDetailedUserInfo(requestingUserId, userId);
      ArgumentNullException.ThrowIfNull(details);
      return details;
    });
    return result;
  }

  // Chat creation
  async public Task<CompleteChatResponse?> CreateNewChat(List<string> userIds, string? name, int? maxUsers) {
    return await HandleException<CompleteChatResponse>(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var chat = await _messagesDB.CreateChat(userId, userIds, name, maxUsers);
      var chatResponse = new ChatPreview(chat);
      await Clients.Users(userIds.AsReadOnly()).SendAsync("newChat", chatResponse, default);
      foreach (var connection in await _messagesDB.GetAllConnectionsToChat(chat.Id)) {
        await Groups.AddToGroupAsync(connection.ConnectionId, chat.Id, default);
      }
      return new CompleteChatResponse(chat, new List<ChatMessage>(), 0);
    });
  }

  // Fetching chat
  async public Task GetChat(string chatId, int skip = 0) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var chat = await _messagesDB.GetChatDetails(userId, chatId, skip);
      await Clients.Caller.SendAsync("chat", chat, default);
    });
  }

  // Searching within chat
  async public Task SearchChat(string chatId, string? search, DateTime? startDate, DateTime? endDate, List<string> userIds, int skip) {
    await HandleException(async () => {
      var mainUserId = EnsureUserIdNotNull(Context.UserIdentifier);
      var results = await _messagesDB.GetChatMessagesFromSearch(chatId, search, startDate, endDate, userIds, skip, mainUserId);
      if (results is null) return;
      await Clients.Caller.SendAsync("chatSearchResults", new { messages = results.Messages, messageCount = results.MessageCount }, default);
    });
  }

  // Searching for a chat
  async public Task<List<ChatPreview>> SearchForChat(string? chatName, string? userName) {
    var result = await HandleException<List<ChatPreview>>(async () => {
      var requestingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
      var list = await _messagesDB.SearchForChats(chatName, userName, requestingUserId);
      return list;
    });
    return result ?? new List<ChatPreview>();
  }

  // Chat settings
  async public Task UpdateChatNotificationSettings(string chatId, bool showOSNotification, bool playSound) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var settings = await _messagesDB.UpdateChatNotificationSettings(userId, chatId, showOSNotification, playSound);
      await Clients.Caller.SendAsync("chatNotificationSettings", settings, default);
    });
  }

  // User settings
  async public Task<UserNotificationSetting> GetNotificationSettings() {
    var result = await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      UserNotificationSetting settings = await _userDB.GetNotificationSettings(userId);
      return settings ?? new UserNotificationSetting {
        UserId = userId,
        ShowOSNotification = true,
        PlaySound = true,
        ShowAlert = true,
      };
    });
    return result!;
  }

  async public Task<UserNotificationSetting> UpdateNotificationSettings(bool playSound, bool showOSNotification, bool showAlert) {
    var result = await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var settings = await _userDB.UpdateUserNotificationSettings(userId, playSound, showOSNotification, showAlert);
      return settings ?? new UserNotificationSetting {
        UserId = userId,
        ShowOSNotification = true,
        PlaySound = true,
        ShowAlert = true,
      };
    });
    return result!;
  }

  // Status
  async public Task<string?> GetStatus() {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetUser(userId);
      return user.Status;
    });
  }
  async public Task<string?> UpdateStatus(string? status) {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var result = await _userDB.UpdateStatus(userId, status);
      await Clients.Group($"{userId}_friends").SendAsync("updateStatus", new { id = userId, status, online = true }, default);
      return result;
    });
  }

  // Security
  async public Task GetLoginAttempts(int page = 1) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var attemptsData = await _userDB.GetUserLoginAttempts(userId, page);
      var attempts = attemptsData.UserLoginAttempts;
      var count = attemptsData.Count;
      await Clients.Caller.SendAsync("loginAttempts", new { attempts, count }, default);
    });
  }

  async public Task<bool> GetMFASettings() {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var isEnabled = await _userDB.GetUserMFAEnabled(userId);
      return isEnabled;
    });
  }

  async public Task SetMFASettings(bool enable) {
    await HandleException(async () => {

      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetUser(userId);
      
      if (enable) {
        var tokenAndRecoveryCodes = await _userDB.GenerateMFACodes(user);
        var token = tokenAndRecoveryCodes.Item1;
        ArgumentException.ThrowIfNullOrEmpty(token);
        var recoveryCodes = tokenAndRecoveryCodes.Item2;
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        await Clients.Caller.SendAsync("mfaToken", new { token, recoveryCodes }, default);
      }
      await _userDB.ToggleMFA(user, enable);
    });
  }

  async public Task<List<ClientConnectionPartialInfo>> GetConnections() {
    var result = await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var connections = await _messagesDB.GetClientConnections(userId);
      return connections.Select(c => new ClientConnectionPartialInfo(c, Context.ConnectionId)).ToList();
    });
    return result ?? new List<ClientConnectionPartialInfo>();
  }

  async public Task CloseConnection(List<string> ids) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetUser(userId);
      var relevantConnections = await _messagesDB.DeleteConnections(ids, user);
      ArgumentNullException.ThrowIfNull(relevantConnections);
      foreach (var relevantConnection in relevantConnections) {
        if ((bool)relevantConnection.Active!)
          await Clients.Client(relevantConnection.ConnectionId).SendAsync("forceLogOut");
      }
    });
  }

  // Privacy
  async public Task SetPrivacySettings(int privacyLevel) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      await _userDB.UpdatePrivateLevel(userId, privacyLevel);
      await Clients.Caller.SendAsync("privacyLevel", privacyLevel, default);
    });
  }

  async public Task GetPrivacySettings() {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.GetUser(userId);
      await Clients.Caller.SendAsync("privacyLevel", user.PrivacyLevel, default);
    });
  }

  // Blocking
  async public Task<UserDetailedResponse?> ToggleBlock(string userToBlockId) {
    var result = await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var blocked = await _userDB.ToggleUserBlocked(userId, userToBlockId);
      var blockedConnections = await _messagesDB.GetClientConnections(userToBlockId);
      //await Clients.Caller.SendAsync("blockToggle", new { id = userToBlockId, blocked }, default);
      if (blockedConnections.Count > 0)
        await Clients.User(userToBlockId).SendAsync("blocked", new { id = userId, blocked }, default);
      return blocked;
    });
    return result;
  }

  // Change username
  async public Task ChangeUserName(string userName) {
    await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var user = await _userDB.ChangeUsername(userId, userName);
      await Clients.Caller.SendAsync("userName", user.UserName, default);
      var groups = user.Chats.Select(c => c.Id).Concat(new List<string> { $"{userId}_friends" });
      await Clients.Groups(groups).SendAsync("newUserName", new { userId, userName }, default);
    });
  }

  // Add & remove users
  async public Task AddUserToChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
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
        await Clients.User(userId).SendAsync("removedFromChat", chatId, default);
        foreach (var connectionId in connectionIds)
          await Groups.AddToGroupAsync(connectionId, chatId);
      }
      await Clients.Caller.SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
    });
  }
  async public Task RemoveUserFromChat(string userId, string chatId) {
    await HandleException(async () => {
      var requestingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
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
        await Clients.User(userId).SendAsync("removedFromChat", chatId, default);
        foreach (var connectionId in connectionIds)
          await Groups.RemoveFromGroupAsync(connectionId, chatId);
      }
      await Clients.Group(chatId).SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
    });
  }
  async public Task<string?> LeaveChat(string chatId) {
    return await HandleException(async () => {
      var userId = EnsureUserIdNotNull(Context.UserIdentifier);
      var systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: userId,
        chatId,
        eventType: "user left"
      );
      await _messagesDB.LeaveChat(userId, chatId);
      await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId);
      await Clients.Group(chatId).SendAsync("systemMessage", new SystemMessagePartial(systemMessage), default);
      return chatId;
    });
  }

  // Add admins
  async public Task<UserPartialResponse> AddAdmin(string userId, string chatId) {
    var result = await HandleException(async () => {
      string requestingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
      User admin = await _messagesDB.AddAdminToChat(userId, requestingUserId, chatId);
      SystemMessage systemMessage = await _messagesDB.CreateSystemMessage(
        instigatingUserId: requestingUserId,
        chatId,
        affectedUserId: userId,
        eventType: "added admin"
      );
      await Clients.Group(chatId).SendAsync("systemMessage", systemMessage, default);
      if (admin.ClientConnections is not null)
        await Clients.User(userId).SendAsync("addedAsAdmin", chatId, default);
      return new UserPartialResponse(admin, requestingUserId);
    });
    return result!;
  }

  // Report user
  async public Task ReportUser(ReportRequest reportRequest) {
    await HandleException(async () => {
      var reportingUserId = EnsureUserIdNotNull(Context.UserIdentifier);
      await _adminDB.CreateReport(reportRequest, reportingUserId);
      await Clients.Caller.SendAsync("reportReceived", default);
    });
  }

  // Update reports
  [Authorize(Roles = "admin,owner")]
  async public Task UpdateReport(string id, bool violationFound) {
    await HandleException(async () => {
      await _adminDB.SetViolationFound(id, violationFound);
      var adminsAndOwner = await _adminDB.GetAdminIds();
      await Clients.Users(adminsAndOwner.AsReadOnly()).SendAsync("updateReport", new { id, violationFound }, default);
    });
  }
}