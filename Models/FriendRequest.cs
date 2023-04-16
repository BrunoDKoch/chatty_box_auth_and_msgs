using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[PrimaryKey("UserAddingId", "UserBeingAddedId")]
[Table("FriendRequest")]
public partial class FriendRequest {
  [Key]
  [Column("userAddingId")]
  public string UserAddingId { get; set; } = null!;

  [Key]
  [Column("userBeingAddedId")]
  public string UserBeingAddedId { get; set; } = null!;

  [Column("sentAt")]
  public DateTime SentAt { get; set; }

  [ForeignKey("UserAddingId")]
  [InverseProperty("FriendRequestsSent")]
  public virtual User UserAdding { get; set; } = null!;

  [ForeignKey("UserBeingAddedId")]
  [InverseProperty("FriendRequestsReceived")]
  public virtual User UserBeingAdded { get; set; } = null!;
}
