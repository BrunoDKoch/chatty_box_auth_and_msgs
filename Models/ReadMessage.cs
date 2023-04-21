using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey("MessageId", "UserId")]
[Table("ReadMessage")]
public partial class ReadMessage {
  [Key]
  [Column("messageId")]
  [StringLength(1000)]
  public string MessageId { get; set; } = null!;

  [Key]
  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Column("readAt")]
  public DateTime ReadAt { get; set; }

  [ForeignKey("MessageId")]
  [InverseProperty("ReadBy")]
  public virtual Message Message { get; set; } = null!;

  [ForeignKey("UserId")]
  [InverseProperty("ReadMessages")]
  public virtual User ReadBy { get; set; } = null!;
}
