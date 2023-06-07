using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Index("UserId", Name = "UserNotificationSettings_userId_key", IsUnique = true)]
public partial class UserNotificationSetting {
  [Key]
  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Required]
  [Column("playSound")]
  public bool? PlaySound { get; set; }

  [Required]
  [Column("showOSNotification")]
  public bool? ShowOSNotification { get; set; }

  [Required]
  [Column("showAlert")]
  public bool ShowAlert { get; set; } = true;

  [ForeignKey("UserId")]
  [InverseProperty("UserNotificationSetting")]
  public virtual User User { get; set; } = null!;
}
