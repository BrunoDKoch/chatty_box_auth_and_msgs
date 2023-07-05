using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Net;
using MaxMind.GeoIP2.Responses;
using UAParser;

namespace ChattyBox.Models;

[Table("ClientConnection")]
public partial class ClientConnection {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = Guid.NewGuid().ToString();

  [Column("userId")]
  public string UserId { get; set; } = null!;

  [Column("connectionId")]
  [StringLength(1000)]
  public string ConnectionId { get; set; } = null!;

  [Column("browser")]
  [StringLength(1000)]
  public string Browser { get; set; } = null!;

  [Column("cityName")]
  [StringLength(1000)]
  public string CityName { get; set; } = null!;

  [Column("countryIsoCode")]
  [StringLength(1000)]
  public string CountryIsoCode { get; set; } = null!;

  [Column("countryName")]
  [StringLength(1000)]
  public string CountryName { get; set; } = null!;

  [Column("device")]
  [StringLength(1000)]
  public string Device { get; set; } = null!;

  [Column("geoNameId")]
  [StringLength(1000)]
  public string GeoNameId { get; set; } = null!;

  [Column("ipAddress")]
  [StringLength(1000)]
  public string IpAddress { get; set; } = null!;

  [Column("latitude")]
  public double Latitude { get; set; }

  [Column("longitude")]
  public double Longitude { get; set; }

  [Column("os")]
  [StringLength(1000)]
  public string Os { get; set; } = null!;

  [Required]
  [Column("active")]
  public bool? Active { get; set; } = true;

  [Column("createdAt")]
  public DateTime CreatedAt { get; set; }

  [ForeignKey("UserId")]
  [InverseProperty("ClientConnections")]
  public virtual User User { get; set; } = null!;
}
