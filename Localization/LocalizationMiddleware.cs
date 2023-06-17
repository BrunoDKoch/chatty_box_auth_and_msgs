using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Globalization;

namespace ChattyBox.Localization;

public class LocalizationMiddleware : IMiddleware {
  public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
    var cultureKey = context.Request.Cookies["lang"];
    if (!string.IsNullOrEmpty(cultureKey)) {
      if (DoesCultureExist(cultureKey)) {
        var culture = new CultureInfo(cultureKey);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
      }
    }
    await next(context);
  }
  private static bool DoesCultureExist(string cultureName) {
    return CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Any(culture => string.Equals(culture.Name, cultureName,
      StringComparison.CurrentCultureIgnoreCase));
  }
}