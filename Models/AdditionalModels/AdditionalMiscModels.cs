namespace ChattyBox.Models.AdditionalModels;

public enum ImageSize {
  Small,
  Medium,
  Large,
  Full
}

public enum ExceptionActionType {
  MESSAGE,
  OTHER
}

public class FileDeletionRequest {
  public string FilePath { get; set; } = null!;
}