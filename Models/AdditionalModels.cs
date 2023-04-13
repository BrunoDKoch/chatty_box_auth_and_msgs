using Microsoft.AspNetCore.Identity;

namespace ChattyBox.Models;

public class UserInitialData {
  public string Email { get; set; } = null!;
  public string UserName { get; set; } = null!;
  public string Password { get; set; } = null!;
}

public class UserCreate : User {
  private PasswordHasher<User> passwordHasher = new PasswordHasher<User>();
  public UserCreate(UserInitialData data) {
    Email = data.Email;
    UserName = data.UserName;
    PasswordHash = passwordHasher.HashPassword(this, data.Password);
  }
}

public class LogInInfo {
  public string Email { get; set; } = null!;
  public string Password { get; set; } = null!;
  public bool Remember { get; set; } = false;
}

public class EmailValidationRequest {
  public string Code { get; set; } = null!;
}