using ChattyBox.Models;
using ChattyBox.Models.AdditionalModels;
using Humanizer;
namespace ChattyBox.Services;

public enum FileType {
  Video,
  Image,
  Audio,
  Other,
}

static public class FileService {
  static private string GetFilesDirectory(FileType fileType) {
    string fileDirectory = fileType switch {
      FileType.Image => "images",
      FileType.Audio => "audio",
      FileType.Video => "video",
      _ => "files",
    };
    return Path.Combine("static", fileDirectory);
  }

  static private Size SetImageSize(ImageSize size) {
    return size switch {
      ImageSize.Small => new Size(50),
      ImageSize.Medium => new Size(100),
      _ => new Size(150),
    };
  }
  static private string GetFilePath(string fileName, FileType fileType, string topDirectory, string lowerDirectory) {
    var filesPath = GetFilesDirectory(fileType);
    var savePath = Path.Combine(filesPath, topDirectory, lowerDirectory);
    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
    var filePath = Path.Combine(savePath, fileName);
    return filePath;
  }

  static private string GetFileName(FileType fileType, IFormFile? file = null, Uri? uri = null, bool isAvatar = false) {
    if (uri is not null && file is null) {
      var name = isAvatar ? "avatar" : Guid.NewGuid().ToString();
      return $"{name}.png";
    }
    ArgumentNullException.ThrowIfNull(file);
    if (fileType != FileType.Image) return file.FileName;
    return isAvatar ? $"avatar.{Path.GetExtension(file.FileName)}" : file.FileName;
  }
  async static public Task<string> SaveFile(IFormFile file, string chatId, string userId, FileType fileType) {
    var fileName = GetFileName(fileType, file);
    var filePath = GetFilePath(fileName, fileType, chatId, userId);
    
    using var stream = new FileStream(filePath, FileMode.Create);
    await file.CopyToAsync(stream);
    return filePath;
  }

  static public async Task<string> SaveImage(IFormFile file, User user, bool isAvatar = false) {
    var fileName = GetFileName(FileType.Image, file, isAvatar: isAvatar);
    var filePath = GetFilePath(fileName, FileType.Image, user.Id, isAvatar ? "avatar" : "images");

    using var image = await Image.LoadAsync(file.OpenReadStream());
    using var stream = new FileStream(filePath, FileMode.Create);
    await image.SaveAsync(stream, image.Metadata.DecodedImageFormat!);
    return filePath;
  }

  static public async Task<string> SaveImage(IFormFile file, User user, string chatId) {
    var fileName = GetFileName(FileType.Image, file);
    var filePath = GetFilePath(fileName, FileType.Image, chatId, user.Id);

    using var image = await Image.LoadAsync(file.OpenReadStream());
    using var stream = new FileStream(filePath, FileMode.Create);
    await image.SaveAsync(stream, image.Metadata.DecodedImageFormat!);
    return filePath;
  }

  static public void CheckFileSize(IFormFile file) {
    if (file.Length.Bytes() > 20.Megabytes()) {
      throw new InvalidOperationException($"file size {file.Length.Megabytes()} greater than 20MB");
    }
  }

  async static public Task<string> GetDefaultAvatar(User user) {
    var uri = new Uri(
      $"https://ui-avatars.com/api/?name={user.UserName}&background=random&size=150&bold=true&format=png&color=random"
    );
    var filePath = await FileService.SaveImage(uri, user, isAvatar: true);
    return filePath;
  }

  static public async Task<string> SaveImage(Uri uri, User user, bool isAvatar = false) {
    var fileName = GetFileName(FileType.Image, uri: uri, isAvatar: isAvatar);
    var filePath = GetFilePath(fileName, FileType.Image, user.Id, isAvatar ? "avatar" : "images");

    using var client = new HttpClient();
    var response = await client.GetAsync(uri);
    using var image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
    await image.SaveAsPngAsync(filePath);
    return filePath;
  }

  static public async Task<Image> GetImage(string filePath, ImageSize size) {
    var image = await Image.LoadAsync(filePath);
    var imageSize = size == ImageSize.Full ? image.Size : SetImageSize(size);
    image.Mutate(x => x.Resize(new ResizeOptions {
      Size = imageSize,
      Mode = ResizeMode.Max
    }));
    return image;
  }

  static public void DeleteFile(string filePath) {
    File.Delete(filePath);
  }
}