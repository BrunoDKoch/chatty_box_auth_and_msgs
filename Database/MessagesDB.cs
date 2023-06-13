using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;
using ChattyBox.Context;
using ChattyBox.Services;
using Microsoft.AspNetCore.Identity;
using UAParser;
using MaxMind.GeoIP2;
using System.Net;
using MaxMind.GeoIP2.Responses;

namespace ChattyBox.Database;

public class MessagesDB {

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly WebServiceClient _maxMindClient;

  public MessagesDB(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      WebServiceClient maxMindClient) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _maxMindClient = maxMindClient;
  }

  async private Task HandleMessageDeletion(ChattyBoxContext ctx, Message message) {
    var readMessage = await ctx.ReadMessages.Where(rm => rm.MessageId == message.Id).ToListAsync();
    ctx.ReadMessages.RemoveRange(readMessage);
    ctx.Messages.Remove(message);
    await ctx.SaveChangesAsync();
  }

  async private Task<ClientConnection?> CheckExistingConnection(
    ChattyBoxContext ctx,
    string userId,
    string connectionId,
    HttpContext context,
    CityResponse geoData
  ) {
    IPAddress iPAddress;
    if (context.Connection.RemoteIpAddress == null || new List<string> { "::1", "127.0.0.1" }.Contains(context.Connection.RemoteIpAddress.ToString())) {
      iPAddress = IPAddress.Parse(_configuration.GetValue<string>("TestIP")!);
    } else {
      iPAddress = context.Connection.RemoteIpAddress;
    }
    var clientInfo = ParsingService.ParseContext(context);
    var osInfo = $"{clientInfo.OS.Family} {clientInfo.OS.Major}.{clientInfo.OS.Minor}";
    var deviceInfo = $"{clientInfo.Device.Brand} {clientInfo.Device.Family} {clientInfo.Device.Model}";
    var browserInfo = $"{clientInfo.UA.Family} {clientInfo.UA.Major}.{clientInfo.UA.Minor}";
    var existingConnection = await ctx.ClientConnections.FirstOrDefaultAsync(
      c => c.UserId == userId && (
        c.ConnectionId == connectionId || (
          (
            // IP Address can change. Therefore, we're also checking if the location is roughly the same.
            c.IpAddress == iPAddress.ToString() || (
              (int)(c.Latitude - (double)(geoData.Location.Latitude ?? (double)0.0)) <= 2 &&
              (int)(c.Longitude - (double)(geoData.Location.Longitude ?? (double)0.0)) <= 2
            )
          ) &&
          c.Os == osInfo &&
          c.Device == deviceInfo &&
          c.Browser == browserInfo
          )
      )
    );
    return existingConnection;
  }

  // Create
  async public Task<ClientConnection> CreateConnection(string userId, string connectionId, HttpContext context) {
    using var ctx = new ChattyBoxContext();
    IPAddress iPAddress;
    if (context.Connection.RemoteIpAddress == null || new List<string> { "::1", "127.0.0.1" }.Contains(context.Connection.RemoteIpAddress.ToString())) {
      iPAddress = IPAddress.Parse(_configuration.GetValue<string>("TestIP")!);
    } else {
      iPAddress = context.Connection.RemoteIpAddress;
    }
    var geoData = await _maxMindClient.CityAsync(iPAddress);
    var existingConnection = await CheckExistingConnection(ctx, userId, connectionId, context, geoData);
    if (existingConnection != null) {
      existingConnection.ConnectionId = connectionId;
      await ctx.SaveChangesAsync();
      return existingConnection;
    }

    var clientInfo = ParsingService.ParseContext(context);
    var clientConnection = new ClientConnection {
      UserId = userId,
      ConnectionId = connectionId,
      IpAddress = iPAddress.ToString(),
      CityName = geoData.City.Name ?? "unknown",
      GeoNameId = geoData.City.GeoNameId != null ? geoData.City.GeoNameId.ToString()! : "unknown",
      CountryName = geoData.Country.Name ?? "unknown",
      CountryIsoCode = geoData.Country.IsoCode ?? "unknown",
      Latitude = (double)geoData.Location.Latitude!,
      Longitude = (double)geoData.Location.Longitude!,
      Os = $"{clientInfo.OS.Family} {clientInfo.OS.Major}.{clientInfo.OS.Minor}",
      Device = $"{clientInfo.Device.Brand} {clientInfo.Device.Family} {clientInfo.Device.Model}",
      Browser = $"{clientInfo.UA.Family} {clientInfo.UA.Major}.{clientInfo.UA.Minor}",
      CreatedAt = DateTime.UtcNow
    };
  await ctx.ClientConnections.AddAsync(clientConnection);
    await ctx.SaveChangesAsync();
    return clientConnection;
  }
  async public Task<ChatMessage?> CreateMessage(string fromId, string chatId, string text, string? replyToId = null) {
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

  async public Task<SystemMessage> CreateSystemMessage(string instigatingUserId, string chatId, string eventType, string? affectedUserId = null) {
    var newSystemMessage = new SystemMessage {
      InstigatingUserId = instigatingUserId,
      ChatId = chatId,
      EventType = eventType,
      AffectedUserId = affectedUserId,
    };
    using var ctx = new ChattyBoxContext();
    await ctx.SystemMessages.AddAsync(newSystemMessage);
    await ctx.SaveChangesAsync();
    return await ctx.SystemMessages.Include(sm => sm.InstigatingUser).Include(sm => sm.AffectedUser).FirstAsync(sm => sm.Id == newSystemMessage.Id);
  }

  // Read
  async public Task<List<ChatPreview>> GetChatPreview(string userId) {
    using var ctx = new ChattyBoxContext();
    var chats = await ctx.Chats
      .Where(
        c => c.Users.Any(u => u.Id == userId)
      )
      .Include(c => c.Users)
      .Include(c => c.ChatNotificationSettings)
      .Include(c => c.Messages)
        .ThenInclude(m => m.From)
      .Include(c => c.Messages)
        .ThenInclude(m => m.ReadBy)
      .ToListAsync();
    if (chats.Count == 0) return new List<ChatPreview>();
    var chatPreviews = chats
      .Select(c => new ChatPreview(c, userId))
      .OrderByDescending(c => c.LastMessage is null ? c.CreatedAt : c.LastMessage.SentAt)
      .ToList();
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
      .Include(c => c.Admins)
      .Include(c => c.SystemMessages.Where(sm => messages.Any() && messages.Select(m => m.SentAt.Date).ToList().Contains(sm.FiredAt.Date)))
        .ThenInclude(sm => sm.InstigatingUser)
      .Include(c => c.SystemMessages)
        .ThenInclude(sm => sm.AffectedUser)
      .Include(c => c.Users)
        .ThenInclude(u => u.Blocking)
      .Include(c => c.Users)
        .ThenInclude(u => u.BlockedBy)
      .FirstAsync(c => c.Id == chatId);

    return new CompleteChatResponse(chat, messages, messageCount, userId);
  }

  async public Task<List<ClientConnection>> GetAllConnectionsToChat(string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).FirstAsync(c => c.Id == chatId);
    var connections = await ctx.ClientConnections
      .Include(c => c.User)
      .Where(c => c.Active && chat.Users.Contains(c.User))
      .ToListAsync();
    return connections;
  }

  async public Task<int> CountUnreadMessages(string userId) {
    using var ctx = new ChattyBoxContext();
    var count = await ctx.Messages.CountAsync(m => m.Chat.Users.Any(u => u.Id == userId) && !m.ReadBy.Any(u => u.UserId == userId));
    return count;
  }

  async public Task<List<ClientConnection>> GetClientConnections(string userId) {
    using var ctx = new ChattyBoxContext();
    try {
      var clientConnection = await ctx.ClientConnections
        .Where(c => c.UserId == userId && c.Active)
        .ToListAsync();
      return clientConnection;
    } catch (InvalidOperationException) {
      return new List<ClientConnection>();
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
    if (String.IsNullOrEmpty(search) && startDate is null && endDate is null && userIds.Count() <= 0) return null;
    var mainUser = await _userManager.FindByIdAsync(mainUserId);
    ArgumentNullException.ThrowIfNull(mainUser);
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Messages).ThenInclude(m => m.From).FirstAsync(c => c.Id == chatId);
    if (chat is null || !chat.Users.Any(u => u.Id == mainUserId)) return null;
    var orderedMessages = chat.Messages.OrderByDescending(m => m.SentAt);

    // Initialize preliminary results, append where clauses for each condition
    IEnumerable<Message> preliminaryResults = orderedMessages;
    if (!String.IsNullOrEmpty(search)) preliminaryResults = preliminaryResults.Where(m => m.Text.ToLower().Contains(search.ToLower()));
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

  async public Task<ChatMessage> GetSpecificMessage(string userId, string messageId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.Include(m => m.From).FirstAsync(m => m.Id == messageId);
    ArgumentNullException.ThrowIfNull(message);
    return new ChatMessage(message, userId);
  }

  // Update
  async public Task<ChatMessage> EditMessage(string userId, string messageId, string text) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.Include(m => m.From).FirstAsync(m => m.Id == messageId);
    ArgumentNullException.ThrowIfNull(message);
    if (message.FromId == userId && message.Text != text) {
      message.Text = text;
      message.EditedAt = DateTime.UtcNow;
      await ctx.SaveChangesAsync();
    }
    return new ChatMessage(message, userId);
  }

  async public Task<MessageReadInformationResponse?> MarkAsRead(string messageId, string userId) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages
      .Include(m => m.ReadBy)
        .ThenInclude(r => r.ReadBy)
      .Include(m => m.From)
        .ThenInclude(f => f.ClientConnections)
      .FirstAsync(m => m.Id == messageId);
    if (message.ReadBy.Any(r => r.ReadBy.Id == userId)) return null;
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    var readMessage = new ReadMessage {
      UserId = userId,
      MessageId = messageId,
      ReadAt = DateTime.UtcNow,
    };
    await ctx.ReadMessages.AddAsync(readMessage);
    await ctx.SaveChangesAsync();
    var readMessageResponse = new ReadMessagePartialResponse(user, readMessage.ReadAt);
    return new MessageReadInformationResponse {
      ReadMessage = readMessageResponse,
      ConnectionIds = message.From.ClientConnections.Select(c => c.ConnectionId).ToList(),
    };
  }

  async public Task<CompleteChatResponse?> RemoveUserFromChat(string userId, string requestingUserId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    if (userId != requestingUserId && !chat.Admins.Any(a => a.Id == requestingUserId)) return null;
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    chat.Users.Remove(user);
    if (chat.Admins.Contains(user)) chat.Admins.Remove(user);
    ctx.Chats.Update(chat);
    await ctx.SaveChangesAsync();
    return new CompleteChatResponse(chat, requestingUserId);
  }

  async public Task<ChatPreview> AddUserToChat(string userId, string requestingUserId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    ArgumentNullException.ThrowIfNull(chat);
    if (!chat.IsGroupChat) throw new InvalidOperationException("Cannot add users to private chat");
    if (!chat.Admins.Any(a => a.Id == requestingUserId))
      throw new ArgumentException("User is not an admin");
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    chat.Users.Add(user);
    ctx.Chats.Update(chat);
    await ctx.SaveChangesAsync();
    return new ChatPreview(chat, userId);
  }

  async public Task<User> AddAdminToChat(string userId, string requestingUserId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats
      .Include(c => c.Users)
      .Include(c => c.Admins)
      .FirstOrDefaultAsync(c => c.Id == chatId);
    ArgumentNullException.ThrowIfNull(chat);
    if (!chat.Admins.Any(a => a.Id == requestingUserId))
      throw new ArgumentException("Must be an admin to add another admin");
    if (chat.Admins.Any(a => a.Id == userId))
      throw new ArgumentException("User is already an admin");
    var user = await ctx.Users
      .Include(u => u.ClientConnections)
      .FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    chat.Admins.Add(user);
    ctx.Chats.Update(chat);
    await ctx.SaveChangesAsync();
    return user;
  }

  async public Task LeaveChat(string userId, string chatId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Users).Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    ArgumentNullException.ThrowIfNull(chat);
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    ArgumentNullException.ThrowIfNull(user);
    chat.Users.Remove(user);
    if (chat.Admins.Contains(user)) chat.Admins.Remove(user);

    // I don't like this, but we need SOMEONE to be admin
    if (chat.Admins.Count == 0) {
      var random = new Random();
      var randomUser = random.Next(chat.Users.Count);
      chat.Admins.Add(chat.Users.ToList()[randomUser]);
    }
    await ctx.SaveChangesAsync();
  }

  async public Task<ChatNotificationSetting> UpdateChatNotificationSettings(string userId, string chatId, bool showOSNotification, bool playSound) {
    using var ctx = new ChattyBoxContext();
    var settings = await ctx.ChatNotificationSettings.FirstOrDefaultAsync(n => n.UserId == userId && n.ChatId == chatId);
    if (settings == null) {
      settings = new ChatNotificationSetting {
        ChatId = chatId,
        UserId = userId,
      };
      await ctx.ChatNotificationSettings.AddAsync(settings);
    }
    settings.ShowOSNotification = showOSNotification;
    settings.PlaySound = playSound;
    await ctx.SaveChangesAsync();
    return settings;
  }

  async public Task<ClientConnection> MarkConnectionAsInactive(string userId, string connectionId) {
    using var ctx = new ChattyBoxContext();
    var clientConnection = await ctx.ClientConnections.FirstOrDefaultAsync(c => c.ConnectionId == connectionId && c.UserId == userId);
    ArgumentNullException.ThrowIfNull(clientConnection);
    clientConnection.Active = false;
    await ctx.SaveChangesAsync();
    return clientConnection;
  }

  // Delete
  async public Task<bool> DeleteMessage(string messageId, string chatId, string userId) {
    using var ctx = new ChattyBoxContext();
    var chat = await ctx.Chats.Include(c => c.Admins).FirstAsync(c => c.Id == chatId);
    var message = await ctx.Messages.FirstAsync(m => m.Id == messageId);
    if (userId != message.FromId && !chat.Admins.Any(a => a.Id == userId)) {
      return false;
    }
    await HandleMessageDeletion(ctx, message);
    return true;
  }

  async public Task DeleteConnection(string connectionId) {
    using var ctx = new ChattyBoxContext();
    var connection = await ctx.ClientConnections.FirstOrDefaultAsync(c => c.ConnectionId == connectionId);
    ArgumentNullException.ThrowIfNull(connection);
    ctx.Remove(connection);
    await ctx.SaveChangesAsync();
  }
}