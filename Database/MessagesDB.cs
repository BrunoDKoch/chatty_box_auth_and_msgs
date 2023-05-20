using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Database;

public class MessagesDB {

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  public MessagesDB(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
  }

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
  async public Task<ChatMessage?> CreateMessage(string fromId, string chatId, string text, string? replyToId) {
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
      ReplyToId = replyToId,
    };
    newMessage.ReadBy.Add(new ReadMessage {
      UserId = fromId,
      MessageId = newMessage.Id,
    });
    await ctx.Messages.AddAsync(newMessage);
    var user = await ctx.Users.FirstAsync(u => u.Id == fromId);
    await ctx.SaveChangesAsync();
    return new ChatMessage(newMessage, fromId);
  }

  async public Task<CompleteChatResponse> CreateChat(string mainUserId, List<string> userIds, string? name, int? maxUsers) {
    using var ctx = new ChattyBoxContext();
    var newChat = new Chat {
      Id = Guid.NewGuid().ToString(),
      ChatName = name,
      Users = await ctx.Users.Where(u => userIds.Contains(u.Id) || u.Id == mainUserId).ToListAsync(),
      Admins = new List<User> { await ctx.Users.FirstAsync(u => u.Id == mainUserId) },
      MaxUsers = maxUsers ?? 99,
      IsGroupChat = userIds.Count() > 1
    };
    await ctx.Chats.AddAsync(newChat);
    await ctx.SaveChangesAsync();
    var chatResponse = new CompleteChatResponse(newChat, new List<ChatMessage>(), 0);
    return chatResponse;
  }

  // Read
  async public Task<List<ChatPreview>> GetChatPreview(string userId) {
    using var ctx = new ChattyBoxContext();
    var chats = await ctx.Chats
      .Where(
        c => c.Users.Any(u => u.Id == userId)
      )
      .Include(c => c.Users)
      .Include(c => c.Messages)
      .ThenInclude(m => m.ReadBy)
      .ToListAsync();
    var chatPreviews = chats.Select(c => new ChatPreview {
      Id = c.Id,
      Users = c.Users.Select(u => new UserPartialResponse(u)).ToList(),
      CreatedAt = c.CreatedAt,
      LastMessage = c.Messages.Count() > 0 ?
        c.Messages
        .OrderByDescending(m => m.SentAt)
        .Select(m => new MessagePreview {
          From = new UserPartialResponse(m.From),
          SentAt = m.SentAt,
          Text = m.Text,
          Read = m.ReadBy.Any(r => r.UserId == userId) || m.FromId == userId,
        })
        .First()
        : null,
      ChatName = c.ChatName ?? null
    }).ToList();
    return chatPreviews;
  }

  async public Task<CompleteChatResponse> GetChatDetails(string userId, string chatId, int skip = 0) {
    using var ctx = new ChattyBoxContext();
    var messages = await ctx.Messages
      .Where(m => m.ChatId == chatId)
      .OrderByDescending(m => m.SentAt)
      .Skip(skip)
      .Take(15)
      .Include(m => m.From)
      .Include(m => m.ReadBy)
      .ThenInclude(r => r.ReadBy)
      .Select(m => new ChatMessage(m, userId))
      .ToListAsync();
    var messageCount = await ctx.Messages.Where(m => m.ChatId == chatId).CountAsync();
    var chat = await ctx.Chats
      .Include(c => c.Users)
      .ThenInclude(u => u.Blocking)
      .Include(c => c.Users)
      .ThenInclude(u => u.BlockedBy)
      .Include(c => c.Admins)
      .FirstAsync(c => c.Id == chatId);

    return new CompleteChatResponse(chat, messages, messageCount, userId);
  }

  async public Task<List<ClientConnection>> GetAllConnectionsToChat(string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).FirstAsync(c => c.Id == chatId);
    var connections = await ctx.ClientConnections.Include(c => c.User).Where(c => chat.Users.Contains(c.User)).ToListAsync();
    return connections;
  }

  async public Task<int> CountUnreadMessages(string userId) {
    using var ctx = new ChattyBoxContext();
    var count = await ctx.Messages.CountAsync(m => m.Chat.Users.Any(u => u.Id == userId) && !m.ReadBy.Any(u => u.UserId == userId));
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

  async public Task<MessagesSearchResults?> GetChatMessagesFromSearch(
    string chatId,
    string? search,
    DateTime? startDate,
    DateTime? endDate,
    List<string> userIds,
    int skip,
    string mainUserId
    ) {
    if ((search == null || String.IsNullOrEmpty(search)) && startDate == null && endDate == null && userIds.Count() <= 0) return null;
    var mainUser = await _userManager.FindByIdAsync(mainUserId);
    if (mainUser == null) return null;
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Messages).ThenInclude(m => m.From).FirstAsync(c => c.Id == chatId);
    if (chat == null || !chat.Users.Any(u => u.Id == mainUserId)) return null;
    var orderedMessages = chat.Messages.OrderByDescending(m => m.SentAt);

    // Initialize preliminary results, append where clauses for each condition
    IEnumerable<Message> preliminaryResults = orderedMessages;
    if (search != null && !String.IsNullOrEmpty(search)) preliminaryResults = preliminaryResults.Where(m => m.Text.Contains(search));
    if (startDate != null) preliminaryResults = preliminaryResults.Where(m => m.SentAt >= startDate);
    if (endDate != null) preliminaryResults = preliminaryResults.Where(m => m.SentAt <= endDate);
    if (userIds.Count() > 0) preliminaryResults = preliminaryResults.Where(m => userIds.Contains(m.FromId));

    // Convert type and return
    var searchResults = preliminaryResults.Skip(skip).Take(15).Select(m => new ChatMessage(m, mainUserId)).ToList();
    
    return new MessagesSearchResults {
      Messages = searchResults,
      MessageCount = preliminaryResults.Count()
    };
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

  async public Task<ReadMessage> MarkAsRead(string messageId, string userId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.Include(m => m.ReadBy).FirstAsync(m => m.Id == messageId);
    if (message.ReadBy.Any(r => r.UserId == userId)) return message.ReadBy.First(r => r.UserId == userId);
    var readMessage = new ReadMessage {
      UserId = userId,
      MessageId = messageId,
    };
    message.ReadBy.Add(readMessage);
    await ctx.SaveChangesAsync();
    return readMessage;
  }

  async public Task<CompleteChatResponse?> RemoveUserFromChat(string userId, string requestingUserId, string chatId) {
    var user = await _userManager.Users.Include(u => u.IsAdminIn).FirstAsync(u => u.Id == userId);
    if (userId != requestingUserId || user.IsAdminIn.Any(a => a.Id == chatId)) return null;
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    chat.Users.Remove(user);
    if (chat.Admins.Contains(user)) chat.Admins.Remove(user);
    await ctx.SaveChangesAsync();
    return new CompleteChatResponse(chat, requestingUserId);
  }

  async public Task<CompleteChatResponse?> AddUserToChat(string userId, string requestingUserId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    ArgumentNullException.ThrowIfNull(chat);
    if (!chat.Admins.Any(a => a.Id == requestingUserId)) return null;
    var user = await _userManager.FindByIdAsync(userId)!;
    ArgumentNullException.ThrowIfNull(user);
    chat.Users.Add(user);
    await ctx.SaveChangesAsync();
    return new CompleteChatResponse(chat, requestingUserId);
  }

  // Delete
  async public Task DeleteMessage(string messageId, string chatId, string userId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.FirstAsync(m => m.Id == messageId);
    if (userId == message.FromId) await HandleMessageDeletion(ctx, message);
    var chat = await ctx.Chats.FirstAsync(c => c.Id == chatId);

  }

  async public Task<List<string>?> DeleteConnection(string userId) {
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
    try {
      var connections = userFriends.Select(u => u.Connection.ConnectionId);
      return connections.ToList();
    } catch (NullReferenceException) {
      return null;
    }
  }
}