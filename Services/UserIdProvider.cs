using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ChattyBox.Context;
using Microsoft.EntityFrameworkCore;

namespace ChattyBox.Services;

public class UserIdProvider : IUserIdProvider {
  public string GetUserId(HubConnectionContext connectionContext) {
    try {
      foreach (var claim in connectionContext.User.Claims) {
        Console.WriteLine(claim);
      }
      var httpContext = connectionContext.GetHttpContext();
      ArgumentNullException.ThrowIfNull(httpContext);
      var accessToken = httpContext.Request.Query["access_token"];
      ArgumentNullException.ThrowIfNull(accessToken);
      var token = TokenService.DecodeToken(accessToken!);
      var email = token.Payload.First(p => p.Key == "email");
      ArgumentNullException.ThrowIfNull(email);
      using var ctx = new ChattyBoxContext();
      var id = ctx.Users.First(u => u.Email == (string)email.Value).Id;
      return id;
    } catch (Exception e) {
      Console.ForegroundColor = ConsoleColor.DarkRed;
      Console.WriteLine(e);
      return String.Empty;
    }
  }
}