using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models.AdditionalModels;
using ChattyBox.Models;
using ChattyBox.Database;
using ChattyBox.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.SignalR;

namespace ChattyBox.Controllers;

[ApiController]
[Authorize(Roles = "admin, owner")]
[Route("[controller]")]
public class AdminController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly IHubContext<MessagesHub> _hubContext;
  private readonly IStringLocalizer<AdminController> _localizer;
  private readonly AdminDB _adminDB;
  private readonly MessagesDB _messagesDB;

  public AdminController(
      IHubContext<MessagesHub> hubContext,
      IStringLocalizer<AdminController> localizer,
      AdminDB adminDB,
      MessagesDB messagesDB) {
    _hubContext = hubContext;
    _localizer = localizer;
    _adminDB = adminDB;
    _messagesDB = messagesDB;
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
    var reportsAndTotal = await _adminDB.ReadReports(skip, take, excludePending);
    var reports = reportsAndTotal.Item1.Select(r => new ReportResponse(r, adminId, _localizer)).ToList();
    var total = reportsAndTotal.Item2;
    return Ok(new { reports, total });
  }

  [HttpPut("Reports/{id}")]
  async public Task<IActionResult> UpdateReport(string id, [FromBody] bool violationFound) {
    var report = await _adminDB.SetViolationFound(id, violationFound);
    return Ok(report);
  }

  // Admin action
  [HttpPost("Action")]
  async public Task<IActionResult> CreateAdminAction([FromBody] AdminActionRequest actionRequest) {
    var adminId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    ArgumentNullException.ThrowIfNull(adminId);
    var reportIdAndAction = await _adminDB.CreateAdminAction(adminId, actionRequest);
    var reportId = reportIdAndAction.Item1;
    var actionPartial = new AdminActionPartial(reportIdAndAction.Item2);
    
    await _hubContext.Clients.Group("admins").SendAsync("action", new { reportId, actionPartial }, default);
    return Ok();
  }

  [HttpPost("Lockout/{userId}")]
  async public Task<IActionResult> LockUserOut(string userId, [FromBody] LockoutInfo lockoutInfo) {
    await _adminDB.LockUserOut(userId, lockoutInfo);
    return Ok();
  }

  // Messages
  [HttpDelete("Message/{id}")]
  async public Task<IActionResult> DeleteMessage(string id) {
    var message = await _messagesDB.DeleteMessageAsAdmin(id);
    // Propagate change to all chat members
    await _hubContext.Clients.Group(message.ChatId).SendAsync("messageDeleted", message.Id, default);
    return Ok();
  }

  // Get suspended users
  [HttpGet("Suspensions")]
  async public Task<IActionResult> GetSuspendedUsers([FromQuery] int skip, [FromQuery] int take) {
    var users = await _adminDB.GetSuspendedUsers(take, skip, _localizer);
    return Ok(users);
  }
}