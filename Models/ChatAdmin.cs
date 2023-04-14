using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey("UserId", "ChatId")]
[Table("ChatAdmin")]
public partial class ChatAdmin {
  [Key]
  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Key]
  [Column("chatId")]
  [StringLength(1000)]
  public string ChatId { get; set; } = null!;
}
