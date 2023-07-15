using Microsoft.Extensions.Localization;
using ChattyBox.Controllers;

namespace ChattyBox.Models.AdditionalModels;

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