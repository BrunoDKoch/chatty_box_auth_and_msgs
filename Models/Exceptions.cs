using System.Net;

namespace ChattyBox.Models;

public class CustomException : Exception {
  public string ErrorMessage { get; }

  public HttpStatusCode Status { get; }

  public CustomException(string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
      : base(message) {
    ErrorMessage = message;
    Status = statusCode;
  }
}

public class InvalidCredentialsException : CustomException {
  public InvalidCredentialsException(string message) : base(message, HttpStatusCode.Unauthorized) { }
}

public class ConflictException : CustomException {
  public ConflictException(string message) : base(message, HttpStatusCode.Conflict) { }
}

public class SuspiciousLocationException : CustomException {
  public SuspiciousLocationException(string message) : base(message, HttpStatusCode.Forbidden) { }
}

public class MFACodeRequired : CustomException {
  public MFACodeRequired(string message) : base(message, HttpStatusCode.BadRequest) { }
}

public class EmailConfirmationException : CustomException {
  public EmailConfirmationException() : base("emailNotConfirmed", HttpStatusCode.BadRequest) { }
}

public class ForbiddenException : CustomException {
  public ForbiddenException(string message) : base(message, HttpStatusCode.Forbidden) { }
}