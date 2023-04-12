using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Keyless]
[Table("_blocked")]
[Index("A", "B", Name = "_blocked_AB_unique", IsUnique = true)]
[Index("B", Name = "_blocked_B_index")]
public partial class Blocked {
  public string A { get; set; } = null!;

  public string B { get; set; } = null!;

  [ForeignKey("A")]
  public virtual User ANavigation { get; set; } = null!;

  [ForeignKey("B")]
  public virtual User BNavigation { get; set; } = null!;
}
