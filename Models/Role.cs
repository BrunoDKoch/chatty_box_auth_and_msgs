using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

public partial class Role : IdentityRole<string> {

  [InverseProperty("Role")]
  public virtual ICollection<RoleClaim> RoleClaims { get; } = new List<RoleClaim>();

  [ForeignKey("RoleId")]
  [InverseProperty("Roles")]
  public virtual ICollection<User> Users { get; } = new List<User>();
}
