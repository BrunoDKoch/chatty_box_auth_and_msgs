using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using ChattyBox.Models;

namespace ChattyBox.Services;

static public class ImageService {

  static private Size SetImageSize(ImageSize size) {
    switch (size) {
      case ImageSize.Small:
        return new Size(50);
      case ImageSize.Medium:
        return new Size(100);
      default:
        return new Size(150);
    }
  }

  static public async Task<string> SaveImage(IFormFile file, User user, IWebHostEnvironment webHostEnvironment) {
    var fileName = $"{Guid.NewGuid().ToString()}{Path.GetExtension(file.FileName)}";
    var imagesPath = Path.Combine(webHostEnvironment.ContentRootPath, "static", "images");
    var savePath = Path.Combine(imagesPath, user.UserName!, "avatar");
    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
    var filePath = Path.Combine(savePath, fileName);
    using var image = await Image.LoadAsync(file.OpenReadStream());
    using var stream = new FileStream(filePath, FileMode.Create);
    await image.SaveAsync(stream, image.Metadata.DecodedImageFormat!);
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
}