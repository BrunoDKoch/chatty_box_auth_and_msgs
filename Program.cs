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
using SixLabors.ImageSharp.Web.Providers.AWS;
using SixLabors.ImageSharp.Web.Caching;
using SixLabors.ImageSharp.Web.Caching.AWS;
using Microsoft.IdentityModel.Logging;
using System.Net.Mime;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.AspNetCore.Localization;
using ChattyBox.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using ChattyBox.Services;
using ChattyBox.Middleware;
using ChattyBox.Database;
using ChattyBox.Utils;
using Amazon.S3;
using Microsoft.AspNetCore.Rewrite;

var reqOrigin = "_reqOrigin";

var allowedLetters = ValidCharacters.GetLetters();

var builder = WebApplication.CreateBuilder(args);

var AWSConfig = builder.Configuration.GetSection("AWS");
var Endpoint = AWSConfig.GetValue<string>("Endpoint")!;
var BucketName = AWSConfig.GetValue<string>("BucketName")!;
var AccessKey = AWSConfig.GetValue<string>("AccessKey")!;
var AccessSecret = AWSConfig.GetValue<string>("AccessSecret")!;
var Region = AWSConfig.GetValue<string>("Region")!;
var redisKey = builder.Configuration.GetValue<string>("Redis")!;

if (builder.Environment.IsDevelopment()) {
  builder.Configuration.AddJsonFile("appsettings.Development.json");
} else {
  builder.Configuration.AddJsonFile("appsettings.json");
}

// Add services to the container.
builder.Services.AddCors(options => {
  options.AddPolicy(name: reqOrigin, policy => {
    policy.WithOrigins(builder.Configuration.GetValue<string>("WebsiteUrl")!, builder.Configuration.GetSection("Sentry").GetValue<string>("Dsn")!)
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
})
  .AddStackExchangeRedis(redisKey, options => {
    options.Configuration.ChannelPrefix = "ChattyBox";
    options.Configuration.AbortOnConnectFail = false;
    options.Configuration.AllowAdmin = true;
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

if (builder.Environment.IsDevelopment()) {
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
} else {
  builder.Services.AddImageSharp()
    .ClearProviders()
    .Configure<AWSS3StorageImageProviderOptions>(options => {
      options.S3Buckets.Add(new AWSS3BucketClientOptions {
        Endpoint = Endpoint,
        BucketName = BucketName,
        AccessKey = AccessKey,
        AccessSecret = AccessSecret,
        Region = Region
      });
    })
    .Configure<AWSS3StorageCacheOptions>(options => {
      options.Endpoint = Endpoint;
      options.BucketName = BucketName;
      options.AccessKey = AccessKey;
      options.AccessSecret = AccessSecret;
      options.Region = Region;
      AWSS3StorageCache.CreateIfNotExists(options, S3CannedACL.Private);
    })
    .SetCache<AWSS3StorageCache>()
    .AddProvider<AWSS3StorageImageProvider>();
}

builder.Services.Configure<WebServiceClientOptions>(builder.Configuration.GetSection("MaxMind"));
builder.Services.AddHttpClient<WebServiceClient>();
builder.Services.AddStackExchangeRedisCache(options => {
  options.Configuration = builder.Configuration.GetValue<string>("Redis");
});

builder.Services.AddLocalization();
builder.Services.AddSingleton<CachingService>();
builder.Services.AddSingleton<LoginAttemptHelper>();
builder.Services.AddSingleton<LocalizationMiddleware>();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddScoped<UserDB>();
builder.Services.AddScoped<MessagesDB>();
builder.Services.AddScoped<AdminDB>();
builder.Services.AddTransient<ErrorHandlerMiddleware>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<AmazonFileHelperService>();
builder.Services.AddSingleton<FileService>();

builder.Services.AddRateLimiter(options => {
  options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.WebHost.UseSentry();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
  app.UseSwagger();
  app.UseSwaggerUI();
  IdentityModelEventSource.ShowPII = true;
} else {
  var rewriter = new RewriteOptions()
    .AddRewrite(@"^static/images/(.*)/(.*)/(.*)", $"{BucketName}/static/images/$1/$2/$3", false);
  app.UseRewriter(rewriter);
}

app.UseHsts();

app.UseHttpsRedirection();

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

app.UseGlobalExceptionHandler();

app.UsePathBase(new PathString("/api/v1"));

app.UseCors(reqOrigin);

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.UseImageSharp();

app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "static")),
    RequestPath = "/static",
});

app.MapControllers();

app.MapHub<MessagesHub>("/hub/messages");

app.Run();
