using Humanizer;
using Microsoft.Extensions.Localization;
using ChattyBox.Controllers;
using Newtonsoft.Json;

namespace ChattyBox.Models.AdditionalModels;
public class UserPartialResponse {
  public UserPartialResponse(User user, string requestingUserId) {
    Id = user.Id;
    UserName = user.UserName!;
    Avatar = user.Avatar;
    IsBlocking = user.Blocking.Any(u => u.Id == requestingUserId);
    IsBlocked = user.BlockedBy.Any(u => u.Id == requestingUserId);
    Status = user.Status ?? string.Empty;
  }
  public UserPartialResponse(User user) {
    Id = user.Id;
    UserName = user.UserName!;
    Avatar = user.Avatar;
    IsBlocked = false;
    IsBlocking = false;
    Status = user.Status ?? string.Empty;
  }

  [JsonConstructor]
  public UserPartialResponse() { }
  public string Id { get; set; } = null!;
  public string UserName { get; set; } = null!;
  public string? Avatar { get; set; } = null!;
  public bool IsBlocking { get; set; }
  public bool IsBlocked { get; set; }
  public string? Status { get; set; }
}

public class UserDetailedResponse : UserPartialResponse {

  static private List<UserPartialResponse> GetFriendsInCommon(User user, string requestingUserId) {
    var list1 = user.Friends
      .Where(
          f => f.Friends.Any(ff => ff.Id == requestingUserId) ||
          f.IsFriendsWith.Any(ff => ff.Id == requestingUserId)
        )
      .Select(f => new UserPartialResponse(f, requestingUserId)).ToList();
    var list2 = user.IsFriendsWith
      .Where(
          f => f.Friends.Any(ff => ff.Id == requestingUserId) ||
          f.IsFriendsWith.Any(ff => ff.Id == requestingUserId)
        )
      .Select(f => new UserPartialResponse(f, requestingUserId)).ToList();
    return list1.Concat(list2).ToList();
  }
  public bool FriendRequestPending;
  public bool IsFriend;
  public List<UserPartialResponse> FriendsInCommon = new();
  public List<ChatBasicInfo> ChatsInCommon = new();
  public bool IsAdmin;

  public UserDetailedResponse(User user, string requestingUserId) : base(user, requestingUserId) {
    FriendRequestPending =
      user.FriendRequestsSent.Any(fr => fr.UserBeingAdded.Id == requestingUserId) ||
      user.FriendRequestsReceived.Any(fr => fr.UserAdding.Id == requestingUserId);
    IsFriend = user.Friends.Any(f => f.Id == requestingUserId) || user.IsFriendsWith.Any(f => f.Id == requestingUserId);
    FriendsInCommon = GetFriendsInCommon(user, requestingUserId) ?? new List<UserPartialResponse>();
    ChatsInCommon = user.Chats
      .Where(c => c.Users.Any(u => u.Id == requestingUserId))
      .Select(c => new ChatBasicInfo(c))
      .ToList();

  }
}

public class UserPersonalInfo : UserPartialResponse {
  public List<FriendRequestFiltered> FriendRequests = new();
  public List<FriendResponse> Friends = new();
  public List<ChatPreview> Previews = new();
  public List<UserPartialResponse> Blocks = new();
  public bool IsAdmin;
  public UserPersonalInfo(User user) : base(user) {
    FriendRequests = user.FriendRequestsReceived.Select(f => new FriendRequestFiltered { UserAdding = new UserPartialResponse(f.UserAdding) }).ToList();
    Friends = user.Friends.Select(f => new FriendResponse(f, Id)).Concat(user.IsFriendsWith.Select(f => new FriendResponse(f, Id))).ToList();
    Previews = user.Chats.Select(c => new ChatPreview(c, Id)).OrderByDescending(c => c.LastMessage is null ? c.CreatedAt : c.LastMessage!.SentAt).ToList();
    Blocks = user.Blocking.Select(b => new UserPartialResponse(b)).ToList();
    IsAdmin = user.Roles.Any(r => r.NormalizedName == "OWNER" || r.NormalizedName == "ADMIN");
  }

  [JsonConstructor]
  public UserPersonalInfo() { }
}

public class FriendResponse : UserPartialResponse {
  public FriendResponse(User user, bool isOnline, string requestingUserId) : base(user, requestingUserId) {
    IsOnline = isOnline;
  }
  public FriendResponse(User user, string requestingUserId) : base(user, requestingUserId) {
    IsOnline = user.ClientConnections.Any(c => (bool)c.Active!);
  }

  [JsonConstructor]
  public FriendResponse() {

  }
  public bool IsOnline { get; set; }
}

public class ReadMessagePartialResponse : UserPartialResponse {
  public DateTime ReadAt;
  public ReadMessagePartialResponse(User user, DateTime readAt) : base(user) {
    ReadAt = readAt;
  }

  [JsonConstructor]
  public ReadMessagePartialResponse() { }
}

public class UserConnectionCallInfo : UserPersonalInfo {

  public UserConnectionCallInfo(User user) : base(user) {

  }
}

public class ReportUserResponse : UserPartialResponse {
  public List<ReportPartial> PastViolations;
  public string LockoutEnd;
  public ReportUserResponse(User user, IStringLocalizer<AdminController> localizer) : base(user) {
    if (user.LockoutEnd is null) LockoutEnd = string.Empty;
    else LockoutEnd = user.LockoutEnd == DateTimeOffset.MaxValue ?
      localizer.GetString("PermanentSuspension")! :
      $"{localizer.GetString("TemporarySuspension")} {TimeSpan.FromMinutes((DateTime.UtcNow - user.LockoutEnd!).Value.TotalMinutes).Humanize()}";
    PastViolations =
      user.ReportsAgainstUser
      .Where(r => r.ViolationFound is not null && (bool)r.ViolationFound)
      .Select(r => new ReportPartial {
        Id = r.Id,
        ReportReason = r.ReportReason,
        ViolationFound = r.ViolationFound,
        SentAt = r.SentAt,
      })
      .ToList();
  }
}

public class UserSearchCall {
  public string UserName { get; set; } = null!;
  public string? ChatId { get; set; } = null!;
}

public class FriendRequestFiltered {
  public UserPartialResponse UserAdding { get; set; } = null!;
}

public class ClientConnectionPartialInfo {
  public string Id;
  public string UserId;
  public string ConnectionId;
  public string Browser;
  public string CityName;
  public string CountryIsoCode;
  public string CountryName;
  public string Device;
  public string GeoNameId;
  public string IpAddress;
  public string Os;
  public bool Active;
  public bool IsCurrentSession;
  public DateTime CreatedAt;
  public ClientConnectionPartialInfo(ClientConnection connection, string currentConnectionId) {
    Id = connection.Id;
    UserId = connection.UserId;
    ConnectionId = connection.ConnectionId;
    Browser = connection.Browser;
    CityName = connection.CityName;
    CountryIsoCode = connection.CountryIsoCode;
    CountryName = connection.CountryName;
    Device = connection.Device.Truncate(20);
    GeoNameId = connection.GeoNameId;
    IpAddress = connection.IpAddress;
    Os = connection.Os.Humanize();
    Active = (bool)connection.Active!;
    CreatedAt = connection.CreatedAt;
    IsCurrentSession = connection.ConnectionId == currentConnectionId;
  }
}

public class UserIdAndConnections {
  public string Id { get; set; } = null!;
  public List<string> ConnectionIds = new();
}