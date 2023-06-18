using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey("ReportId", "AdminId")]
[Table("AdminAction")]
public partial class AdminAction {
  [Key]
  [Column("reportId")]
  [StringLength(1000)]
  public string ReportId { get; set; } = null!;

  [Key]
  [Column("adminId")]
  public string AdminId { get; set; } = null!;

  [Column("action")]
  [StringLength(1000)]
  public string Action { get; set; } = null!;

  [Column("enactedOn")]
  public DateTime EnactedOn { get; set; }

  [Column("revoked")]
  public bool Revoked { get; set; }

  [ForeignKey("AdminId")]
  [InverseProperty("AdminActions")]
  public virtual User Admin { get; set; } = null!;

  [ForeignKey("ReportId")]
  [InverseProperty("AdminActions")]
  public virtual UserReport Report { get; set; } = null!;
}
