using Microsoft.EntityFrameworkCore;
using ChattyBox.Models;
using ChattyBox.Context;
using Microsoft.AspNetCore.Identity;
using MaxMind.GeoIP2;

namespace ChattyBox.Database;

public class AdminDB {

  // Create
  async public Task<UserReport> CreateReport(ReportRequest reportRequest, string reportingUserId) {
    using var ctx = new ChattyBoxContext();
    var report = new UserReport {
      Id = Guid.NewGuid().ToString(),
      ReportReason = reportRequest.ReportReason,
      ReportedUserId = reportRequest.ReportedUserId,
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
      .Include(r => r.ReportedUser)
      .Include(r => r.ReportingUser)
      .Include(r => r.Chat)
      .Include(r => r.Message)
      .Include(r => r.AdminActions)
        .ThenInclude(a => a.Admin)
      .OrderByDescending(r => r.SentAt)
      .Skip(skip)
      .Take(take)
      .ToListAsync();
    return reports;
  }

  async public Task<List<UserReport>> ReadReports(int skip, int take, bool excludePending) {
    using var ctx = new ChattyBoxContext();
    var reports = await ctx.UserReports
      .Include(r => r.ReportedUser)
        .ThenInclude(u => u.ReportsAgainstUser.Where(r => r.ViolationFound == null || (bool)r.ViolationFound))
      .Include(r => r.ReportingUser)
      .Include(r => r.Chat)
      .Include(r => r.Message)
      .Include(r => r.AdminActions)
        .ThenInclude(a => a.Admin)
      .Where(
        r => excludePending ? 
        r.ViolationFound != null && (!(bool)r.ViolationFound) || r.AdminActions.Any() :
        r.ViolationFound == null || ((bool)r.ViolationFound && !r.AdminActions.Any())
      )
      .OrderByDescending(r => r.SentAt)
      .Skip(skip)
      .Take(take)
      .ToListAsync();
    return reports;
  }

  async public Task<UserReport> GetReport(string id) {
    using var ctx = new ChattyBoxContext();
    var report = await ctx.UserReports
      .Include(r => r.ReportedUser)
      .Include(r => r.ReportingUser)
      .Include(r => r.Chat)
      .Include(r => r.Message)
      .FirstOrDefaultAsync(r => r.Id == id);
    ArgumentNullException.ThrowIfNull(report);
    return report;
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