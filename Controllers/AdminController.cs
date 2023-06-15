using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Context;
using ChattyBox.Services;
using ChattyBox.Database;
using ChattyBox.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MaxMind.GeoIP2;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

[ApiController]
[Authorize(Roles = "admin, owner")]
[Route("[controller]")]
public class AdminController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly WebServiceClient _maxMindClient;
  private readonly IWebHostEnvironment _webHostEnvironment;
  private readonly IHubContext<MessagesHub> _hubContext;
  private AdminDB _adminDB;

  public AdminController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      WebServiceClient maxMindClient,
      IWebHostEnvironment webHostEnvironment,
      IHubContext<MessagesHub> hubContext) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _maxMindClient = maxMindClient;
    _webHostEnvironment = webHostEnvironment;
    _hubContext = hubContext;
    _adminDB = new AdminDB(_userManager, _roleManager, _configuration, _signInManager);
  }

  // Admin check
  [AllowAnonymous]
  [HttpGet("IsAdmin")]
  async public Task<IActionResult> AdminCheck() {
    var user = await _userManager.GetUserAsync(HttpContext.User);
    ArgumentNullException.ThrowIfNull(user);
    var roles = await _userManager.GetRolesAsync(user);
    return Ok(roles.Any(r => r == "admin" || r == "owner"));
  }

  // Reports
  [HttpGet("Reports")]
  async public Task<IActionResult> GetReports([FromQuery] int skip, [FromQuery] int take) {
    var adminId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(adminId);
    var reports = await _adminDB.ReadReports(skip, take);
    var response = reports.Select(r => new ReportResponse(r, adminId)).ToList();
    return Ok(response);
  }

  [HttpPut("Reports/{id}")]
  async public Task<IActionResult> UpdateReport(string id, [FromBody] bool violationFound) {
    using var ctx = new ChattyBoxContext();
    var report = await ctx.UserReports.FirstOrDefaultAsync(r => r.Id == id);
    ArgumentNullException.ThrowIfNull(report);
    report.ViolationFound = violationFound;
    return Ok(report);
  }

  // Lockout
  [HttpPut("User/{id}")]
  async public Task<IActionResult> LockUserOut(string id, [FromBody] LockoutInfo lockoutInfo) {
    var user = await _userManager
      .Users
      .Include(u => u.ClientConnections)
      .FirstOrDefaultAsync(u => u.Id == id);
    ArgumentNullException.ThrowIfNull(user);

    // Set lockout
    if (!lockoutInfo.Lockout)
      user.LockoutEnd = DateTimeOffset.MinValue;
    else {
      user.LockoutEnd = lockoutInfo.LockoutEnd;
      user.LockoutReason = lockoutInfo.LockoutReason;
    }
    await _userManager.UpdateAsync(user);

    // Force user to log out
    await _hubContext.Clients.User(user.Id).SendAsync("forceLogOut", default);
    return Ok();
  }

  // Messages
  [HttpDelete("Message/{id}")]
  async public Task<IActionResult> DeleteMessage(string id) {
    using var ctx = new ChattyBoxContext();
    var message = await ctx.Messages.FirstOrDefaultAsync(m => m.Id == id);
    ArgumentNullException.ThrowIfNull(message);
    ctx.Remove(message);
    await ctx.SaveChangesAsync();

    // Propagate change to all chat members
    await _hubContext.Clients.Group(message.ChatId).SendAsync("messageDeleted", message.Id, default);
    return Ok();
  }


}