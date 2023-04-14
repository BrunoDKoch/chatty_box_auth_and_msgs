using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;

namespace ChattyBox.Hubs;

public class MessagesHub : Hub {
  private ConcurrentDictionary<string, string> _usersAndConnections = new ConcurrentDictionary<string, string>();

  private MessagesDB _messagesDB = new MessagesDB();

  async public override Task OnConnectedAsync() {
    var connection = await _messagesDB.CreateConnection(this.Context.UserIdentifier, this.Context.ConnectionId);
    await base.OnConnectedAsync();
  }

  async public Task GetChatMessages(string userId, string chatId) {
    var messages = await _messagesDB.GetMessagesFromChat(userId, chatId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("directMessages", messages, default);
  }

  async public Task GetUnreadCount(string userId) {
    var count = await _messagesDB.CountUnreadMessages(userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("unread", count, default);
  }

  async public Task StartTyping(string fromId, string chatId) {
    var connections = await _messagesDB.GetAllConnections(chatId);
    if (connections == null) return;
    foreach (var connection in connections) {
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { fromId, isTyping = true }, default);
    }
  }

  async public Task StopTyping(string fromId, string chatId) {
    var connections = await _messagesDB.GetAllConnections(chatId);
    if (connections == null || connections.Count() == 0) return;
    foreach (var connection in connections) {
      await Clients.Client(connection.ConnectionId).SendAsync("typing", new { fromId, isTyping = false }, default);
    }
  }

  async public Task SendMessage(string fromId, string chatId, string text) {
    try {
      var message = await _messagesDB.CreateMessage(fromId, chatId, text);
      if (message == null) throw new Exception();
      var connections = await _messagesDB.GetAllConnections(chatId);
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
    await _messagesDB.MarkAsRead(id, this.Context.UserIdentifier);
    await Clients.Client(this.Context.ConnectionId).SendAsync("read", id, default);
  }

  async public Task GetMessagePreviews(string userId) {
    var messages = await _messagesDB.GetLatestMessagesAsync(userId);
    await Clients.Client(this.Context.ConnectionId).SendAsync("previews", messages, default);
  }
}