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

  private string GetAdminActionString(AdminActionRequest actionRequest) {
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
    [FromQuery] bool excludePending = false,
    [FromQuery] bool violationsFound = false
  ) {
    var adminId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(adminId);
    var reports = await _adminDB.ReadReports(skip, take, excludePending, violationsFound);
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