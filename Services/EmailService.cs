using Microsoft.Extensions.Localization;
using HtmlAgilityPack;
using System.Text;
using ChattyBox.Models;
using Microsoft.AspNetCore.Identity;
using System.Net.Mail;
using System.Net;

namespace ChattyBox.Services;

public enum EmailType {
  EmailConfirmation,
  LocationConfirmation,
  PasswordResetConfirmation,
  MFADisabledWarning,
  EmailChangedWarning,
  PasswordChangedWarning,
}

public class EmailData {
  public string Host { get; set; } = null!;
  public int Port { get; set; }
  public string MainAddress { get; set; } = null!;
  public NetworkCredential Credential { get; set; } = null!;
  public EmailData(IConfiguration configuration) {
    var host = configuration.GetValue<string>("Email:Host");
    var port = configuration.GetValue<int>("Email:Port");
    var mainAddress = configuration.GetValue<string>("Email:MainAddress");
    var password = configuration.GetValue<string>("Email:Password");
    ArgumentException.ThrowIfNullOrEmpty(host);
    ArgumentNullException.ThrowIfNull(port);
    ArgumentException.ThrowIfNullOrEmpty(mainAddress);
    ArgumentException.ThrowIfNullOrEmpty(password);
    Host = host;
    Port = port;
    MainAddress = mainAddress;
    Credential = new NetworkCredential(mainAddress, password);
  }
}

public class EmailService {
  private string _basePath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "EmailTemplateFiles");
  private readonly IConfiguration _configuration;
  private readonly IStringLocalizer<EmailService> _localizer;
  public EmailService(
    IConfiguration configuration,
    IStringLocalizer<EmailService> localizer
  ) {
    _configuration = configuration;
    _localizer = localizer;
  }

  private EmailData GetEmailData() {
    return new EmailData(_configuration);
  }

  private HtmlDocument ConvertToHtmlDocument(string htmlString) {
    HtmlDocument document = new();
    document.LoadHtml(htmlString);
    return document;
  }

  private string GetTitle(EmailType emailType) {
    switch (emailType) {
      case EmailType.EmailConfirmation:
        return _localizer.GetString("welcome");
      case EmailType.MFADisabledWarning:
        return _localizer.GetString("mfaWarning");
      default:
        return _localizer.GetString("actionRequired");
    }
  }
  private string ReplaceWithLocalizedText(string originalText, string replacementTitle, string replacementParagraph) {
    originalText = originalText.Replace("TitleText", _localizer.GetString(replacementTitle));
    originalText = originalText.Replace("MainText", _localizer.GetString(replacementParagraph));
    return originalText;
  }

  async private Task<string> GetAdditionalElement(string toAppend, bool isLink = false, string itemAndToken = "") {
    var additionalElement = await File.ReadAllTextAsync(Path.Combine(_basePath, isLink ? "ButtonRowTemplate.html" : "CodeTextTemplate.html"), Encoding.UTF8);
    additionalElement = additionalElement.Replace(isLink ? "BtnText" : "CodeText", toAppend);
    if (!string.IsNullOrEmpty(itemAndToken))
      additionalElement = additionalElement.Replace("link", $"{_configuration.GetValue<string>("JsonWebToken:Audience")}/recovery/{itemAndToken}");
    return additionalElement;
  }

  async private Task<string> ModifyTemplate(string mainTemplate, string toAppend, bool isLink = false, string itemAndToken = "") {
    var htmlDocument = ConvertToHtmlDocument(mainTemplate);
    var tableNode = htmlDocument.DocumentNode.SelectNodes("//table").FirstOrDefault();
    ArgumentNullException.ThrowIfNull(tableNode);
    var additionalElement = await GetAdditionalElement(toAppend, isLink);
    tableNode.AppendChild(HtmlNode.CreateNode(additionalElement));
    var htmlString = htmlDocument.DocumentNode.OuterHtml;
    return htmlString;
  }

  async private Task<string> GetEmailBody(EmailType emailType, string otpCode = "", string itemAndToken = "") {
    string htmlString;
    var mainTemplate = await File.ReadAllTextAsync(
      Path.Combine(_basePath, "EmailTemplate.html"), Encoding.UTF8
    );
    switch (emailType) {
      case EmailType.EmailConfirmation:
        mainTemplate = ReplaceWithLocalizedText(mainTemplate, "welcome", "confirmEmail");
        htmlString = await ModifyTemplate(mainTemplate, otpCode);
        break;
      case EmailType.LocationConfirmation:
        mainTemplate = ReplaceWithLocalizedText(mainTemplate, "actionRequired", "confirmLocation");
        htmlString = await ModifyTemplate(mainTemplate, otpCode);
        break;
      case EmailType.PasswordResetConfirmation:
        mainTemplate = ReplaceWithLocalizedText(mainTemplate, "actionRequired", "changePassword");
        htmlString = await ModifyTemplate(mainTemplate, "actionRequired", isLink: true, itemAndToken);
        break;
      case EmailType.EmailChangedWarning:
        mainTemplate = ReplaceWithLocalizedText(mainTemplate, "actionRequired", "emailChanged");
        htmlString = await ModifyTemplate(mainTemplate, "emailChanged", isLink: true, itemAndToken);
        break;
      default:
        mainTemplate = ReplaceWithLocalizedText(mainTemplate, "mfaWarning", "mfaParagraph");
        htmlString = mainTemplate;
        break;
    }
    return htmlString;
  }

  async public Task SendEmail(string emailAddress, EmailType emailType, string otpCode = "", string itemAndToken = "") {
    var emailData = GetEmailData();
    using var client = new SmtpClient();
    client.EnableSsl = true;
    client.Host = emailData.Host;
    client.Port = emailData.Port;
    client.Credentials = emailData.Credential;
    using var message = new MailMessage(
      from: emailData.MainAddress,
      to: emailAddress
    );
    message.Subject = GetTitle(emailType);
    message.Body = string.IsNullOrEmpty(itemAndToken) ? await GetEmailBody(emailType, otpCode) : await GetEmailBody(emailType, itemAndToken: itemAndToken);
    message.IsBodyHtml = true;
    await client.SendMailAsync(message);
  }
}