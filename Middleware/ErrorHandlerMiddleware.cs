using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Diagnostics;
using System.Net.Mime;
using ChattyBox.Models;

namespace ChattyBox.Middleware;

public class ErrorHandlerMiddleware : IMiddleware {
  private readonly IStringLocalizer<ErrorHandlerMiddleware> _localizer;
  public ErrorHandlerMiddleware(IStringLocalizer<ErrorHandlerMiddleware> localizer) : base() {
    _localizer = localizer;
  }

  async private Task HandleLoginError(HttpContext context, Exception ex) {
    switch (ex) {
      case ArgumentNullException:
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync(_localizer.GetString("401Auth"));
        break;
      case MFACodeRequiredException:
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("");
        break;
      default:
        await context.Response.WriteAsync(_localizer.GetString($"{context.Response.StatusCode}"));
        break;
    }
    
  }

  async private Task HandleUserError(HttpContext context, Exception ex) {
    var path = context.Request.Path.Value;
    if (string.IsNullOrEmpty(path)) return;
    path = path.ToLower();
    if (path.EndsWith("login")) {
      await HandleLoginError(context, ex);
      return;
    }
    if (path.EndsWith("upload")) {
      if (ex.ToString() == "InvalidOperationException") {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync(_localizer.GetString("FileTooBig"));
      }
    }
  }

  public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
    try {
      await next(context);
    } catch (Exception exception) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.Error.WriteLine(exception);
      Console.ResetColor();
      context.Response.ContentType = MediaTypeNames.Application.Json;
      string errorId = Guid.NewGuid().ToString();
      exception.AddSentryTag("path", context.Request.Path.ToString() ?? "unknown");
      exception.AddSentryTag("ip", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
      exception.AddSentryTag("stackTrace", exception.StackTrace!);
      exception.AddSentryTag("id", errorId);
      if (context.Request.Path.HasValue && context.Request.Path.Value.Contains("/api/v1/User")) {
        await HandleUserError(context, exception);
        return;
      }

      context.Response.StatusCode = exception switch {
        CustomException e => (int)e.Status,
        _ => StatusCodes.Status500InternalServerError,
      };
      string message = exception switch {
        Microsoft.Data.SqlClient.SqlException sqlException => $"{_localizer.GetString("DatabaseError")} {sqlException.Number}",
        _ => $"{_localizer.GetString(context.Response.StatusCode.ToString())}",
      };
      message += $"\n{_localizer.GetString("ErrorLogged").Value} {errorId}";
      await context.Response.WriteAsync(message);
    }
  }
}