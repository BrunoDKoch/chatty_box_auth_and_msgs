using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;
using ChattyBox.Context;

namespace ChattyBox.Database;

public class MessagesDB {

  async private Task HandleMessageDeletion(ChattyBoxContext ctx, Message message) {
    ctx.Messages.Remove(message);
    await ctx.SaveChangesAsync();
  }

  // Create
  async public Task<ClientConnection> CreateConnection(string userId, string connectionId) {
    using var ctx = new ChattyBoxContext();
    var existingConnection = await ctx.ClientConnections.FirstOrDefaultAsync(c => c.UserId == userId);
    Console.WriteLine($"{existingConnection}");
    if (existingConnection != null) {
      existingConnection.ConnectionId = connectionId;
      await ctx.SaveChangesAsync();
      return existingConnection;
    }
    var clientConnection = new ClientConnection {
      UserId = userId,
      ConnectionId = connectionId
    };
    await ctx.ClientConnections.AddAsync(clientConnection);
    await ctx.SaveChangesAsync();
    return clientConnection;
  }
  async public Task<Message?> CreateMessage(string fromId, string chatId, string text) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.FirstAsync(c => c.Id == chatId);
    if (chat == null) return null;
    if (!chat.IsGroupChat) {
      var isBlocked = await ctx.Blocks.AnyAsync(b => 
        (b.ANavigation.Id == fromId && chat.Users.Contains(b.BNavigation)) ||
        (b.BNavigation.Id == fromId && chat.Users.Contains(b.ANavigation))
      );
      if (isBlocked) return null;
    }
    var newMessage = new Message {
      FromId = fromId,
      ChatId = chatId,
      Text = text,
    };
    await ctx.Messages.AddAsync(newMessage);
    await ctx.SaveChangesAsync();
    return newMessage;
  }

  async public Task<Chat> CreateChat(string[] userIds, string? name, int? maxUsers) {
    using var ctx = new ChattyBoxContext();
    var newChat = new Chat {
      ChatName = name,
      Users = await ctx.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(),
      MaxUsers = maxUsers ?? 99
    };
    await ctx.Chats.AddAsync(newChat);
    await ctx.SaveChangesAsync();
    return newChat;
  }

  // Read
  async public Task<List<Message>> GetLatestMessagesAsync(string userId) {
    using var ctx = new ChattyBoxContext();
    var chats = await ctx.Chats.Where(c => c.Users.Any(u => u.Id == userId)).ToListAsync();
    var latestMessages = chats.Select(c => c.Messages.OrderBy(m => m.SentAt).First()).ToList();

    // TODO: Implement this filter
    var filteredMessages = from message in latestMessages select new MessagePreview {
      From = message.From,
      SentAt = message.SentAt,
      Text = message.Text,
      Chat = message.Chat,
    };
    return latestMessages;
  }

  async public Task<List<ChatMessage>> GetMessagesFromChat(string userId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    var messages = await ctx.Messages.Where(m => m.Chat.Users.First(u => u.Id == userId) != null && m.ChatId == chatId).ToListAsync();
    var messagesFromCaller = from message in messages where message.FromId == userId select new ChatMessage {
      Message = message,
      User = user,
      IsFromCaller = true,
    };
    var messagesFromOthers = from message in messages where message.FromId != userId select new ChatMessage {
      Message = message,
      User = message.From,
      IsFromCaller = false,
    };
    var filteredMessages = messagesFromCaller.Concat(messagesFromOthers).ToList();
    return filteredMessages;
  }

  async public Task<List<ClientConnection>> GetAllConnectionsToChat(string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.FirstAsync(c => c.Id == chatId);
    var connections = await ctx.ClientConnections.Where(c => chat.Users.Contains(c.User)).ToListAsync();
    return connections;
  }

  async public Task<int> CountUnreadMessages(string userId) {
    using var ctx = new ChattyBoxContext();
    var count = await ctx.Messages.CountAsync(m => m.Chat.Users.Any(u => u.Id == userId) && !m.ReadBy.Any(u => u.Id == userId));
    return count;
  }

  async public Task<ClientConnection?> GetClientConnection(string userId) {
    using var ctx = new ChattyBoxContext();
    try {
      var clientConnection = await ctx.ClientConnections.FirstAsync(c => c.UserId == userId);
      return clientConnection;
    } catch (InvalidOperationException) {
      return null;
    }
  }

  // Update
  async public Task<Message> EditMessage(string messageId, string text) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.FirstAsync(m => m.Id == messageId);
    message.Text = text;
    message.EditedAt = DateTime.UtcNow;
    await ctx.SaveChangesAsync();
    return message;
  }

  async public Task<Message> MarkAsRead(string messageId, string userId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.FirstAsync(m => m.Id == messageId);
    var readMessage = new ReadMessage {
      UserId = userId,
      MessageId = messageId,
      ReadAt = DateTime.UtcNow
    };
    await ctx.ReadMessages.AddAsync(readMessage);
    await ctx.SaveChangesAsync();
    return message;
  }

  // Delete
  async public Task DeleteMessage(string messageId, string chatId, string userId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.FirstAsync(m => m.Id == messageId);
    if (userId == message.FromId) await HandleMessageDeletion(ctx, message);
    var chat = await ctx.Chats.FirstAsync(c => c.Id == chatId);
    
  }

  async public Task<List<string>> DeleteConnection(string userId) {
    using var ctx = new ChattyBoxContext();
    var connection = await ctx.ClientConnections.FirstAsync(c => c.UserId == userId);
    ctx.Remove(connection);
    await ctx.SaveChangesAsync();
    var userFriends = await ctx.Users
      .Include(u => u.Connection)
      .Where(
        u => u.Friends.Any(f => f.Id == userId) ||
        u.IsFriendsWith.Any(f => f.Id == userId) ||
        u.Chats.Any(c => c.Users.Any(cu => cu.Id == userId))
      )
      .ToListAsync();
    var connections = userFriends.Select(u => u.Connection.ConnectionId);
    return connections.ToList();
  }
}