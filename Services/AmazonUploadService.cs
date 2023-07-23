using Amazon.S3;
using Amazon.S3.Model;
using Amazon;
using Amazon.Runtime;
using Amazon.S3.Transfer;

namespace ChattyBox.Services;

public class AmazonFileHelperService {
  private readonly string _bucketName;

  private readonly BasicAWSCredentials _credentials;
  private readonly RegionEndpoint _region;
  private readonly string _endpoint;

  public AmazonFileHelperService(IConfiguration configuration) {
    var awsConfig = configuration.GetSection("AWS");
    var accessKey = awsConfig.GetValue<string>("AccessKey")!;
    var accessSecret = awsConfig.GetValue<string>("AccessSecret")!;
    _bucketName = awsConfig.GetValue<string>("BucketName")!;
    _region = RegionEndpoint.GetBySystemName(awsConfig.GetValue<string>("Region")!);
    _endpoint = awsConfig.GetValue<string>("Endpoint")!;
    _credentials = new BasicAWSCredentials(accessKey, accessSecret);
  }

  async public Task<string> UploadFile(IFormFile file, string filePath) {
    using var client = new AmazonS3Client(_credentials, _region);
    using var stream = new MemoryStream();
    await file.CopyToAsync(stream);
    var uploadRequest = new TransferUtilityUploadRequest {
      InputStream = stream,
      Key = filePath,
      BucketName = _bucketName,
    };
    var transferUtility = new TransferUtility(client);
    await transferUtility.UploadAsync(uploadRequest);
    return $"{_endpoint}/{filePath}";
  }

  async public Task DeleteFile(string filePath) {
    using var client = new AmazonS3Client(_credentials, _region);
    var deleteObjectRequest = new DeleteObjectRequest {
      BucketName = _bucketName,
      Key = filePath,
    };
    await client.DeleteObjectAsync(deleteObjectRequest);
  }

  async public Task<GetObjectResponse> DownloadFile(string filePath) {
    using var client = new AmazonS3Client(_credentials, _region);
    var downloadRequest = new GetObjectRequest {
      BucketName = _bucketName,
      Key = filePath
    };
    var transferUtility = new TransferUtility(client);
    var response = await transferUtility.S3Client.GetObjectAsync(downloadRequest);
    return response;
  }
}