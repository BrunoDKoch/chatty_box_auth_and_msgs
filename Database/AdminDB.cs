using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.AspNetCore.Identity;
using MaxMind.GeoIP2;

namespace ChattyBox.Database;

public class AdminDB {

  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  public AdminDB(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
  }

  // Create
  async public Task<UserReport> CreateReport(ReportRequest reportRequest, string reportingUserId) {
    using var ctx = new ChattyBoxContext();
    var report = new UserReport {
      Id = Guid.NewGuid().ToString(),
      ReportReason = reportRequest.ReportReason,
      ChatId = reportRequest.ChatId,
      ReportingUserId = reportingUserId,
      MessageId = reportRequest.MessageId,
      SentAt = DateTime.UtcNow,
    };
    await ctx.UserReports.AddAsync(report);
    await ctx.SaveChangesAsync();
    return report;
  }

  // Read
  async public Task<List<UserReport>> ReadReports(int skip, int take) {
    using var ctx = new ChattyBoxContext();
    var reports = await ctx.UserReports
      .OrderByDescending(r => r.SentAt)
      .Skip(skip)
      .Take(take)
      .ToListAsync();
    return reports;
  }

  // Update
  async public Task<UserReport> SetViolationFound(string id, bool violationFound) {
    using var ctx = new ChattyBoxContext();
    var report = await ctx.UserReports.FirstOrDefaultAsync(r => r.Id == id);
    ArgumentNullException.ThrowIfNull(report);
    report.ViolationFound = violationFound;
    await ctx.SaveChangesAsync();
    return report;
  }
}