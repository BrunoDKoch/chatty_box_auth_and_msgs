namespace ChattyBox.Middleware;

public static class Startup {
  public static IApplicationBuilder UseGlobalExceptionHandler(this WebApplication app) =>
    app.UseMiddleware<ErrorHandlerMiddleware>();

}