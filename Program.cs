using Microsoft.AspNetCore.Identity;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.EntityFrameworkCore;
using ChattyBox.Context;
using ChattyBox.Models;

var reqOrigin = "_reqOrigin";

string[] vowels = { "a", "e", "i", "o", "u" };
char[] accents = { '\u0300', '\u0301', '\u0302', '\u0303', '\u0308' };

var englishAlphabet = new List<string>();
for (char a = 'A'; a <= 'Z'; a++) {
  var charString = a.ToString();
  englishAlphabet.Add(charString);
}

for (char a = 'a'; a <= 'z'; a++) {
  var charString = a.ToString();
  englishAlphabet.Add(charString);
}

var accentedLetters = new List<string>();

foreach (var vowel in vowels) {
  foreach (var accent in accents) {
    accentedLetters.Add((vowel + accent).Normalize());
    accentedLetters.Add((vowel.ToUpper() + accent).Normalize());
  }
}

var allowedLetters = (englishAlphabet.Concat(accentedLetters)).ToList();
allowedLetters.Add("Ç");
allowedLetters.Add("ç");
allowedLetters.Add(" ");

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

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ChattyBoxContext>(options => {
  options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
});

builder.Services.AddIdentity<User, Role>(options => {
  options.User.RequireUniqueEmail = true;
  options.User.AllowedUserNameCharacters = String.Join("", allowedLetters);
  options.SignIn.RequireConfirmedEmail = false;
  options.Password.RequiredLength = 8;
  options.Lockout.MaxFailedAccessAttempts = 4;
})
  .AddEntityFrameworkStores<ChattyBoxContext>()
  .AddRoles<Role>()
  .AddTokenProvider<DataProtectorTokenProvider<User>>(TokenOptions.DefaultAuthenticatorProvider)
  .AddTokenProvider<DataProtectorTokenProvider<User>>(TokenOptions.DefaultEmailProvider);

builder.Services.AddAuthorization(options =>
  options.AddPolicy("TwoFactorEnabled", x => x.RequireClaim("amr", "mfa"))
);

builder.Services.ConfigureApplicationCookie(options => {
  options.Cookie.HttpOnly = true;
  options.Cookie.SameSite = SameSiteMode.Lax;
  options.Cookie.Path = "/";
  options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
  options.ExpireTimeSpan = TimeSpan.FromDays(14);
  options.LoginPath = "/User/Login";
  options.LogoutPath = "/User/Logout";
});

builder.Services.AddCookiePolicy(options => {
  options.Secure = CookieSecurePolicy.Always;
  options.CheckConsentNeeded = o => false;
  options.HttpOnly = HttpOnlyPolicy.Always;
  options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
