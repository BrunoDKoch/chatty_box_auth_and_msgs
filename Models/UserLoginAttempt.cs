using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Models;

[Table("UserLoginAttempt")]
public partial class UserLoginAttempt {
  [Key]
  [Column("id")]
  [StringLength(1000)]
  public string Id { get; set; } = null!;

  [Column("userId")]
  [StringLength(450)]
  public string UserId { get; set; } = null!;

  [Column("attemptedAt")]
  public DateTime AttemptedAt { get; set; }

  [Column("ipAddress")]
  [StringLength(1000)]
  public string IpAddress { get; set; } = null!;

  [Column("geoNameId")]
  [StringLength(1000)]
  public string GeoNameId { get; set; } = null!;

  [Column("cityName")]
  [StringLength(1000)]
  public string CityName { get; set; } = null!;

  [Column("countryIsoCode")]
  [StringLength(1000)]
  public string CountryIsoCode { get; set; } = null!;

  [Column("countryName")]
  [StringLength(1000)]
  public string CountryName { get; set; } = null!;

  [Column("latitude")]
  public double Latitude { get; set; }

  [Column("longitude")]
  public double Longitude { get; set; }

  [Column("os")]
  public string OS { get; set; } = null!;

  [Column("browser")]
  public string Browser { get; set; } = null!;

  [Column("device")]
  public string Device { get; set; } = null!;

  [Column("success")]
  public bool Success { get; set; }

  [ForeignKey("UserId")]
  [InverseProperty("UserLoginAttempts")]
  public virtual User User { get; set; } = null!;
}
