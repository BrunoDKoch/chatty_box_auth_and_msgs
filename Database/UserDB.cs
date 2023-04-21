using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Database;

public class UserDB {
  // Create
  async public Task<FriendRequest?> CreateFriendRequest(string addingId, string addedId) {
    using var ctx = new ChattyBoxContext();
    try {
      var friendRequest = new FriendRequest {
        UserAddingId = addingId,
        UserBeingAddedId = addedId,
        SentAt = DateTime.UtcNow,
      };
      await ctx.FriendRequests.AddAsync(friendRequest);
      await ctx.SaveChangesAsync();
      return friendRequest;
    } catch (Exception e) {
      Console.Error.Write(e);
      return null;
    }
  }

  // Read
  async public Task<List<FriendsResponse>> GetAnUsersFriends(string userId) {
    using var ctx = new ChattyBoxContext();
    var user = await ctx.Users
      .Include(u => u.Friends).ThenInclude(f => f.Connection)
      .Include(u => u.IsFriendsWith).ThenInclude(f => f.Connection)
      .FirstAsync(u => u.Id == userId);
    List<FriendsResponse> friends = new List<FriendsResponse>();
    List<User> userFriends = new List<User>();
    userFriends = user.Friends.Concat(user.IsFriendsWith).ToList();
    foreach (var friend in userFriends) {
      if (friend.Connection != null) {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          UserId = friend.Id,
          IsOnline = true
        });
      } else {
        friends.Add(new FriendsResponse {
          UserName = friend.UserName!,
          UserId = friend.Id,
          IsOnline = false
        });
      }
    }
    return friends;
  }

  async public Task<List<User>> GetUsers(string userId, string userName) {
    using var ctx = new ChattyBoxContext();
    // Ensuring the user doesn't somehow search for themselves by adding an ID filter
    var users = await ctx.Users.Where(u => u.NormalizedUserName!.StartsWith(userName.ToUpper()) && u.Id != userId).ToListAsync();
    return users;
  }

  async public Task<List<FriendRequest>> GetFriendRequests(string userId) {
    using var ctx = new ChattyBoxContext();
    var requests = await ctx.FriendRequests.Where(f => f.UserBeingAddedId == userId).Include(f => f.UserAdding).ToListAsync();
    return requests;
  }

  async public Task<User> GetSpecificUser(string userId) {
    using var ctx = new ChattyBoxContext();
    return await ctx.Users.FirstAsync(u => u.Id == userId);
  }

  // Update

  async public Task HandleFriendRequest(string userId, string addingId, bool accepting) {
    using var ctx = new ChattyBoxContext();
    ctx.FriendRequests.Remove(
      await ctx.FriendRequests.FirstAsync(f => f.UserBeingAddedId == userId && f.UserAddingId == addingId)
    );
    if (accepting) {
      var adding = await ctx.Users.FirstAsync(u => u.Id == addingId);
      var user = await ctx.Users.FirstAsync(u => u.Id == userId);
      adding.Friends.Add(user);
      user.IsFriendsWith.Add(adding);
    }
    await ctx.SaveChangesAsync();
  }
}