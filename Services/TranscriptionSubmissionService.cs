using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PodcastTranscribe.API.Models;
using System.Diagnostics;
using Xabe.FFmpeg;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Streams;
using System.Net.Http.Headers;

namespace PodcastTranscribe.API.Services
{
    public class TranscriptionSubmissionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class TranscriptionSubmissionService : ITranscriptionSubmissionService
    {
        private readonly ILogger<TranscriptionSubmissionService> _logger;
        private readonly CosmosDbService _cosmosDbService;
        private readonly IAzureBlobStorageService _azureBlobStorageService;
        private readonly string _debugDirectory;
        public TranscriptionSubmissionService(
            ILogger<TranscriptionSubmissionService> logger,
            CosmosDbService cosmosDbService,
            IAzureBlobStorageService azureBlobStorageService
        )
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
            _debugDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DebugAudio");
            Directory.CreateDirectory(_debugDirectory);
            _azureBlobStorageService = azureBlobStorageService;
        }

        /// <summary>
        /// Master function to handle the complete transcription submission workflow
        /// </summary>
        /// <param name="episodeId">The ID of the episode to process</param>
        /// <returns>A result containing success status and message</returns>
        public async Task<TranscriptionSubmissionResult> ProcessTranscriptionSubmissionAsync(string episodeId)
        {
            string audioFilePath = null;
            string processedAudioPath = null;

            try
            {
                _logger.LogInformation($"Starting transcription submission process for episode {episodeId}");

                // Step 1: Get episode details from CosmosDB
                var episode = await _cosmosDbService.GetEpisodeByIdAsync(episodeId);
                if (episode == null)
                {
                    var errorMessage = $"Episode {episodeId} not found in database";
                    _logger.LogError(errorMessage);
                    return new TranscriptionSubmissionResult 
                    { 
                        Success = false, 
                        Message = errorMessage 
                    };
                }

                // Step 2: Download audio file
                audioFilePath = await DownloadAudioFileAsync(episode.AudioUrl);
                if (string.IsNullOrEmpty(audioFilePath))
                {
                    var errorMessage = $"Failed to download audio for episode {episodeId}";
                    _logger.LogError(errorMessage);
                    return new TranscriptionSubmissionResult 
                    { 
                        Success = false, 
                        Message = errorMessage 
                    };
                }

                // Step 3: Process audio file (reduce bitrate)
                processedAudioPath = await ProcessAudioFileAsync(audioFilePath);
                if (string.IsNullOrEmpty(processedAudioPath))
                {
                    var errorMessage = $"Failed to process audio for episode {episodeId}";
                    _logger.LogError(errorMessage);
                    return new TranscriptionSubmissionResult 
                    { 
                        Success = false, 
                        Message = errorMessage 
                    };
                }

                // Step 4: Upload to Azure Blob Storage (to be implemented)
                // TODO: Implement blob storage upload
                var blobUrl = await UploadToBlobStorageAsync(processedAudioPath, episode.Id);
                // _logger.LogInformation($"Successfully completed transcription submission process for episode {episodeId}");
                return new TranscriptionSubmissionResult 
                { 
                    Success = true, 
                    Message = "Transcription submitted successfully. Please check back later for results." 
                };
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error processing transcription submission for episode {episodeId}: {ex.Message}";
                _logger.LogError(ex, errorMessage);
                return new TranscriptionSubmissionResult 
                { 
                    Success = false, 
                    Message = errorMessage 
                };
            }
            finally
            {
                // Cleanup temporary files
                CleanupTemporaryFiles(audioFilePath, processedAudioPath);
            }
        }

