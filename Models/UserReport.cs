using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Table("UserReport")]
public partial class UserReport {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = null!;

  [Column("adminAction")]
  public string? AdminAction { get; set; }

  [Column("reportingUserId")]
  public string ReportingUserId { get; set; } = null!;

  [Column("reportedUserId")]
  public string ReportedUserId { get; set; } = null!;

  [Column("reportReason")]
  [StringLength(1000)]
  public string ReportReason { get; set; } = null!;

  [Column("chatId")]
  [StringLength(1000)]
  public string? ChatId { get; set; }

  [Column("messageId")]
  [StringLength(1000)]
  public string? MessageId { get; set; }

  [Column("sentAt")]
  public DateTime SentAt { get; set; }

  [Column("violationFound")]
  public bool? ViolationFound { get; set; }

  [ForeignKey("ChatId")]
  [InverseProperty("UserReports")]
  public virtual Chat? Chat { get; set; }

  [ForeignKey("MessageId")]
  [InverseProperty("UserReports")]
  public virtual Message? Message { get; set; }

  [ForeignKey("ReportedUserId")]
  [InverseProperty("ReportsAgainstUser")]
  public virtual User ReportedUser { get; set; } = null!;

  [ForeignKey("ReportingUserId")]
  [InverseProperty("UserReports")]
  public virtual User ReportingUser { get; set; } = null!;
}
