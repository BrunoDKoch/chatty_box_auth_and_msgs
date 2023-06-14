using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Table("Message")]
public partial class Message {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = null!;

  [Column("sentAt")]
  public DateTime SentAt { get; set; }

  [Column("editedAt")]
  public DateTime? EditedAt { get; set; } = null!;

  [Column("fromId")]
  [StringLength(450)]
  public string FromId { get; set; } = null!;

  [Column("chatId")]
  [StringLength(1000)]
  public string ChatId { get; set; } = null!;

  [Column("text")]
  [StringLength(1000)]
  public string Text { get; set; } = null!;

  [Column("replyToId")]
  [StringLength(1000)]
  public string? ReplyToId { get; set; } = null!;

  [ForeignKey("ChatId")]
  [InverseProperty("Messages")]
  public virtual Chat Chat { get; set; } = null!;

  [ForeignKey("FromId")]
  [InverseProperty("Messages")]
  public virtual User From { get; set; } = null!;

  [InverseProperty("ReplyTo")]
  public virtual ICollection<Message> InverseReplyTo { get; set; } = new List<Message>();

  [ForeignKey("ReplyToId")]
  [InverseProperty("InverseReplyTo")]
  public virtual Message ReplyTo { get; set; } = null!;

  [InverseProperty("Message")]
  public virtual ICollection<ReadMessage> ReadBy { get; set; } = new List<ReadMessage>();
  
  [InverseProperty("Message")]
  public virtual ICollection<UserReport> UserReports { get; set; } = new List<UserReport>();
}
