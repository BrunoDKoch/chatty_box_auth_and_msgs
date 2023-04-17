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
  [InverseProperty("User")]
  [Column("clientConnection")]
  public virtual ClientConnection Connection { get; set; } = null!;

  [Column("avatar")]
  public string? Avatar { get; set; }

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

  [ForeignKey("B")]
  [InverseProperty("Users")]
  public virtual ICollection<Chat> Chats { get; set; } = new List<Chat>();

  [ForeignKey("UserId")]
  [InverseProperty("Admins")]
  public virtual ICollection<Chat> IsAdminIn { get; set; } = new List<Chat>();

  [ForeignKey("MessageId")]
  [InverseProperty("ReadBy")]
  public virtual ICollection<Message> ReadMessages { get; set; } = new List<Message>();

  [InverseProperty("IsFriendsWith")]
  public virtual ICollection<User> Friends { get; set; } = new List<User>();

  [InverseProperty("Friends")]
  public virtual ICollection<User> IsFriendsWith { get; set; } = new List<User>();

  [InverseProperty("BlockedBy")]
  public virtual ICollection<User> Blocking { get; set; } = new List<User>();

  [InverseProperty("Blocking")]
  public virtual ICollection<User> BlockedBy { get; set; } = new List<User>();

  [InverseProperty("UserAdding")]
  public virtual ICollection<FriendRequest> FriendRequestsSent { get; set; } = new List<FriendRequest>();

  [InverseProperty("UserBeingAdded")]
  public virtual ICollection<FriendRequest> FriendRequestsReceived { get; set; } = new List<FriendRequest>();
}
