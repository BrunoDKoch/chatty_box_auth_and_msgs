using Newtonsoft.Json;

namespace ChattyBox.Models.AdditionalModels;

public class MessagePreview {
  public UserPartialResponse From { get; set; } = null!;
  public DateTime SentAt { get; set; }
  public string Text { get; set; } = null!;
  public bool Read { get; set; }
}

public class ChatMessage {
  public string Id { get; set; } = null!;
  public string ChatId { get; set; } = null!;
  public DateTime SentAt { get; set; }
  public DateTime? EditedAt { get; set; }
  public string Text { get; set; } = null!;
  public string? ReplyToId { get; set; } = null!;
  public UserPartialResponse User { get; set; } = null!;
  public bool IsFromCaller { get; set; }
  public ICollection<ReadMessagePartialResponse> ReadBy { get; set; } = new List<ReadMessagePartialResponse>();
  public ChatMessage(Message message, string mainUserId, bool adminRequest = false) {
    Id = message.Id;
    ChatId = message.ChatId;
    // Omit text if flagged
    Text = (bool)message.FlaggedByAdmin! && !adminRequest ? "messageFlagged" : message.Text;
    ReplyToId = message.ReplyToId;
    SentAt = message.SentAt;
    EditedAt = message.EditedAt;
    IsFromCaller = message.FromId == mainUserId;
    ReadBy = message.ReadBy.Select(r => new ReadMessagePartialResponse(r.ReadBy, r.ReadAt)).ToList();
    User = new UserPartialResponse(message.From);
  }

  [JsonConstructor]
  public ChatMessage() { }
}

public class MessagesSearchResults {
  public List<ChatMessage> Messages = new();
  public int MessageCount { get; set; }
}

public class SystemMessagePartial {
  public string Id { get; set; } = null!;
  public string ChatId { get; set; } = null!;
  public DateTime FiredAt { get; set; }
  public UserPartialResponse InstigatingUser { get; set; } = null!;
  public string EventType { get; set; } = null!;
  public UserPartialResponse? AffectedUser { get; set; } = null!;
  public SystemMessagePartial(SystemMessage systemMessage) {
    Id = systemMessage.Id;
    ChatId = systemMessage.ChatId;
    FiredAt = systemMessage.FiredAt;
    InstigatingUser = new UserPartialResponse(systemMessage.InstigatingUser);
    AffectedUser = systemMessage.AffectedUser != null ? new UserPartialResponse(systemMessage.AffectedUser) : null;
    EventType = systemMessage.EventType;
  }

  [JsonConstructor]
  public SystemMessagePartial() { }
}

public class MessageReadInformationResponse {
  public ReadMessagePartialResponse ReadMessage { get; set; } = null!;
  public List<string>? ConnectionIds { get; set; } = null!;
}