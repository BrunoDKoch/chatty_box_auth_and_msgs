using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

public partial class UserClaim : IdentityUserClaim<string> {
  [ForeignKey("UserId")]
  [InverseProperty("UserClaims")]
  public virtual User User { get; set; } = null!;
}
