using Microsoft.AspNetCore.Identity;
using Humanizer;

namespace ChattyBox.Models.AdditionalModels;

public class UserInitialData {
  public string Email { get; set; } = null!;
  public string UserName { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class UserCreate : User {
  private readonly PasswordHasher<User> passwordHasher = new();
  public UserCreate(UserInitialData data) {
    Email = data.Email;
    UserName = data.UserName;
    PasswordHash = passwordHasher.HashPassword(this, data.Password);
    UserNotificationSetting = new UserNotificationSetting {
      PlaySound = true,
      ShowOSNotification = true,
    };
  }
}

public class LogInInfo {
  public string Email { get; set; } = null!;
  public string Password { get; set; } = null!;
  public bool Remember { get; set; } = false;
  public string? MFACode { get; set; }
  public bool RememberMultiFactor { get; set; } = false;
}

public class EmailValidationRequest {
  public string Email { get; set; } = null!;
  public string Code { get; set; } = null!;
}

public class LocationValidationRequest : EmailValidationRequest {
}

public class ChangeEmailRequest {
  public string CurrentEmail { get; set; } = null!;
  public string NewEmail { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class ChangePasswordRequest {
  public string CurrentPassword { get; set; } = null!;
  public string NewPassword { get; set; } = null!;
}

public class LoginAttemptResult {
  public UserLoginAttempt LoginAttempt { get; set; } = null!;
  public string FailureReason { get; set; } = null!;
  public bool SuspiciousLocation;
}

public class LockoutInfo {
  public bool Lockout { get; set; }
  public string LockoutReason { get; set; } = null!;
  public DateTime? LockoutEnd { get; set; }
  public bool Permanent { get; set; }
}

public class LoginAttemptPartial {
  public string Id;
  public DateTime AttemptedAt;
  public string CityName;
  public string CountryIsoCode;
  public string OS;
  public string Browser;
  public string Device;
  public bool Success;
  public LoginAttemptPartial(UserLoginAttempt loginAttempt) {
    Id = loginAttempt.Id;
    AttemptedAt = loginAttempt.AttemptedAt;
    CityName = loginAttempt.CityName;
    CountryIsoCode = loginAttempt.CountryIsoCode;
    OS = string.Join(' ', loginAttempt.OS.Split(' ').Distinct()).Humanize();
    Browser = string.Join(' ', loginAttempt.Browser.Split(' ').Distinct());
    Device = string.Join(' ', loginAttempt.Device.Split(' ').Distinct());
    Success = loginAttempt.Success;
  }
}

public class LoginAttemptsResponse {
  public List<LoginAttemptPartial> UserLoginAttempts = new();
  public int Count { get; set; }
}

public class PasswordRecoveryTokenRequest {
  public string Email { get; set; } = null!;
}

public class PasswordResetRequest : PasswordRecoveryTokenRequest {
  public string Token { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class MFADisableRequest {
  public string Password { get; set; } = null!;
}