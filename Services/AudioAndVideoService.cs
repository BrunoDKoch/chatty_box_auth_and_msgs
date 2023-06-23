namespace ChattyBox.Services;

static public class AudioAndVideoService {
  async static public Task<string> SaveFile(IFormFile file, string chatId, string userId) {
    var name = Guid.NewGuid().ToString();
    var fileName = $"{name}{Path.GetExtension(file.FileName)}";
    var filesPath = file.ContentType.StartsWith("audio") ? Path.Combine("static", "audio") : Path.Combine("static", "video");
    var savePath = Path.Combine(filesPath, chatId, userId);
    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
    var filePath = Path.Combine(savePath, fileName);
    using var stream = new FileStream(filePath, FileMode.Create);
    await file.CopyToAsync(stream);
    return filePath;
  }
}