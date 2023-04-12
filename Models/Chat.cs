﻿using System;
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

  [Column("createdAt")]
  public DateTime CreatedAt { get; set; }

  [InverseProperty("Chat")]
  public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
