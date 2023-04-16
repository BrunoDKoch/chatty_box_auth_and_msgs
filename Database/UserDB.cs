using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Database;

public class UserDB {
  // Read
  async public Task<List<FriendsResponse>> GetAnUsersFriends(string userId) {
    using var ctx = new ChattyBoxContext();
    var user = await ctx.Users.FirstAsync(u => u.Id == userId);
    List<FriendsResponse> friends = new List<FriendsResponse>();
    foreach (var friend in user.Friends) {
      if (friend.Connection != null) {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          IsOnline = true
        });
      } else {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          IsOnline = false
        });
      }
    }
    return friends;
  }
}