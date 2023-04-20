﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey(nameof(A), nameof(B))]
[Table("_ChatToUser")]
[Index("A", "B", Name = "_ChatToUser_AB_unique", IsUnique = true)]
[Index("B", Name = "_ChatToUser_B_index")]
public partial class ChatToUser {
  [StringLength(1000)]
  [Column(Order = 0)]
  public string A { get; set; } = null!;

  [Column(Order = 1)]
  public string B { get; set; } = null!;

  [ForeignKey("A")]
  public virtual Chat ANavigation { get; set; } = null!;

  [ForeignKey("B")]
  public virtual User BNavigation { get; set; } = null!;
}
