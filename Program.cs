using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.EntityFrameworkCore;
using ChattyBox.Context;
using ChattyBox.Models;
using ChattyBox.Hubs;
using ChattyBox.Misc;
using ChattyBox.Services;
using MaxMind.GeoIP2;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Caching;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;
using System.Net.Mime;
using Microsoft.AspNetCore.Diagnostics;

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

builder.Services.AddAuthorization();

builder.Services.ConfigureApplicationCookie(options => {
  options.Cookie.Path = "/";
  options.LoginPath = "/User/LoggedIn";
  options.LogoutPath = "/User/Logout";
  options.ExpireTimeSpan = TimeSpan.FromDays(14);
  options.SlidingExpiration = true;
  if (!builder.Environment.IsDevelopment())
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var provider = builder.Environment.ContentRootFileProvider;

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
//builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();

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


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
  app.UseSwagger();
  app.UseSwaggerUI();
  IdentityModelEventSource.ShowPII = true;
}


app.UseExceptionHandler(exceptionHandler => {
  exceptionHandler.Run(async context => {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = MediaTypeNames.Application.Json;
    var ex = context.Features.Get<IExceptionHandlerPathFeature>();
    if (ex is not null) {
      if (context.Request.Path.Value == "/User/Login") {
        await context.Response.WriteAsJsonAsync(new {
          status = 403,
          cause = "Unauthorized",
          message = "Invalid credentials"
        });
        return;
      }
      await context.Response.WriteAsJsonAsync(new {
        status = context.Response.StatusCode,
        cause = "Internal server error",
        message = ex.Error.Message
      });
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

app.MapControllers();

app.MapHub<MessagesHub>("/hub/messages");

app.Run();
