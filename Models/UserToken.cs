using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

[PrimaryKey("UserId", "LoginProvider", "Name")]
public partial class UserToken : IdentityUserToken<string> {
  [ForeignKey("UserId")]
  [InverseProperty("UserTokens")]
  public virtual User User { get; set; } = null!;
}
