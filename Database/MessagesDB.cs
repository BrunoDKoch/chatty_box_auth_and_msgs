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
  async public Task<ChatMessage?> CreateMessage(string fromId, string chatId, string text) {
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
      Id = Guid.NewGuid().ToString(),
      FromId = fromId,
      ChatId = chatId,
      Text = text,
    };
    await ctx.Messages.AddAsync(newMessage);
    await ctx.SaveChangesAsync();
    return new ChatMessage {
      Message = newMessage,
      IsFromCaller = false,
      User = await ctx.Users.FirstAsync(u => u.Id == fromId),
    };
  }

  async public Task<Chat> CreateChat(string mainUserId, List<string> userIds, string? name, int? maxUsers) {
    using var ctx = new ChattyBoxContext();
    var newChat = new Chat {
      Id = Guid.NewGuid().ToString(),
      ChatName = name,
      Users = await ctx.Users.Where(u => userIds.Contains(u.Id) || u.Id == mainUserId).ToListAsync(),
      Admins = new List<User> { await ctx.Users.FirstAsync(u => u.Id == mainUserId) },
      MaxUsers = maxUsers ?? 99
    };
    await ctx.Chats.AddAsync(newChat);
    await ctx.SaveChangesAsync();
    return newChat;
  }

  // Read
  async public Task<List<ChatPreview>> GetChatPreview(string userId) {
    using var ctx = new ChattyBoxContext();
    var chats = await ctx.Chats.Where(c => c.Users.Any(u => u.Id == userId)).Include(c => c.Users).Include(c => c.Messages).ToListAsync();
    var chatPreviews = chats.Select(c => new ChatPreview {
      Id = c.Id,
      Users = c.Users,
      CreatedAt = c.CreatedAt,
      LastMessage = c.Messages.Count() > 0 ?
        c.Messages
        .OrderByDescending(m => m.SentAt)
        .Select(m => new MessagePreview {
          From = m.From,
          SentAt = m.SentAt,
          Text = m.Text
        })
        .First()
        : null,
      ChatName = c.ChatName ?? null
    }).ToList();
    return chatPreviews;
  }

  async public Task<CompleteChatResponse> GetMessagesFromChat(string userId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var messages = await ctx.Messages
      .Where(m => m.ChatId == chatId)
      .Include(m => m.From)
      .Include(m => m.Chat)
      .Select(m => new ChatMessage {
        User = m.From ?? ctx.Users.First(u => u.Id == m.FromId),
        Message = m,
        IsFromCaller = m.FromId == userId
      })
      .ToListAsync();
    var completeChat = await ctx.Chats
      .Include(c => c.Users)
      .Select(c => new CompleteChatResponse {
        Id = c.Id,
        IsGroupChat = c.IsGroupChat,
        Messages = messages,
        Users = c.Users,
        MaxUsers = c.MaxUsers,
        ChatName = c.ChatName,
        CreatedAt = c.CreatedAt,
      })
      .FirstAsync(c => c.Id == chatId);

    return completeChat;
  }

  async public Task<List<ClientConnection>> GetAllConnectionsToChat(string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).FirstAsync(c => c.Id == chatId);
    var connections = await ctx.ClientConnections.Include(c => c.User).Where(c => chat.Users.Contains(c.User)).ToListAsync();
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