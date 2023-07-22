using Newtonsoft.Json;

namespace ChattyBox.Models.AdditionalModels;

public class CompleteChatResponse {
  public string Id { get; set; } = null!;
  public bool IsGroupChat { get; set; }
  public int MaxUsers { get; set; }
  public string? ChatName { get; set; }
  public bool UserIsAdmin { get; set; }
  public DateTime CreatedAt { get; set; }
  public ICollection<UserPartialResponse> Admins { get; set; } = new List<UserPartialResponse>();
  public ICollection<UserPartialResponse> Users { get; set; } = new List<UserPartialResponse>();
  public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
  public ICollection<SystemMessagePartial> SystemMessages { get; set; } = new List<SystemMessagePartial>();
  public ICollection<string> AdminIds { get; set; } = new List<string>();
  public int MessageCount { get; set; }
  public CompleteChatResponse(Chat chat, string mainUserId) {
    Id = chat.Id;
    IsGroupChat = chat.IsGroupChat;
    MaxUsers = chat.MaxUsers;
    ChatName = chat.ChatName;
    CreatedAt = chat.CreatedAt;
    UserIsAdmin = chat.Admins.Any(a => a.Id == mainUserId);
    Admins = chat.Admins.Select(a => new UserPartialResponse(a, mainUserId)).ToList();
    Users = chat.Users.Select(u => new UserPartialResponse(u, mainUserId)).ToList();
    Messages = chat.Messages.Select(m => new ChatMessage(m, mainUserId)).ToList();
    SystemMessages = chat.SystemMessages.Select(sm => new SystemMessagePartial(sm)).ToList();
    AdminIds = chat.Admins.Select(a => a.Id).ToList();
    MessageCount = chat.Messages.Count;
  }
  public CompleteChatResponse(Chat chat, string mainUserId, int messageCount) {
    Id = chat.Id;
    IsGroupChat = chat.IsGroupChat;
    MaxUsers = chat.MaxUsers;
    ChatName = chat.ChatName;
    CreatedAt = chat.CreatedAt;
    UserIsAdmin = chat.Admins.Any(a => a.Id == mainUserId);
    Admins = chat.Admins.Select(a => new UserPartialResponse(a, mainUserId)).ToList();
    Users = chat.Users.Select(u => new UserPartialResponse(u, mainUserId)).ToList();
    Messages = chat.Messages.Select(m => new ChatMessage(m, mainUserId)).ToList();
    SystemMessages = chat.SystemMessages.Select(sm => new SystemMessagePartial(sm)).ToList();
    AdminIds = chat.Admins.Select(a => a.Id).ToList();
    MessageCount = messageCount;
  }
  public CompleteChatResponse(Chat chat, List<ChatMessage> messages) {
    Id = chat.Id;
    IsGroupChat = chat.IsGroupChat;
    Admins = chat.Admins.Select(a => new UserPartialResponse(a)).ToList();
    MaxUsers = chat.MaxUsers;
    ChatName = chat.ChatName;
    CreatedAt = chat.CreatedAt;
    Users = chat.Users.Select(u => new UserPartialResponse(u)).ToList();
    Messages = messages;
    SystemMessages = chat.SystemMessages.Select(sm => new SystemMessagePartial(sm)).ToList();
    AdminIds = chat.Admins.Select(a => a.Id).ToList();
    MessageCount = chat.Messages.Count;
  }
  public CompleteChatResponse(Chat chat, List<ChatMessage> messages, int messageCount) {
    Id = chat.Id;
    IsGroupChat = chat.IsGroupChat;
    Admins = chat.Admins.Select(a => new UserPartialResponse(a)).ToList();
    MaxUsers = chat.MaxUsers;
    ChatName = chat.ChatName;
    CreatedAt = chat.CreatedAt;
    Users = chat.Users.Select(u => new UserPartialResponse(u)).ToList();
    Messages = messages;
    SystemMessages = chat.SystemMessages.Select(sm => new SystemMessagePartial(sm)).ToList();
    AdminIds = chat.Admins.Select(a => a.Id).ToList();
    MessageCount = messageCount;
  }
  public CompleteChatResponse(Chat chat, List<ChatMessage> messages, int messageCount, string mainUserId) {
    Id = chat.Id;
    IsGroupChat = chat.IsGroupChat;
    Admins = chat.Admins.Select(a => new UserPartialResponse(a, mainUserId)).ToList();
    MaxUsers = chat.MaxUsers;
    ChatName = chat.ChatName;
    CreatedAt = chat.CreatedAt;
    UserIsAdmin = chat.Admins.Any(a => a.Id == mainUserId);
    Users = chat.Users.Select(u => new UserPartialResponse(u, mainUserId)).ToList();
    Messages = messages;
    SystemMessages = chat.SystemMessages.Any() ? chat.SystemMessages.Select(sm => new SystemMessagePartial(sm)).ToList() : new List<SystemMessagePartial>();
    AdminIds = chat.Admins.Select(a => a.Id).ToList();
    MessageCount = messageCount;
  }

  [JsonConstructor]
  public CompleteChatResponse() { }
}

public class ChatBasicInfo {
  public string Id { get; set; } = null!;
  public string? ChatName { get; set; } = null!;
  public DateTime CreatedAt { get; set; }
  public ChatBasicInfo(Chat chat) {
    Id = chat.Id;
    CreatedAt = chat.CreatedAt;
    ChatName = chat.ChatName;
  }
}
public class ChatPreview {
  public string Id { get; set; } = null!;
  public string? ChatName { get; set; } = null!;
  public MessagePreview? LastMessage { get; set; } = null!;
  public ICollection<UserPartialResponse> Users { get; set; } = null!;
  public DateTime CreatedAt { get; set; }
  public bool? ShowOSNotification { get; set; }
  public bool? PlaySound { get; set; }

  public ChatPreview(Chat chat, string userId) {
    Id = chat.Id;
    ChatName = chat.ChatName;
    LastMessage = chat.Messages.Any() ? chat.Messages
      .OrderByDescending(m => m.SentAt)
      .Select(m => new MessagePreview {
        From = new UserPartialResponse(m.From),
        SentAt = m.SentAt,
        Text = m.Text,
        Read = m.ReadBy.Any(r => r.UserId == userId) || m.FromId == userId,
      })
      .FirstOrDefault()
      : null;
    Users = chat.Users.Where(u => u.Id != userId).Select(u => new UserPartialResponse(u)).ToList();
    CreatedAt = chat.CreatedAt;
    if (chat.ChatNotificationSettings != null && chat.ChatNotificationSettings.Any()) {
      var chatNotificationSetting =
        chat.ChatNotificationSettings.FirstOrDefault(n => n.UserId == userId);
      ShowOSNotification = chatNotificationSetting?.ShowOSNotification ?? null;
      PlaySound = chatNotificationSetting?.PlaySound ?? null;
    } else {
      ShowOSNotification = null;
      PlaySound = null;
    }
  }

  // Empty chat
  public ChatPreview(Chat chat) {
    Id = chat.Id;
    ChatName = chat.ChatName;
    LastMessage = null;
    Users = chat.Users.Select(u => new UserPartialResponse(u)).ToList();
    CreatedAt = chat.CreatedAt;
    ShowOSNotification = null;
    PlaySound = null;
  }

  [JsonConstructor]
  public ChatPreview() { }
}