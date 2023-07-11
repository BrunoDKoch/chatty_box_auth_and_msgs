using System.Net;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Diagnostics;
using System.Net.Mime;
using ChattyBox.Models;
using Microsoft.Extensions.FileProviders;

namespace ChattyBox.Middleware;

public class ErrorHandlerMiddleware : IMiddleware {
  private readonly IStringLocalizer<ErrorHandlerMiddleware> _localizer;
  public ErrorHandlerMiddleware(IStringLocalizer<ErrorHandlerMiddleware> localizer) : base() {
    _localizer = localizer;
  }

  async private Task HandleLoginError(HttpContext context, IExceptionHandlerPathFeature ex) {
    if (ex.Error.GetType().ToString() == "ArgumentNullException") {
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;
      await context.Response.WriteAsync(_localizer.GetString("401Auth"));
      return;
    }
    await context.Response.WriteAsync(_localizer.GetString($"{context.Response.StatusCode}"));
  }

  async private Task HandleUserError(HttpContext context, IExceptionHandlerPathFeature ex) {
    var exType = ex.GetType();
    var path = context.Request.Path.Value;
    if (String.IsNullOrEmpty(path)) return;
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
      var ex = context.Features.Get<IExceptionHandlerPathFeature>();
      if (ex is null) return;
      context.Response.ContentType = MediaTypeNames.Application.Json;
      string errorId = Guid.NewGuid().ToString();
      exception.AddSentryTag("source", ex.Path ?? "unknown");
      exception.AddSentryTag("stackTrace", exception.StackTrace!);
      exception.AddSentryTag("id", errorId);
      if (context.Request.Path.HasValue && context.Request.Path.Value.Contains("/api/v1/User")) {
        await HandleUserError(context, ex);
        return;
      }
      string message;
      context.Response.StatusCode = StatusCodes.Status500InternalServerError;
      switch (ex.Error) {
        case CustomException e:
          context.Response.StatusCode = (int)((CustomException)ex).Status;
          message = $"{_localizer.GetString(context.Response.StatusCode.ToString())}";
          break;
        case Microsoft.Data.SqlClient.SqlException sqlException:
          message = $"{_localizer.GetString("DatabaseError")} {sqlException.Number}";
          break;
        default:
          message = $"{_localizer.GetString(context.Response.StatusCode.ToString())}";
          break;
      }
      message += $"\n{_localizer.GetString("ErrorLogged").Value} {errorId}";
      await context.Response.WriteAsync(message);
    }
  }
}