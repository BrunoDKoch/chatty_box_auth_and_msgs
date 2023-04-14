using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using System.Security.Claims;

namespace ChattyBox.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController : ControllerBase {
  public IPasswordHasher<User> hasher = new PasswordHasher<User>();
  private readonly UserManager<User> _userManager;
  private readonly RoleManager<Role> _roleManager;
  private readonly IConfiguration _configuration;
  private readonly SignInManager<User> _signInManager;

  public UserController(
      UserManager<User> userManager,
      RoleManager<Role> roleManager,
      IConfiguration configuration,
      SignInManager<User> signInManager) {
    _userManager = userManager;
    _roleManager = roleManager;
    _configuration = configuration;
    _signInManager = signInManager;
  }

  [HttpPost("Register")]
  async public Task<IActionResult> RegisterUser([FromBody] UserInitialData data) {
    var createdUser = new UserCreate(data);
    var result = await _userManager.CreateAsync(createdUser);
    if (result.Errors.Count() > 0) return Conflict();
    foreach (var err in result.Errors) {
      Console.WriteLine(err);
    }
    // TODO: Add email confirmation logic
    // For now, we'll just send the email confirmation code to the client
    createdUser.TwoFactorEnabled = true;
    var otp = new Random().Next(100000, 999999).ToString();
    var otpClaim = new Claim("OTP", otp);
    await _userManager.AddClaimAsync(createdUser, otpClaim);
    await _signInManager.PasswordSignInAsync(createdUser, data.Password, false, false);
    return Ok(otp);
  }

  [HttpPost("Login")]
  async public Task<IActionResult> LogInUser([FromBody] LogInInfo data) {
    var user = await _userManager.FindByEmailAsync(data.Email);
    if (user == null) return Unauthorized();
    try {
      await _signInManager.PasswordSignInAsync(user, data.Password, data.Remember, user.AccessFailedCount > 4);
      await _userManager.UpdateSecurityStampAsync(user);
      return Ok(user.Email);
    } catch(Exception e) {
      Console.Error.WriteLine(e);
      return Unauthorized();
    }
  }

  [HttpHead("Logout")]
  async public Task<IActionResult> LogOut() {
    var userClaim = HttpContext.User;
    if (userClaim == null) return BadRequest();
    var user = await _userManager.GetUserAsync(userClaim);
    if (user == null) return BadRequest();
    await _signInManager.SignOutAsync();
    return Ok();
  }

  [HttpGet("Current")]
  async public Task<IActionResult> GetCurrentUser() {
    Console.WriteLine($"Is signed in: {_signInManager.IsSignedIn(HttpContext.User)}");
    if (!_signInManager.IsSignedIn(HttpContext.User)) return Unauthorized();
    var user = await _userManager.GetUserAsync(HttpContext.User);
    if (user == null) return Unauthorized();
    return Ok(new {
      email = user.Email,
      userName = user.UserName,
    });
  }
}