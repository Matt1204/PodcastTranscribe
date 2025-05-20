using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Configuration;
using System.Text;
using System.Text.Json;
using PodcastTranscribe.API.Models;
namespace PodcastTranscribe.API.Services
{
    /// <summary>
    /// Handles Azure Speech Service transcription submission and result retrieval.
    /// </summary>
    public class AzureSpeechHandlerService : IAzureSpeechHandlerService
    {
        private readonly ILogger<AzureSpeechHandlerService> _logger;
        private readonly IAzureBlobStorageService _azureBlobStorageService;
        private readonly string _azureSpeechKey;
        private readonly string _azureSpeechRegion;
        private readonly string _azureSpeechApiVersion;
        private readonly HttpClient _httpClient;
        private readonly CosmosDbService _cosmosDbService;

        public AzureSpeechHandlerService(
            ILogger<AzureSpeechHandlerService> logger,
            IAzureBlobStorageService azureBlobStorageService,
            IOptions<AzureSpeechSettings> azureSpeechSettings,
            CosmosDbService cosmosDbService)
        {
            _logger = logger;
            _azureBlobStorageService = azureBlobStorageService;
            _azureSpeechKey = azureSpeechSettings.Value.SubscriptionKey;
            _azureSpeechRegion = azureSpeechSettings.Value.Region;
            _azureSpeechApiVersion = azureSpeechSettings.Value.ApiVersion;
            _cosmosDbService = cosmosDbService;
            _httpClient = new HttpClient();
            // Add subscription key header once
            if (!_httpClient.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"))
            {
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _azureSpeechKey);
            }
        }

        /// <summary>
        /// Submits an audio file (by URL) to Azure Speech Service for transcription.
        /// </summary>
        public async Task<string> SubmitTranscriptionToAzureAsync(string blobAudioURL, string episodeId)
        {
            try
            {
                var requestUrl = $"https://{_azureSpeechRegion}.api.cognitive.microsoft.com/speechtotext/{_azureSpeechApiVersion}/transcriptions";
                _logger.LogInformation($"Submitting transcription for episode {episodeId} to {requestUrl}");
                var requestBody = new
                {
                    displayName = $"{episodeId}-transcription",
                    locale = "en-US",
                    contentUrls = new[] { blobAudioURL },
                    properties = new
                    {
                        wordLevelTimestampsEnabled = false,
                        languageIdentification = new
                        {
                            candidateLocales = new[] { "zh-CN", "en-US" }
                        }
                    }
                };
                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(requestUrl, content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = response.StatusCode;
                    var errorMessage = $"Failed to submit transcription: {response.ReasonPhrase}, status code: {statusCode}";
                    _logger.LogError(errorMessage);
                    throw new Exception(errorMessage);
                }
                var resJsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var resBody = JsonSerializer.Deserialize<JsonElement>(resJsonStr);
                var selfUrl = resBody.GetProperty("self").GetString();
                await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionSubmitted, azureSpeechUri: selfUrl);
                _logger.LogInformation($"Transcription request submitted. Self URL: {selfUrl}");
                return selfUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to submit transcription for episode {episodeId}");
                throw;
            }
        }

        /// <summary>
        /// Syncs the transcription status from Azure Speech Service and updates the episode in Cosmos DB.
        /// </summary>
        public async Task<string> syncTranscriptionStatusAsync(string episodeId)
        {
            var episode = await _cosmosDbService.GetEpisodeByIdAsync(episodeId);
            if (episode == null)
                throw new Exception($"Episode {episodeId} not found");
            if (string.IsNullOrEmpty(episode.AzureSpeechURI))
                throw new Exception($"Episode {episodeId} has no Azure Speech URI");
            string requestUrl = episode.AzureSpeechURI;
            try
            {
                var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"Failed to get transcription status. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                    _logger.LogError(errorMsg);
                    throw new Exception(errorMsg);
                }
                var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
                // Get the transcription status from the response
                if (jsonDoc.TryGetProperty("status", out JsonElement statusElement))
                {
                    var status = statusElement.GetString();
                    _logger.LogInformation($"Transcription status for URI {episode.AzureSpeechURI}: {status}");
                    switch (status)
                    {
                        case "Running":
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionRunning);
                            break;
                        case "Succeeded":
                            await this.UpdateTranscriptionResultAsync(episode, jsonDoc.GetProperty("self").GetString());
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionSucceeded);
                            break;
                        case "Failed":
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.Failed);
                            break;
                        case "NotStarted":
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.NotStarted);
                            break;
                        default:
                            _logger.LogError($"Unknown transcription status: {status} for URI: {episode.AzureSpeechURI}");
                            break;
                    }
                    return status;
                }
                else
                {
                    throw new Exception("Response JSON does not contain 'status' field.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving transcription status for URI: {episode.AzureSpeechURI}");
                throw;
            }
        }

        /// <summary>
        /// Updates the transcription result in Cosmos DB after successful transcription.
        /// </summary>
        public async Task<bool> UpdateTranscriptionResultAsync(Episode episode, string azureSpeechUri)
        {
            if (episode == null || string.IsNullOrEmpty(azureSpeechUri))
                throw new ArgumentNullException("Episode or Azure Speech URI cannot be null");
            string transcriptionResultUrl = $"{azureSpeechUri}/files";
            var response = await _httpClient.GetAsync(transcriptionResultUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"Failed to get transcription result. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                _logger.LogError(errorMsg);
                throw new Exception(errorMsg);
            }
            var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
            if (jsonDoc.TryGetProperty("values", out JsonElement valuesEntry))
            {
                foreach (var value in valuesEntry.EnumerateArray())
                {
                    if (value.TryGetProperty("kind", out JsonElement kindEntry) && kindEntry.GetString() == "Transcription")
                    {
                        if (value.TryGetProperty("links", out JsonElement linksEntry) && linksEntry.TryGetProperty("contentUrl", out JsonElement contentUrlEntry))
                        {
                            var textContentUrl = contentUrlEntry.GetString();
                            string displayContent = await this.GetTranscriptionContentAsync(textContentUrl);
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId: episode.Id, transcriptionResultDisplay: displayContent);
                            return true;
                        }
                        else
                        {
                            throw new Exception("Response JSON does not contain 'contentUrl' field.");
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Retrieves the transcription content from Azure Speech Service.
        /// </summary>
        private async Task<string> GetTranscriptionContentAsync(string textContentUrl)
        {
            var response = await _httpClient.GetAsync(textContentUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"Failed to get transcription content. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                _logger.LogError(errorMsg);
                throw new Exception(errorMsg);
            }
            var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
            // Get the first display text from combinedRecognizedPhrases
            if (jsonDoc.TryGetProperty("combinedRecognizedPhrases", out JsonElement combinedRecognizedPhrasesEntry))
            {
                var firstPhrase = combinedRecognizedPhrasesEntry.EnumerateArray().FirstOrDefault();
                if (firstPhrase.ValueKind != JsonValueKind.Undefined && firstPhrase.TryGetProperty("display", out JsonElement displayText))
                {
                    return displayText.GetString();
                }
            }
            return string.Empty;
        }
    }
}