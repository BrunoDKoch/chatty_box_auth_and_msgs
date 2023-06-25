using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChattyBox.Context;
using ChattyBox.Models;
using ChattyBox.Hubs;
using ChattyBox.Misc;
using MaxMind.GeoIP2;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Caching;
using Microsoft.IdentityModel.Logging;
using System.Net.Mime;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Localization;
using ChattyBox.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.FileProviders;
using ChattyBox.Services;

var reqOrigin = "_reqOrigin";

var allowedLetters = ValidCharacters.GetLetters();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options => {
  options.AddPolicy(name: reqOrigin, policy => {
    policy.WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials();
  });
});

builder.Services.AddControllers().AddJsonOptions(options => {
  options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
  options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
  options.JsonSerializerOptions.IncludeFields = true;
});
builder.Services.AddSignalR().AddJsonProtocol(o => {
  o.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
  o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
  o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
  o.PayloadSerializerOptions.IgnoreReadOnlyProperties = false;
  o.PayloadSerializerOptions.IgnoreReadOnlyFields = false;
  o.PayloadSerializerOptions.IncludeFields = true;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ChattyBoxContext>(options => {
  options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
});

builder.Services.AddIdentity<User, Role>(options => {
  options.User.RequireUniqueEmail = true;
  options.User.AllowedUserNameCharacters = allowedLetters;
  options.SignIn.RequireConfirmedEmail = true;
  options.Password.RequiredLength = 8;
  options.Lockout.MaxFailedAccessAttempts = 4;
})
  .AddEntityFrameworkStores<ChattyBoxContext>()
  .AddRoles<Role>()
  .AddDefaultTokenProviders()
  .AddTokenProvider<AuthenticatorTokenProvider<User>>(TokenOptions.DefaultAuthenticatorProvider)
  .AddTokenProvider<EmailTokenProvider<User>>(TokenOptions.DefaultEmailProvider);

builder.Services.AddAuthorization(options => {
  options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
});

builder.Services.ConfigureApplicationCookie(options => {
  options.Cookie.Path = "/";
  options.LoginPath = "/User/LoggedIn";
  options.LogoutPath = "/User/Logout";
  options.ExpireTimeSpan = TimeSpan.FromDays(14);
  options.SlidingExpiration = true;
  options.Cookie.IsEssential = true;
  if (!builder.Environment.IsDevelopment())
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var provider = builder.Environment.ContentRootFileProvider;

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

builder.Services.AddImageSharp()
  .Configure<PhysicalFileSystemProviderOptions>(options => {
    options.ProviderRootPath = ".";
    options.ProcessingBehavior = ProcessingBehavior.All;
  })
  .Configure<PhysicalFileSystemCacheOptions>(options => {
    options.CacheRootPath = "./";
    options.CacheFolder = "cache";
  })
  .AddProvider<PhysicalFileSystemProvider>();
builder.Services.Configure<WebServiceClientOptions>(builder.Configuration.GetSection("MaxMind"));
builder.Services.AddHttpClient<WebServiceClient>();

builder.Services.AddLocalization();
builder.Services.AddSingleton<LocalizationMiddleware>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddSingleton<EmailService>();

builder.Services.AddRateLimiter(options => {
  options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
  app.UseSwagger();
  app.UseSwaggerUI();
  IdentityModelEventSource.ShowPII = true;
}

app.UseRateLimiter();

var supportedCultures = new List<RequestCulture> { new RequestCulture("en"), new RequestCulture("pt"), new RequestCulture("es") };
var localizationProviders = new List<IRequestCultureProvider> { new CookieRequestCultureProvider { CookieName = "lang" } };
var localizationOptions = new RequestLocalizationOptions {
  DefaultRequestCulture = supportedCultures[0],
  RequestCultureProviders = localizationProviders,
  SupportedCultures = supportedCultures.Select(s => s.Culture).ToList(),
  SupportedUICultures = supportedCultures.Select(s => s.UICulture).ToList()
};

app.UseRequestLocalization(localizationOptions);

app.UseMiddleware<LocalizationMiddleware>();

app.UseExceptionHandler(exceptionHandler => {
  exceptionHandler.Run(async context => {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = MediaTypeNames.Application.Json;
    var ex = context.Features.Get<IExceptionHandlerPathFeature>();
    if (ex is not null) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine(context.Request.Path.Value);
      Console.ResetColor();
      if (context.Request.Path.HasValue && context.Request.Path.Value.Contains("/api/v1/User")) {
        if (ex.Error.GetType().ToString() == "ArgumentNullException") {
          context.Response.StatusCode = StatusCodes.Status401Unauthorized;
          await context.Response.WriteAsync("Invalid credentials");
          return;
        }
      }
      string message;
      switch (ex.Error) {
        case Microsoft.Data.SqlClient.SqlException sqlException:
          message = $"Database error {sqlException.Number}";
          break;
        default:
          message = ex.Error.Message;
          break;
      }
      context.Response.StatusCode = StatusCodes.Status500InternalServerError;
      await context.Response.WriteAsync(message);
    }
  });
});

app.UseHsts();

app.UseHttpsRedirection();

app.UsePathBase(new PathString("/api/v1"));

app.UseCors(reqOrigin);

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.UseImageSharp();

app.UseStaticFiles(new StaticFileOptions {
  FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "static")),
  RequestPath = "/static"
});

app.MapControllers();

app.MapHub<MessagesHub>("/hub/messages");

app.Run();
