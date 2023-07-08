using Microsoft.AspNetCore.Identity;
using Humanizer;
using Microsoft.Extensions.Localization;
using ChattyBox.Services;

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
    UserNotificationSetting = new UserNotificationSetting {
      PlaySound = true,
      ShowOSNotification = true,
    };
  }
}

public class LogInInfo {
  public string Email { get; set; } = null!;
  public string Password { get; set; } = null!;
  public bool Remember { get; set; } = false;
  public string? MFACode { get; set; }
  public bool RememberMultiFactor { get; set; } = false;
}

public class EmailValidationRequest {
  public string Email { get; set; } = null!;
  public string Code { get; set; } = null!;
}

public class LocationValidationRequest : EmailValidationRequest {
}

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
}

public class MessagesSearchResults {
  public List<ChatMessage> Messages = new List<ChatMessage>();
  public int MessageCount { get; set; }
}

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
    MessageCount = chat.Messages.Count();
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
    MessageCount = chat.Messages.Count();
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
}

public class UserPartialResponse {
  public UserPartialResponse(User user, string requestingUserId) {
    Id = user.Id;
    UserName = user.UserName!;
    Avatar = user.Avatar;
    IsBlocking = user.Blocking.Any(u => u.Id == requestingUserId);
    IsBlocked = user.BlockedBy.Any(u => u.Id == requestingUserId);
    Status = user.Status ?? String.Empty;
  }
  public UserPartialResponse(User user) {
    Id = user.Id;
    UserName = user.UserName!;
    Avatar = user.Avatar;
    IsBlocked = false;
    IsBlocking = false;
    Status = user.Status ?? String.Empty;
  }
  public string Id { get; set; } = null!;
  public string UserName { get; set; } = null!;
  public string? Avatar { get; set; } = null!;
  public bool IsBlocking { get; set; }
  public bool IsBlocked { get; set; }
  public string? Status { get; set; }
}

public class UserDetailedResponse : UserPartialResponse {

  private List<UserPartialResponse> GetFriendsInCommon(User user, string requestingUserId) {
    var list1 = user.Friends
      .Where(
          f => f.Friends.Any(ff => ff.Id == requestingUserId) ||
          f.IsFriendsWith.Any(ff => ff.Id == requestingUserId)
        )
      .Select(f => new UserPartialResponse(f, requestingUserId)).ToList();
    var list2 = user.IsFriendsWith
      .Where(
          f => f.Friends.Any(ff => ff.Id == requestingUserId) ||
          f.IsFriendsWith.Any(ff => ff.Id == requestingUserId)
        )
      .Select(f => new UserPartialResponse(f, requestingUserId)).ToList();
    return list1.Concat(list2).ToList();
  }
  public bool FriendRequestPending;
  public bool IsFriend;
  public List<UserPartialResponse> FriendsInCommon = new List<UserPartialResponse>();
  public List<ChatBasicInfo> ChatsInCommon = new List<ChatBasicInfo>();
  public bool IsAdmin;

  public UserDetailedResponse(User user, string requestingUserId) : base(user, requestingUserId) {
    FriendRequestPending =
      user.FriendRequestsSent.Any(fr => fr.UserBeingAdded.Id == requestingUserId) ||
      user.FriendRequestsReceived.Any(fr => fr.UserAdding.Id == requestingUserId);
    IsFriend = user.Friends.Any(f => f.Id == requestingUserId) || user.IsFriendsWith.Any(f => f.Id == requestingUserId);
    FriendsInCommon = GetFriendsInCommon(user, requestingUserId) ?? new List<UserPartialResponse>();
    ChatsInCommon = user.Chats
      .Where(c => c.Users.Any(u => u.Id == requestingUserId))
      .Select(c => new ChatBasicInfo(c))
      .ToList();

  }
}

