using UAParser;
using Microsoft.Net.Http.Headers;

namespace ChattyBox.Services;

static public class ParsingService {
  static public ClientInfo ParseContext(HttpContext context) {
    var parser = Parser.GetDefault();
    var result = parser.Parse(context.Request.Headers[HeaderNames.UserAgent]);
    ArgumentNullException.ThrowIfNull(result);
    return result;
  }
}