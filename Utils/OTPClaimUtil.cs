using System.Security.Claims;

namespace ChattyBox.Utils;

static public class OTPClaimUtil {
  static public Claim CreateOTPClaim(string claimName) {
    var otp = new Random().Next(100000, 999999).ToString();
    var otpClaim = new Claim(claimName, otp);
    return otpClaim;
  }
}