public class UserPersonalInfo : UserPartialResponse {
  public List<FriendRequestFiltered> FriendRequests;
  public List<FriendResponse> Friends;
  public List<ChatPreview> Previews;
  public List<UserPartialResponse> Blocks;
  public bool IsAdmin;
  public UserPersonalInfo(User user) : base(user) {
    FriendRequests = user.FriendRequestsReceived.Select(f => new FriendRequestFiltered { UserAdding = new UserPartialResponse(f.UserAdding) }).ToList();
    Friends = user.Friends.Select(f => new FriendResponse(f, Id)).Concat(user.IsFriendsWith.Select(f => new FriendResponse(f, Id))).ToList();
    Previews = user.Chats.Select(c => new ChatPreview(c, Id)).OrderByDescending(c => c.LastMessage == null ? c.CreatedAt : c.LastMessage.SentAt).ToList();
    Blocks = user.Blocking.Select(b => new UserPartialResponse(b)).ToList();
    IsAdmin = user.Roles.Any(r => r.NormalizedName == "OWNER" || r.NormalizedName == "ADMIN");
  }
}

public class FriendResponse : UserPartialResponse {
  public FriendResponse(User user, bool isOnline, string requestingUserId) : base(user, requestingUserId) {
    IsOnline = isOnline;
  }
  public FriendResponse(User user, string requestingUserId) : base(user, requestingUserId) {
    IsOnline = user.ClientConnections.Any(c => (bool)c.Active!);
  }
  public bool IsOnline { get; set; }
}

public class ReadMessagePartialResponse : UserPartialResponse {
  public ReadMessagePartialResponse(User user, DateTime readAt) : base(user) {
    ReadAt = readAt;
  }
  public DateTime ReadAt;
}

public enum ImageSize {
  Small,
  Medium,
  Large,
  Full
}

public class LoginAttemptPartial {
  public string Id;
  public DateTime AttemptedAt;
  public string CityName;
  public string CountryIsoCode;
  public string OS;
  public string Browser;
  public string Device;
  public bool Success;
  public LoginAttemptPartial(UserLoginAttempt loginAttempt) {
    Id = loginAttempt.Id;
    AttemptedAt = loginAttempt.AttemptedAt;
    CityName = loginAttempt.CityName;
    CountryIsoCode = loginAttempt.CountryIsoCode;
    OS = string.Join(' ', loginAttempt.OS.Split(' ').Distinct()).Humanize();
    Browser = string.Join(' ', loginAttempt.Browser.Split(' ').Distinct());
    Device = string.Join(' ', loginAttempt.Device.Split(' ').Distinct());
    Success = loginAttempt.Success;
  }
}

public class LoginAttemptsResponse {
  public List<LoginAttemptPartial> UserLoginAttempts = new List<LoginAttemptPartial>();
  public int Count { get; set; }
}

public class PasswordRecoveryTokenRequest {
  public string Email { get; set; } = null!;
}

