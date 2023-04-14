using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Table("ClientConnection")]
public partial class ClientConnection {
  [Key]
  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Column("connectionId")]
  [StringLength(1000)]
  public string ConnectionId { get; set; } = null!;

  [ForeignKey("UserId")]
  public virtual User User {get; set;} = null!;

}