using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.EntityFrameworkCore;
using ChattyBox.Context;
using ChattyBox.Models;
using ChattyBox.Hubs;
using ChattyBox.Misc;
using MaxMind.GeoIP2;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Caching;

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
});
builder.Services.AddSignalR().AddJsonProtocol(o => {
  o.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
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
  options.Cookie.HttpOnly = true;
  options.Cookie.SameSite = SameSiteMode.Lax;
  options.Cookie.Path = "/";
  options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
  options.SlidingExpiration = true;
  options.ExpireTimeSpan = TimeSpan.FromDays(14);
  options.LoginPath = "/User/Login";
  options.LogoutPath = "/User/Logout";
});

builder.Services.AddCookiePolicy(options => {
  options.Secure = CookieSecurePolicy.Always;
  options.CheckConsentNeeded = o => false;
  options.HttpOnly = HttpOnlyPolicy.Always;
  options.MinimumSameSitePolicy = SameSiteMode.Lax;
});

var provider = builder.Environment.ContentRootFileProvider;

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

builder.Services.AddImageSharp()
  .Configure<PhysicalFileSystemProviderOptions>(options => {
    options.ProviderRootPath = ".";
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
}

app.UseHttpsRedirection();

app.UsePathBase(new PathString("/api/v1"));

app.UseCors(reqOrigin);

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.UseImageSharp();

app.UseEndpoints(endpoints => {
  endpoints.MapControllers();
  endpoints.MapHub<MessagesHub>("/hub/messages");
});

app.Run();
