using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

[Index("NormalizedEmail", Name = "EmailIndex")]
[Index("Id", Name = "IX_Users_Id", IsUnique = true)]
[Index("NormalizedUserName", Name = "UserNameIndex")]
[Index("Email", Name = "Users_email_key", IsUnique = true)]
[Index("NormalizedEmail", Name = "Users_normalizedEmail_key", IsUnique = true)]
public partial class User : IdentityUser {

  [InverseProperty("From")]
  public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

  [InverseProperty("User")]
  public virtual ICollection<UserClaim> UserClaims { get; set; } = new List<UserClaim>();

  [InverseProperty("User")]
  public virtual ICollection<UserLogin> UserLogins { get; set; } = new List<UserLogin>();

  [InverseProperty("User")]
  public virtual ICollection<UserToken> UserTokens { get; set; } = new List<UserToken>();

  [ForeignKey("UserId")]
  [InverseProperty("Users")]
  public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
