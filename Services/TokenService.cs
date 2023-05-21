using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;

namespace ChattyBox.Services;

static public class TokenService {
  static public JwtSecurityToken DecodeToken(string token) {
    var jwt = new JwtSecurityToken(token);
    return jwt;
  }
  static public string EncodeToken(JwtSecurityToken jwt) {
    var token = $"{jwt.EncodedHeader}.{jwt.EncodedPayload}.{jwt.RawEncryptedKey}";
    return token;
  }
}