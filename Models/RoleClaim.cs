using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

public partial class RoleClaim : IdentityRoleClaim<string> {
  [ForeignKey("RoleId")]
  [InverseProperty("RoleClaims")]
  public virtual Role Role { get; set; } = null!;
}