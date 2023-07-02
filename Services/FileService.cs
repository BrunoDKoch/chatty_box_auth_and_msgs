namespace ChattyBox.Services;

static public class FileService {
  async static public Task<string> SaveFile(IFormFile file, string chatId, string userId) {
    var fileName = file.FileName;
    var filesPath = Path.Combine("static", "files");
    var savePath = Path.Combine(filesPath, chatId, userId);
    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
    var filePath = Path.Combine(savePath, fileName);
    using var stream = new FileStream(filePath, FileMode.Create);
    await file.CopyToAsync(stream);
    return filePath;
  }
}