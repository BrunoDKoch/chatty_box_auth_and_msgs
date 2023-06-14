using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Table("Chat")]
public partial class Chat {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = null!;

  [Column("isGroupChat")]
  public bool IsGroupChat { get; set; }

  [Column("maxUsers")]
  public int MaxUsers { get; set; }

  [Column("chatName")]
  public string? ChatName { get; set; }

  [Column("createdAt")]
  public DateTime CreatedAt { get; set; }

  [InverseProperty("Chat")]
  public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

  [InverseProperty("Chat")]
  public virtual ICollection<SystemMessage> SystemMessages { get; set; } = new List<SystemMessage>();

  [ForeignKey("A")]
  [InverseProperty("Chats")]
  public virtual ICollection<User> Users { get; set; } = new List<User>();

  [ForeignKey("UserId")]
  [InverseProperty("IsAdminIn")]
  public virtual ICollection<User> Admins { get; set; } = new List<User>();

  [InverseProperty("Chat")]
  public virtual ICollection<ChatNotificationSetting> ChatNotificationSettings { get; set; } = new List<ChatNotificationSetting>();

  [InverseProperty("Chat")]
  public virtual ICollection<UserReport> UserReports { get; set; } = new List<UserReport>();
}
