using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Configuration;

namespace PodcastTranscribe.API.Services;

public interface IAzureBlobStorageService
{
    Task<bool> InitializeAsync();
    Task<Uri> UploadFileAsync(Stream fileStream, string fileName);
    Task<Stream> DownloadFileAsync(string fileName);
    Task DeleteFileAsync(string fileName);
    Task<bool> FileExistsAsync(string fileName);
    Task<string> GetFileUrlAsync(string fileName);
}

public class AzureBlobStorageService : IAzureBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private BlobContainerClient? _containerClient;
    private readonly ILogger<AzureBlobStorageService> _logger;
    public AzureBlobStorageService(
        IOptions<AzureBlobStorageSettings> blobSettings,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _blobServiceClient = new BlobServiceClient(blobSettings.Value.ConnectionString);
        _containerName = blobSettings.Value.ContainerName;

    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            // Get container client
            _containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            // Create container if it doesn't exist
            await _containerClient.CreateIfNotExistsAsync();

            // Set public access level
            await _containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);

            _logger.LogInformation("*** Azure Blob Storage initialized");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize blob storage: {ex.Message}");
            return false;
        }
    }

    public async Task<Uri> UploadFileAsync(Stream fileStream, string fileName)
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Blob storage not initialized");

        var blobClient = _containerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(fileStream, true);
        _logger.LogInformation($"*** Uploaded file to blob storage: {fileName}");
        _logger.LogInformation($"*** Blob URL: {blobClient.Uri}");
        return blobClient.Uri;
    }

    public async Task<Stream> downloadFileUrlAsync(string url)
    {
        var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(url);
        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<Stream> DownloadFileAsync(string fileName)
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Blob storage not initialized");

        var blobClient = _containerClient.GetBlobClient(fileName);
        var response = await blobClient.DownloadAsync();
        _logger.LogInformation($"*** Downloaded file from blob storage: {fileName}");
        return response.Value.Content;
    }

    public async Task<string> GetFileUrlAsync(string fileName)
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Blob storage not initialized");

        var blobClient = _containerClient.GetBlobClient(fileName);
        return blobClient.Uri.ToString();
    }

    public async Task<bool> FileExistsAsync(string fileName)
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Blob storage not initialized");

        var blobClient = _containerClient.GetBlobClient(fileName);
        return await blobClient.ExistsAsync();
    }

    public async Task DeleteFileAsync(string fileName)
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Blob storage not initialized");

        var blobClient = _containerClient.GetBlobClient(fileName);
        await blobClient.DeleteIfExistsAsync();
    }
}