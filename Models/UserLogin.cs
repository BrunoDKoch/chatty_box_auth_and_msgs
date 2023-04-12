using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

[PrimaryKey("LoginProvider", "ProviderKey")]
public partial class UserLogin : IdentityUserLogin<string> {

  [ForeignKey("UserId")]
  [InverseProperty("UserLogins")]
  public virtual User User { get; set; } = null!;
}
