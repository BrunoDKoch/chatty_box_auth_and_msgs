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
  public virtual ICollection<ClientConnection> ClientConnections { get; set; } = new List<ClientConnection>();

  [Column("avatar")]
  public string? Avatar { get; set; }

  [Column("privacyLevel")]
  public int PrivacyLevel { get; set; } = 1;

  [Column("status")]
  public string? Status { get; set; }

  [Column("showStatus")]
  public bool ShowStatus { get; set; } = true;

  [Column("lockoutReason")]
  public string? LockoutReason { get; set; }

  [InverseProperty("From")]
  public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

  [InverseProperty("User")]
  public virtual ICollection<UserClaim> UserClaims { get; set; } = new List<UserClaim>();

  [InverseProperty("User")]
  public virtual ICollection<UserLogin> UserLogins { get; set; } = new List<UserLogin>();

  [InverseProperty("User")]
  public virtual ICollection<UserLoginAttempt> UserLoginAttempts { get; set; } = new List<UserLoginAttempt>();

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

  [InverseProperty("ReadBy")]
  public virtual ICollection<ReadMessage> ReadMessages { get; set; } = new List<ReadMessage>();

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

  [InverseProperty("User")]
  public virtual ICollection<ChatNotificationSetting> ChatNotificationSettings { get; set; } = new List<ChatNotificationSetting>();

  [InverseProperty("User")]
  public virtual UserNotificationSetting? UserNotificationSetting { get; set; }

  [InverseProperty("AffectedUser")]
  public virtual ICollection<SystemMessage> SystemMessagesAffectingUser { get; set; } = new List<SystemMessage>();

  [InverseProperty("InstigatingUser")]
  public virtual ICollection<SystemMessage> SystemMessageInstigatingUsers { get; set; } = new List<SystemMessage>();

  [InverseProperty("ReportedUser")]
  public virtual ICollection<UserReport> ReportsAgainstUser { get; set; } = new List<UserReport>();

  [InverseProperty("ReportingUser")]
  public virtual ICollection<UserReport> UserReports { get; set; } = new List<UserReport>();

  [InverseProperty("Admin")]
  public virtual ICollection<AdminAction> AdminActions { get; set; } = new List<AdminAction>();
}
