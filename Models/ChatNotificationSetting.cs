using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey("UserId", "ChatId")]
[Index("ChatId", Name = "ChatNotificationSettings_chatId_key", IsUnique = true)]
[Index("UserId", Name = "ChatNotificationSettings_userId_key", IsUnique = true)]
public partial class ChatNotificationSetting {
  [Key]
  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Key]
  [Column("chatId")]
  [StringLength(1000)]
  public string ChatId { get; set; } = null!;

  [Required]
  [Column("playSound")]
  public bool? PlaySound { get; set; }

  [Required]
  [Column("showOSNotification")]
  public bool? ShowOSNotification { get; set; }

  [ForeignKey("ChatId")]
  [InverseProperty("ChatNotificationSettings")]
  public virtual Chat Chat { get; set; } = null!;

  [ForeignKey("UserId")]
  [InverseProperty("ChatNotificationSettings")]
  public virtual User User { get; set; } = null!;
}
