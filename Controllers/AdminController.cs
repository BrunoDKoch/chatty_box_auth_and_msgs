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
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Humanizer;

[ApiController]
[Authorize(Roles = "admin, owner")]
[Route("[controller]")]
public class AdminController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;
  private readonly IHubContext<MessagesHub> _hubContext;
  private readonly IStringLocalizer<AdminController> _localizer;
  private readonly AdminDB _adminDB;

  public AdminController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager,
      IHubContext<MessagesHub> hubContext,
      IStringLocalizer<AdminController> localizer) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
    _hubContext = hubContext;
    _localizer = localizer;
    _adminDB = new AdminDB();
  }

  static private string GetAdminActionString(AdminActionRequest actionRequest) {
    if (!actionRequest.ViolationFound)
      return "none";
    else if (actionRequest.PermanentLockout)
      return "permanent suspension";
    else if (actionRequest.LockoutEnd is not null)
      return $"suspension for {(DateTime.UtcNow - actionRequest.LockoutEnd)!.Value.Humanize(3)}";
    else
      return "message retained";
  }

  // Admin check
  [AllowAnonymous]
  [HttpGet("IsAdmin")]
  public IActionResult AdminCheck() {
    return Ok(HttpContext.User.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value == "admin" || c.Value == "owner")));
  }

  // Reports
  [HttpGet("Reports")]
  async public Task<IActionResult> GetReports(
    [FromQuery] int skip,
    [FromQuery] int take,
    [FromQuery] bool excludePending = false
  ) {
    var adminId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(adminId);
    var reports = await _adminDB.ReadReports(skip, take, excludePending);
    var response = reports.Select(r => new ReportResponse(r, adminId, _localizer)).ToList();
    using var ctx = new ChattyBoxContext();
    var total = await ctx.UserReports.Where(r => r.AdminActions.Any() != excludePending).CountAsync();
    return Ok(new { reports = response, total });
  }

  [HttpPut("Reports/{id}")]
  async public Task<IActionResult> UpdateReport(string id, [FromBody] bool violationFound) {
    using var ctx = new ChattyBoxContext();
    var report = await ctx.UserReports.FirstOrDefaultAsync(r => r.Id == id);
    ArgumentNullException.ThrowIfNull(report);
    report.ViolationFound = violationFound;
    return Ok(report);
  }

  // Admin action
  [HttpPost("Action")]
  async public Task<IActionResult> CreateAdminAction([FromBody] AdminActionRequest actionRequest) {
    var adminId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(adminId);
    var action = new AdminAction {
      AdminId = adminId,
      ReportId = actionRequest.ReportId,
      Action = GetAdminActionString(actionRequest)
    };
    using var ctx = new ChattyBoxContext();
    var report =
      await ctx.UserReports
        .Include(r => r.ReportedUser)
        .FirstOrDefaultAsync(r => r.Id == action.ReportId);
    ArgumentNullException.ThrowIfNull(report);
    report.ViolationFound = actionRequest.ViolationFound;
    if (actionRequest.LockoutEnd is not null || actionRequest.PermanentLockout) {
      report.ReportedUser.LockoutEnd = actionRequest.LockoutEnd ?? DateTimeOffset.MaxValue;
      report.ReportedUser.LockoutReason = report.ReportReason;
    }
    await ctx.AdminActions.AddAsync(action);
    await ctx.SaveChangesAsync();
    await _hubContext.Clients.Group("admins").SendAsync("action", new { reportId = report.Id, actionPartial = new AdminActionPartial(action) }, default);
    return Ok();
  }

  [HttpPost("Lockout/{userId}")]
  async public Task<IActionResult> LockUserOut(string userId, [FromBody] LockoutInfo lockoutInfo) {
    var user = await _userManager.FindByIdAsync(userId);
    ArgumentNullException.ThrowIfNull(user);
    if (!lockoutInfo.Lockout)
      user.LockoutEnd = DateTimeOffset.MinValue;
    else if (lockoutInfo.Permanent)
      user.LockoutEnd = DateTimeOffset.MaxValue;
    else
      user.LockoutEnd = lockoutInfo.LockoutEnd;

    user.LockoutReason = lockoutInfo.LockoutReason;
    await _userManager.UpdateAsync(user);
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

  // Get suspended users
  [HttpGet("Suspensions")]
  async public Task<IActionResult> GetSuspendedUsers([FromQuery] int skip, [FromQuery] int take) {
    var suspendedUsers = await _userManager.Users
      .Where(u => u.LockoutEnd > DateTime.UtcNow)
      .Include(u => u.ReportsAgainstUser)
      .OrderByDescending(u => u.LockoutEnd)
      .Select(u => new ReportUserResponse(u, _localizer))
      .ToListAsync();
    var users = suspendedUsers.Skip(skip)
      .Take(take);
    var total = suspendedUsers.Count;
    return Ok(new { users, total });
  }
}