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
using System.Text.Json;
using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Configuration;
using System.Net.Http;
using System.Text;

namespace PodcastTranscribe.API.Services
{
    /// <summary>
    /// Represents the result of a transcription submission.
    /// </summary>
    public class TranscriptionSubmissionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Handles the workflow for submitting audio for transcription.
    /// </summary>
    public class TranscriptionSubmissionService : ITranscriptionSubmissionService
    {
        private readonly ILogger<TranscriptionSubmissionService> _logger;
        private readonly CosmosDbService _cosmosDbService;
        private readonly IAzureBlobStorageService _azureBlobStorageService;
        private readonly string _debugDirectory;
        private readonly AzureSpeechSettings _azureSpeechSettings;
        private readonly HttpClient _httpClient;
        private readonly IAzureSpeechHandlerService _azureSpeechHandlerService;
        // Use named constants for magic numbers
        private const int MaxDownloadBytes = 10 * 1024 * 1024; // 10 MB
        private const int DownloadBufferSize = 8192;
        private const int AudioBitrateKbps = 16;
        private const int AudioSampleRate = 22050;

        public TranscriptionSubmissionService(
            ILogger<TranscriptionSubmissionService> logger,
            CosmosDbService cosmosDbService,
            IAzureBlobStorageService azureBlobStorageService,
            IOptions<AzureSpeechSettings> azureSpeechSettings,
            IAzureSpeechHandlerService azureSpeechHandlerService
        )
        {
            _logger = logger;
            _cosmosDbService = cosmosDbService;
            _debugDirectory = Path.Combine(Directory.GetCurrentDirectory(), "DebugAudio");
            Directory.CreateDirectory(_debugDirectory);
            _azureBlobStorageService = azureBlobStorageService;
            _azureSpeechSettings = azureSpeechSettings.Value;
            _httpClient = new HttpClient();
            _azureSpeechHandlerService = azureSpeechHandlerService;
        }

        /// <summary>
        /// Master function to handle the complete transcription submission workflow.
        /// </summary>
        /// <param name="audioUrl">The ID of the episode to process</param>
        /// <returns>A result containing success status and message</returns>
        public async Task<(bool isSuccess, string message)> ProcessTranscriptionSubmissionAsync(Episode episode)
        {
            string audioFilePath = null;
            string processedAudioPath = null;
            string blobUrl = null;

            try
            {
                await _cosmosDbService.UpdateEpisodeEntryAsync(episode.Id, transcriptionStatus: TranscriptionStatus.Processing);
                _logger.LogInformation($"Starting transcription submission for episode {episode.Id}");

                // Check if audio file already exists in blob storage
                var blobFileExists = await _azureBlobStorageService.FileExistsAsync(generateAudioFileName(episode.Id));

                if (blobFileExists)
                {
                    _logger.LogInformation($"Audio file already exists in blob storage for episode {episode.Id}");
                    blobUrl = await _azureBlobStorageService.GetFileUrlAsync(generateAudioFileName(episode.Id));
                }
                else
                {
                    _logger.LogInformation($"Audio file does not exist in blob storage for episode {episode.Id}");
                    // Step 1: Download audio file
                    audioFilePath = await DownloadAudioFileAsync(episode.AudioUrl);
                    if (string.IsNullOrEmpty(audioFilePath))
                    {
                        var errorMessage = $"Failed to download audio for episode {episode.Id}";
                        _logger.LogError(errorMessage);
                        return (false, errorMessage);
                    }

                    // Step 2: Process audio file (reduce bitrate)
                    processedAudioPath = await ProcessAudioFileAsync(audioFilePath);
                    if (string.IsNullOrEmpty(processedAudioPath))
                    {
                        var errorMessage = $"Failed to process audio for episode {episode.Id}";
                        _logger.LogError(errorMessage);
                        return (false, errorMessage);
                    }

                    // Step 3: Upload audio to Azure Blob Storage
                    blobUrl = await UploadToBlobStorageAsync(processedAudioPath, episode.Id);
                }

                // Step 4: Submit transcription to Azure Speech Service
                var transcriptionUrl = await _azureSpeechHandlerService.SubmitTranscriptionToAzureAsync(blobUrl, episode.Id);

                return (true, "Transcription task submitted");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error processing transcription submission for episode {episode.Id}: {ex.Message}";
                _logger.LogError(ex, errorMessage);
                // Update episode status to Failed if an error occurs
                await _cosmosDbService.UpdateEpisodeEntryAsync(episode.Id, transcriptionStatus: TranscriptionStatus.Failed);
                return (false, errorMessage);
            }
            finally
            {
                // Cleanup temporary files
                CleanupTemporaryFiles(audioFilePath, processedAudioPath);
            }
        }

