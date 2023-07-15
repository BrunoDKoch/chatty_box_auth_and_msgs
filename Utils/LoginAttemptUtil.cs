using ChattyBox.Models;
using ChattyBox.Services;
using MaxMind.GeoIP2;
using System.Net;

namespace ChattyBox.Utils;

public class LoginAttemptHelper {
  private readonly WebServiceClient _maxMindClient;
  private readonly IConfiguration _configuration;
  public LoginAttemptHelper(
    WebServiceClient maxMindClient,
    IConfiguration configuration
  ) {
    _maxMindClient = maxMindClient;
    _configuration = configuration;
  }
  static private string JoinThreeStrings(string string1, string string2, string string3, bool includesVersionNumber = false) {
    var listOfStrings = new List<string> {
      string1,
      string2,
    };
    if (!string.IsNullOrEmpty(string3)) {
      if (includesVersionNumber) listOfStrings[1] = $"{string2}.{string3}";
      else listOfStrings.Add(string3);
    }
    return string.Join(' ', listOfStrings.Distinct());
  }
  static public double CalculateDistance(double lat1, double lon1, double lat2, double lon2) {
    const double R = 6371; // Radius of the Earth in km
    var dLat = ToRadians(lat2 - lat1);
    var dLon = ToRadians(lon2 - lon1);
    var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    var distance = R * c; // Distance in km
    return distance;
  }

  static private double ToRadians(double degrees) {
    return degrees * Math.PI / 180;
  }

  async public Task<UserLoginAttempt> CreateLoginAttempt(string userId, HttpContext context) {
    IPAddress ipAddress;
    // TODO: when ready for deployment, remove if statement
    if (context.Connection.RemoteIpAddress == null || new List<string> { "::1", "127.0.0.1" }.Contains(context.Connection.RemoteIpAddress.ToString())) {
      ipAddress = IPAddress.Parse(_configuration.GetValue<string>("TestIP")!);
    } else {
      ipAddress = context.Connection.RemoteIpAddress;
    }
    var clientInfo = ParsingService.ParseContext(context);
    var city = await _maxMindClient.CityAsync(ipAddress);
    var loginAttempt = new UserLoginAttempt {
      Id = Guid.NewGuid().ToString(),
      UserId = userId,
      IpAddress = ipAddress.ToString(),
      CityName = city.City.Name ?? "unknown",
      GeoNameId = city.City.GeoNameId is null ? "unknown" : city.City.GeoNameId.ToString()!,
      CountryName = city.Country.Name ?? "unknown",
      CountryIsoCode = city.Country.IsoCode ?? "unknown",
      Latitude = (double)city.Location.Latitude!,
      Longitude = (double)city.Location.Longitude!,
      OS = JoinThreeStrings(clientInfo.OS.Family, clientInfo.OS.Major, clientInfo.OS.Minor, includesVersionNumber: true),
      Device = JoinThreeStrings(clientInfo.Device.Brand, clientInfo.Device.Family, clientInfo.Device.Model),
      Browser = JoinThreeStrings(clientInfo.UA.Family, clientInfo.UA.Major, clientInfo.UA.Minor, includesVersionNumber: true),
    };
    return loginAttempt;
  }
}