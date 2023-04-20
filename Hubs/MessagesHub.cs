using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;
using ChattyBox.Misc;

namespace ChattyBox.Hubs;

public class MessagesHub : Hub {
  private List<string> _validLetters = ValidCharacters.GetLetters().Split().ToList();
  private MessagesDB _messagesDB = new MessagesDB();
  private UserDB _userDB = new UserDB();

  async public override Task OnConnectedAsync() {
    var id = this.Context.UserIdentifier;
    if (id == null) return;
    var userConnection = await _messagesDB.CreateConnection(id, this.Context.ConnectionId);
    try {
      var friends = await _userDB.GetAnUsersFriends(id);
      await Clients.Caller.SendAsync("friends", friends, default);
      foreach (var friend in friends) {
        var connection = await _messagesDB.GetClientConnection(friend.UserId);
        if (connection == null) continue;
        await Clients.Client(connection.ConnectionId).SendAsync("updateStatus", id, default);
      }
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
    foreach (var connection in relevantConnections) {
      await Clients.Client(connection).SendAsync("updateStatus", userId, default);
    }
    await base.OnDisconnectedAsync(exception);
  }

  async public Task GetChatMessages(string chatId) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var messages = await _messagesDB.GetMessagesFromChat(userId, chatId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("chatMessages", messages, default);
  }

  async public Task GetUnreadCount() {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var count = await _messagesDB.CountUnreadMessages(userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("unread", count, default);
  }

  async public Task StartTyping(string fromId, string chatId) {
    var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
    if (connections == null) return;
    foreach (var connection in connections) {
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { fromId, isTyping = true }, default);
    }
  }

  async public Task StopTyping(string fromId, string chatId) {
    var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
    if (connections == null || connections.Count() == 0) return;
    foreach (var connection in connections) {
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { fromId, isTyping = false }, default);
    }
  }

  async public Task SendMessage(string fromId, string chatId, string text) {
    try {
      var message = await _messagesDB.CreateMessage(fromId, chatId, text);
      if (message == null) throw new Exception();
      var connections = await _messagesDB.GetAllConnectionsToChat(chatId);
      foreach (var connection in connections) {
        await Clients.Client(connection.ConnectionId).SendAsync("newMessage", message, default);
      }
      await Clients.Caller.SendAsync("newMessage", message, default);
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
    var friendRequest = _userDB.CreateFriendRequest(userId, addedId);
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
  }

  // Chat creation
  async public Task CreateNewChat(List<string> userIds, string? name, int? maxUsers) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var chat = await _messagesDB.CreateChat(userId, userIds, name, maxUsers);
    Console.WriteLine(chat.Id);
    foreach (var connection in await _messagesDB.GetAllConnectionsToChat(chat.Id)) {
      await Clients.Client(connection.ConnectionId).SendAsync("newChat", chat, default);
    }
  }

  // Fetching chat
  async public Task GetChat(string chatId) {
    var userId = this.Context.UserIdentifier;
    if (userId == null) return;
    var chat = await _messagesDB.GetMessagesFromChat(userId, chatId);
    await Clients.Caller.SendAsync("chat", chat, default);
  }
}