        /// <summary>
        /// Downloads an audio file from a URL, saving only the first 10MB for processing.
        /// </summary>
        private async Task<string> DownloadAudioFileAsync(string audioUrl)
        {
            string tempFilePath = null;
            try
            {
                // Create a temporary file path
                tempFilePath = Path.Combine(Path.GetTempPath(), $"podcast_{Guid.NewGuid()}.mp3");
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(30);
                    // Set a browser-like User-Agent header to avoid 403 Forbidden
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36");
                    // Request only the first 10 MB of the file
                    client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, MaxDownloadBytes - 1);
                    var response = await client.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var fileSize = response.Content.Headers.ContentLength ?? -1;
                    _logger.LogInformation($"Starting download of {fileSize / (1024 * 1024)}MB file from {audioUrl}");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[DownloadBufferSize];
                        var totalBytesRead = 0L;
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                            totalBytesRead += bytesRead;
                            // Log progress every 1MB
                            if (totalBytesRead % (1 * 1024 * 1024) < DownloadBufferSize)
                            {
                                var progress = fileSize > 0 ? (double)totalBytesRead / fileSize * 100 : 0;
                                _logger.LogInformation($"Download progress: {progress:F1}%");
                            }
                            if (totalBytesRead >= MaxDownloadBytes)
                            {
                                _logger.LogInformation($"Reached 10MB partial download limit; stopping download.");
                                break;
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
                _logger.LogInformation($"Saved debug copy to: {debugFilePath}");
                _logger.LogInformation($"Successfully downloaded {fileInfo.Length / (1024 * 1024)}MB file to {tempFilePath}");
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

        /// <summary>
        /// Processes the audio file to reduce bitrate and sample rate for efficient transcription.
        /// </summary>
        private async Task<string> ProcessAudioFileAsync(string inputFilePath)
        {
            string processedFilePath = null;
            try
            {
                var originalFileInfo = new FileInfo(inputFilePath);
                var originalSizeMB = originalFileInfo.Length / (1024.0 * 1024.0);
                _logger.LogInformation($"Original file size: {originalSizeMB:F2}MB");
                processedFilePath = Path.Combine(Path.GetTempPath(), $"processed_{Path.GetFileName(inputFilePath)}");
                var mediaInfo = await FFmpeg.GetMediaInfo(inputFilePath).ConfigureAwait(false);
                var audioStream = mediaInfo.AudioStreams.FirstOrDefault();
                if (audioStream == null)
                {
                    throw new Exception("No audio stream found in file");
                }
                // Convert audio with reduced bitrate, lower sample rate, and force mono
                var conversion = FFmpeg.Conversions.New()
                    .AddStream(audioStream)
                    .SetAudioBitrate(AudioBitrateKbps)
                    .AddParameter("-ac 1")
                    .AddParameter($"-ar {AudioSampleRate}")
                    .SetOutput(processedFilePath);
                // Add progress reporting
                conversion.OnProgress += (sender, args) =>
                {
                    var percent = args.TotalLength.TotalSeconds > 0 ? (int)(args.Duration.TotalSeconds / args.TotalLength.TotalSeconds * 100) : 0;
                    _logger.LogInformation($"Processing progress: {percent}%");
                };
                await conversion.Start().ConfigureAwait(false);
                var processedFileInfo = new FileInfo(processedFilePath);
                var processedSizeMB = processedFileInfo.Length / (1024.0 * 1024.0);
                var reductionPercentage = ((originalSizeMB - processedSizeMB) / originalSizeMB) * 100;
                _logger.LogInformation($"Processed file size: {processedSizeMB:F2}MB");
                _logger.LogInformation($"Size reduction: {reductionPercentage:F1}%");
                // Save debug copy
                var debugFilePath = Path.Combine(_debugDirectory, $"processed_demo_{Path.GetFileName(processedFilePath)}");
                File.Copy(processedFilePath, debugFilePath, true);
                _logger.LogInformation($"Saved debug copy to: {debugFilePath}");
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

        /// <summary>
        /// Uploads the processed audio file to Azure Blob Storage.
        /// </summary>
        private async Task<string> UploadToBlobStorageAsync(string filePath, string episodeId)
        {
            // Open the file as a stream and upload
            using (var fileStream = File.OpenRead(filePath))
            {
                var blobFileName = generateAudioFileName(episodeId);
                var blobUrl = await _azureBlobStorageService.UploadFileAsync(fileStream, blobFileName).ConfigureAwait(false);
                await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, processedAudioBlobUri: blobUrl.ToString());
                return blobUrl.ToString();
            }
        }

        /// <summary>
        /// Deletes temporary files used during processing.
        /// </summary>
        private void CleanupTemporaryFiles(params string[] filePaths)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        File.Delete(path);
                        _logger.LogInformation($"Cleaned up temporary file: {path}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to delete temporary file: {path}");
                }
            }
        }

        /// <summary>
        /// Generates a consistent audio file name for blob storage.
        /// </summary>
        private string generateAudioFileName(string episodeId)
        {
            return $"{episodeId}_audio.mp3";
        }
    }
}