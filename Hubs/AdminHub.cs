using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using ChattyBox.Database;
using ChattyBox.Misc;
using ChattyBox.Models;
using ChattyBox.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using UAParser;

namespace ChattyBox.Hubs;

[Authorize(Roles = "admin,owner")]
public class AdminHub : Hub {
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  private MessagesDB _messagesDB;
  private UserDB _userDB;

  public AdminHub(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      MaxMind.GeoIP2.WebServiceClient maxMindClient) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _userDB = new UserDB(_userManager, _roleManager, _configuration, _signInManager);
    _messagesDB = new MessagesDB(_userManager, _roleManager, _configuration, _signInManager, maxMindClient);
  }

  private string EnsureUserIdNotNull(string? userId) {
    ArgumentNullException.ThrowIfNull(userId);
    return userId;
  }
  
}