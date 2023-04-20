using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

public class UserInitialData {
  public string Email { get; set; } = null!;
  public string UserName { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class UserCreate : User {
  private PasswordHasher<User> passwordHasher = new PasswordHasher<User>();
  public UserCreate(UserInitialData data) {
    Email = data.Email;
    UserName = data.UserName;
    PasswordHash = passwordHasher.HashPassword(this, data.Password);
  }
}

public class LogInInfo {
  public string Email { get; set; } = null!;
  public string Password { get; set; } = null!;
  public bool Remember { get; set; } = false;
}

public class EmailValidationRequest {
  public string Code { get; set; } = null!;
}

public class MessagePreview {
  public User From { get; set; } = null!;
  public DateTime SentAt { get; set; }
  public string Text { get; set; } = null!;
}

public class FriendsResponse {
  public string UserName { get; set; } = null!;
  public string UserId { get; set; } = null!;
  public bool IsOnline { get; set; }
}

public class ChatMessage {
  public User User { get; set; } = null!;
  public Message Message { get; set; } = null!;
  public bool IsFromCaller { get; set; }
}

public class CompleteChatResponse {
  public string Id { get; set; } = null!;
  public bool IsGroupChat { get; set; }
  public int MaxUsers { get; set; }
  public string? ChatName { get; set; }
  public DateTime CreatedAt { get; set; }
  public ICollection<User> Users { get; set; } = new List<User>();
  public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatPreview {
  public string Id { get; set; } = null!;
  public string? ChatName { get; set; } = null!;
  public MessagePreview? LastMessage { get; set; } = null!;
  public ICollection<User> Users { get; set; } = null!;
}