        private async Task<string> DownloadAudioFileAsync(string audioUrl)
        {
            string tempFilePath = null;
            try
            {
                // Create a temporary file path
                tempFilePath = Path.Combine(Path.GetTempPath(), $"podcast_{Guid.NewGuid()}.mp3");
                
                using (var client = new HttpClient())
                {
                    // Set timeout to 30 minutes for large files
                    client.Timeout = TimeSpan.FromMinutes(30);

                    // Set a browser-like User-Agent header to avoid 403 Forbidden
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36");

                    // Request only the first 15 MB of the file
                    var maxBytes = 30 * 1024 * 1024;
                    client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, maxBytes - 1);

                    // Get file size first
                    var response = await client.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    var fileSize = response.Content.Headers.ContentLength ?? -1;
                    
                    _logger.LogInformation($"... Starting download of {fileSize / (1024 * 1024)}MB file from {audioUrl}");
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        var totalBytesRead = 0L;
                        int bytesRead;
                        
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            
                            if (totalBytesRead % (10 * 1024 * 1024) == 0)
                            {
                                var progress = (double)totalBytesRead / fileSize * 100;
                                _logger.LogInformation($"...downloading... Download progress: {progress:F1}%");

                            if (totalBytesRead >= maxBytes)
                            {
                                _logger.LogInformation($"...Reached 15MB partial download limit; stopping download.");
                                break;
                            }
                            }
                        }
                    }
                }
                
                // Verify file exists and has content
                var fileInfo = new FileInfo(tempFilePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new Exception("Downloaded file is empty or does not exist");
                }

                // Save debug copy
                var debugFilePath = Path.Combine(_debugDirectory, $"original_demo_{Path.GetFileName(tempFilePath)}");
                File.Copy(tempFilePath, debugFilePath, true);
                _logger.LogInformation($"... Saved debug copy to: {debugFilePath}");

                _logger.LogInformation($"... Successfully downloaded {fileInfo.Length / (1024 * 1024)}MB file to {tempFilePath}");
                return tempFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to download audio file from {audioUrl}");
                if (tempFilePath != null && File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                throw;
            }
        }

        private async Task<string> ProcessAudioFileAsync(string inputFilePath)
        {
            string processedFilePath = null;
            try
            {
                // Get original file size
                var originalFileInfo = new FileInfo(inputFilePath);
                var originalSizeMB = originalFileInfo.Length / (1024.0 * 1024.0);
                _logger.LogInformation($"... Original file size: {originalSizeMB:F2}MB");

                // Create output path
                processedFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"processed_{Path.GetFileName(inputFilePath)}"
                );

                // Get media info
                // The name 'FFmpeg' does not exist in the current contextCS0103, solved by adding using Xabe.FFmpeg;
                var mediaInfo = await FFmpeg.GetMediaInfo(inputFilePath);
                var audioStream = mediaInfo.AudioStreams.First();

                // Convert audio with reduced bitrate, lower sample rate, and force mono (single-pass)
                var conversion = FFmpeg.Conversions.New()
                    .AddStream(audioStream)
                    .SetAudioBitrate(16)            // lower to 16 kbps
                    .AddParameter("-ac 1")          // force mono 
                    .AddParameter("-ar 22050")       // set sample rate to 22.05 kHz
                    .SetOutput(processedFilePath);

                // Add progress reporting
                conversion.OnProgress += (sender, args) =>
                {
                    var percent = (int)(Math.Round(args.Duration.TotalSeconds / args.TotalLength.TotalSeconds, 2) * 100);
                    _logger.LogInformation($"... Processing progress: {percent}%");
                };

                // Start conversion
                await conversion.Start();

                // Get processed file size
                var processedFileInfo = new FileInfo(processedFilePath);
                var processedSizeMB = processedFileInfo.Length / (1024.0 * 1024.0);
                var reductionPercentage = ((originalSizeMB - processedSizeMB) / originalSizeMB) * 100;
                
                _logger.LogInformation($"... Processed file size: {processedSizeMB:F2}MB");
                _logger.LogInformation($"... Size reduction: {reductionPercentage:F1}%");

                // Save debug copy
                var debugFilePath = Path.Combine(_debugDirectory, $"processed_demo_{Path.GetFileName(processedFilePath)}");
                File.Copy(processedFilePath, debugFilePath, true);
                _logger.LogInformation($"... Saved debug copy to: {debugFilePath}");

                return processedFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process audio file");
                if (processedFilePath != null && File.Exists(processedFilePath))
                {
                    File.Delete(processedFilePath);
                }
                throw;
            }
        }

        private async Task<string> UploadToBlobStorageAsync(string filePath, string episodeId){
            // reading the file as stream
            var fileStream = File.OpenRead(filePath);
            // uploading the file to blob storage, use audioUrl as the file name

            var timestamp = DateTime.Now.ToString("HH-mm-ss");
            // var blobFileName = $"{episodeId}_{timestamp}.mp3";
            var blobFileName = $"{episodeId}.mp3";
            var blobUrl = await _azureBlobStorageService.UploadFileAsync(fileStream, blobFileName);
            // returning the blob url
            return blobUrl.ToString();
            // var blobUrl = await _azureBlobStorageService.UploadFileAsync(fileStream, Path.GetFileName(filePath));
            // return blobUrl.ToString();
        }

        private void CleanupTemporaryFiles(params string[] filePaths)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        File.Delete(path);
                        _logger.LogInformation($"... Cleaned up temporary file: {path}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to delete temporary file: {path}");
                }
            }
        }
    }
}