using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using ChattyBox.Models;

namespace ChattyBox.Services;

static public class TokenService {
  private static JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
  static public JwtSecurityToken DecodeToken(string token) {
    var jwt = handler.ReadJwtToken(token);
    return jwt;
  }
  static public string EncodeToken(JwtSecurityToken jwt) {
    return handler.WriteToken(jwt);
  }
  async static public Task<JwtSecurityToken> CreateAccessToken(
    User user,
    UserManager<User> _userManager,
    IConfiguration _configuration
  ) {
    await _userManager.UpdateSecurityStampAsync(user);
    var jwtSection = _configuration.GetSection("JsonWebToken")!;
    var key = jwtSection.GetValue<string>("Key")!;
    var issuer = jwtSection.GetValue<string>("Issuer")!;
    var audience = jwtSection.GetValue<string>("Audience")!;
    var claims = new List<Claim>();
    claims.Add(new Claim(JwtRegisteredClaimNames.GivenName, user.UserName!));
    claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email!));
    claims.Add(new Claim(JwtRegisteredClaimNames.NameId, user.Id));
    claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
    claims.Add(new Claim("securityStamp", user.SecurityStamp!));
    var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(5));
    return token;
  }
  async static public Task<string> GetCurrentToken(
    HttpContext httpContext,
    UserManager<User> _userManager,
    IConfiguration _configuration
  ) {
    // Get the token
    var bearer = httpContext.Request.Headers.Authorization.ToString();
    ArgumentNullException.ThrowIfNullOrEmpty(bearer);
    var jwt = bearer.Replace("Bearer ", "");
    var decodedJwt = DecodeToken(jwt);

    // Get user from id and check if user and expiry are ok
    var id = decodedJwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.NameId);
    var user = await _userManager.FindByIdAsync(id.Value);
    ArgumentNullException.ThrowIfNull(user);
    ArgumentNullException.ThrowIfNull(decodedJwt.Payload.Exp);

    // Check token expiry and security stamp expiry
    var userClaims = await _userManager.GetClaimsAsync(user);
    var expiry = DateTimeOffset.FromUnixTimeMilliseconds((int)decodedJwt.Payload.Exp);
    var stampExpiry = DateTime.Parse(userClaims.First(c => c.Type == "stampExpiry").Value);

    // Throw error if stamp is expired
    if (stampExpiry < DateTime.UtcNow) throw new InvalidOperationException();

    // If the token itself is expired but stamp is fine, send new token
    string token;
    if (expiry < DateTime.UtcNow)
      token = EncodeToken(await CreateAccessToken(user, _userManager, _configuration));
    else token = jwt;
    return token;
  }

  async static public Task<TokenValidationResult> ValidadeToken(string token, TokenValidationParameters validationParameters) {
    var result = await handler.ValidateTokenAsync(token, validationParameters);
    return result;
  }
}