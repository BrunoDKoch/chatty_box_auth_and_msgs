using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using ChattyBox.Models;
using ChattyBox.Services;
using ChattyBox.Database;
using ChattyBox.Models.AdditionalModels;
using ChattyBox.Hubs;
using ChattyBox.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Security.Claims;

namespace ChattyBox.Controllers;

[ApiController]
[AllowAnonymous]
[Route("[controller]")]
public class LegalController : ControllerBase {
  private readonly FileService _fileService;

  public LegalController(FileService fileService) {
    _fileService = fileService;
  }
  [HttpGet("{lang}/{fileName}")]
  async public Task<ActionResult<string>> GetLegalDoc(string lang, string fileName) {
    var result = await _fileService.GetFile(lang, fileName);
    return Ok(result);
  }
}