public class PasswordResetRequest : PasswordRecoveryTokenRequest {
  public string Token { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class MFADisableRequest {
  public string Password { get; set; } = null!;
}

public class FriendRequestFiltered {
  public UserPartialResponse UserAdding { get; set; } = null!;
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
}

public enum ExceptionActionType {
  MESSAGE,
  OTHER
}

public class MessageReadInformationResponse {
  public ReadMessagePartialResponse ReadMessage { get; set; } = null!;
  public List<string>? ConnectionIds { get; set; } = null!;
}

public class ClientConnectionPartialInfo {
  public string Id;
  public string UserId;
  public string ConnectionId;
  public string Browser;
  public string CityName;
  public string CountryIsoCode;
  public string CountryName;
  public string Device;
  public string GeoNameId;
  public string IpAddress;
  public string Os;
  public bool Active;
  public bool IsCurrentSession;
  public DateTime CreatedAt;
  public ClientConnectionPartialInfo(ClientConnection connection, string currentConnectionId) {
    Id = connection.Id;
    UserId = connection.UserId;
    ConnectionId = connection.ConnectionId;
    Browser = connection.Browser;
    CityName = connection.CityName;
    CountryIsoCode = connection.CountryIsoCode;
    CountryName = connection.CountryName;
    Device = connection.Device.Truncate(20);
    GeoNameId = connection.GeoNameId;
    IpAddress = connection.IpAddress;
    Os = connection.Os.Humanize();
    Active = (bool)connection.Active!;
    CreatedAt = connection.CreatedAt;
    IsCurrentSession = connection.ConnectionId == currentConnectionId;
  }
}

public class UserConnectionCallInfo : UserPersonalInfo {

  public UserConnectionCallInfo(User user) : base(user) {

  }
}

public class LockoutInfo {
  public bool Lockout { get; set; }
  public string LockoutReason { get; set; } = null!;
  public DateTime? LockoutEnd { get; set; }
  public bool Permanent { get; set; }
}

public class ReportRequest {
  public string ReportedUserId { get; set; } = null!;
  public string? MessageId { get; set; } = null!;
  public string? ChatId { get; set; } = null!;
  public string ReportReason { get; set; } = null!;
}

public class ReportPartial {
  public string Id { get; set; } = null!;
  public string ReportReason { get; set; } = null!;
  public bool? ViolationFound;
  public DateTime SentAt;
}

public class ReportUserResponse : UserPartialResponse {
  public List<ReportPartial> PastViolations;
  public string LockoutEnd;
  public ReportUserResponse(User user, IStringLocalizer<AdminController> localizer) : base(user) {
    if (user.LockoutEnd is null) LockoutEnd = String.Empty;
    else LockoutEnd = user.LockoutEnd == DateTimeOffset.MaxValue ?
      localizer.GetString("PermanentSuspension")! :
      $"{localizer.GetString("TemporarySuspension")} {TimeSpan.FromMinutes((DateTime.UtcNow - user.LockoutEnd!).Value.TotalMinutes).Humanize()}";
    PastViolations =
      user.ReportsAgainstUser
      .Where(r => r.ViolationFound is not null && (bool)r.ViolationFound)
      .Select(r => new ReportPartial {
        Id = r.Id,
        ReportReason = r.ReportReason,
        ViolationFound = r.ViolationFound,
        SentAt = r.SentAt,
      })
      .ToList();
  }
}

public class AdminActionPartial {
  public UserPartialResponse Admin;
  public string Action;
  public DateTime EnactedOn;
  public bool Revoked;
  public AdminActionPartial(AdminAction action) {
    Admin = new UserPartialResponse(action.Admin);
    Action = action.Action;
    EnactedOn = action.EnactedOn;
    Revoked = (bool)action.Revoked!;
  }
}

public class ReportResponse : ReportPartial {
  public ChatMessage? Message;
  public CompleteChatResponse? Chat;
  public UserPartialResponse ReportingUser;
  public ReportUserResponse ReportedUser;
  public List<AdminActionPartial> AdminActions;
  public ReportResponse(UserReport report, string adminId, IStringLocalizer<AdminController> localizer) {
    Id = report.Id;
    Message = report.Message is null ? null : new ChatMessage(report.Message, adminId, adminRequest: true);
    Chat = report.Chat is null ? null : new CompleteChatResponse(report.Chat, adminId);
    ReportedUser = new ReportUserResponse(report.ReportedUser, localizer);
    ReportingUser = new UserPartialResponse(report.ReportingUser);
    SentAt = report.SentAt;
    ReportReason = report.ReportReason;
    ViolationFound = report.ViolationFound;
    AdminActions = report.AdminActions.Select(a => new AdminActionPartial(a)).ToList();
  }
}

public class AdminActionRequest {
  public string ReportId { get; set; } = null!;
  public bool PermanentLockout = false;
  public DateTime? LockoutEnd;
  public bool ViolationFound = false;
}

public class UserSearchCall {
  public string UserName { get; set; } = null!;
  public string? ChatId { get; set; } = null!;
}

public class ChangeEmailRequest {
  public string CurrentEmail { get; set; } = null!;
  public string NewEmail { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class ChangePasswordRequest {
  public string CurrentPassword { get; set; } = null!;
  public string NewPassword { get; set; } = null!;
}

public class LoginAttemptResult {
  public UserLoginAttempt LoginAttempt { get; set; } = null!;
  public string FailureReason { get; set; } = null!;
  public bool SuspiciousLocation;
}