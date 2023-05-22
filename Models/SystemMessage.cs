using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;

namespace ChattyBox.Models;

[Table("SystemMessage")]
public partial class SystemMessage {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = Guid.NewGuid().ToString();

  [Column("chatId")]
  [StringLength(1000)]
  public string ChatId { get; set; } = null!;

  [Column("firedAt")]
  public DateTime FiredAt { get; set; }

  [Column("instigatingUserId")]
  [StringLength(450)]
  public string InstigatingUserId { get; set; } = null!;

  [Column("eventType")]
  [StringLength(1000)]
  public string EventType { get; set; } = null!;

  [Column("affectedUserId")]
  [StringLength(450)]
  public string? AffectedUserId { get; set; } = null!;

  [ForeignKey("AffectedUserId")]
  [InverseProperty("SystemMessagesAffectingUser")]
  public virtual User? AffectedUser { get; set; } = null!;

  [ForeignKey("ChatId")]
  [InverseProperty("SystemMessages")]
  public virtual Chat Chat { get; set; } = null!;

  [ForeignKey("InstigatingUserId")]
  [InverseProperty("SystemMessageInstigatingUsers")]
  public virtual User InstigatingUser { get; set; } = null!;
}
