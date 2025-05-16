using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Configuration;
using System.Text;
using System.Text.Json;
using PodcastTranscribe.API.Models;
namespace PodcastTranscribe.API.Services
{
    public class AzureSpeechHandlerService : IAzureSpeechHandlerService
    {
        private readonly ILogger<AzureSpeechHandlerService> _logger;
        private readonly IAzureBlobStorageService _azureBlobStorageService;
        private readonly AzureSpeechSettings _azureSpeechSettings;
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
            _azureSpeechSettings = azureSpeechSettings.Value;
            _cosmosDbService = cosmosDbService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _azureSpeechSettings.SubscriptionKey);
        }

        public async Task<string> SubmitTranscriptionToAzureAsync(string blobAudioURL, string episodeId)
        {
            try
            {
                var requestUrl = $"https://{_azureSpeechSettings.Region}.api.cognitive.microsoft.com/speechtotext/{_azureSpeechSettings.ApiVersion}/transcriptions";

                _logger.LogInformation($"URL: {requestUrl}");
                var requestBody = new
                {
                    displayName = $"{episodeId}-transcription",
                    locale = "en-US",
                    contentUrls = new[] { blobAudioURL },
                    // model = (string)null,
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

                _logger.LogInformation($"subscription key: {_azureSpeechSettings.SubscriptionKey}");
                var response = await _httpClient.PostAsync(requestUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = response.StatusCode;
                    var errorMessage = $"**** Failed to submit transcription to Azure Speech Service: {response.ReasonPhrase}, status code: {statusCode}";
                    _logger.LogError(errorMessage);
                    throw new Exception(errorMessage);
                }

                var resJsonStr = await response.Content.ReadAsStringAsync();
                var resBody = JsonSerializer.Deserialize<JsonElement>(resJsonStr);

                // Extract the self URL from the response which contains the transcription ID
                var selfUrl = resBody.GetProperty("self").GetString();

                // _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, null, null, selfUrl, TranscriptionStatus.TranscriptionSubmitted);

                await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionSubmitted, azureSpeechUri: selfUrl);
                _logger.LogInformation($"!!! Successfully submitted transcription request. Self URL: {selfUrl}");
                return selfUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit transcription to Azure Speech Service");
                throw;
            }
        }

        public async Task<string> syncTranscriptionStatusAsync(string episodeId)
        {

            Episode? episode = await _cosmosDbService.GetEpisodeByIdAsync(episodeId);
            if (episode == null)
            {
                throw new Exception($"Episode {episodeId} not found");
            }
            if (episode.AzureSpeechURI == null)
            {
                throw new Exception($"Episode {episodeId} has no Azure Speech URI");
            }

            string requestUrl = episode.AzureSpeechURI;
            try
            {
                // _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _azureSpeechSettings.SubscriptionKey);

                var response = await _httpClient.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"Failed to get transcription status. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                    _logger.LogError(errorMsg);
                    throw new Exception(errorMsg);
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

                // get the transcription status from the response
                if (jsonDoc.TryGetProperty("status", out JsonElement statusElement))
                {
                    var status = statusElement.GetString();
                    _logger.LogInformation($"Transcription status for URI {episode.AzureSpeechURI}: {status}");
                    if (status == "Running")
                    {
                        await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionRunning);
                    }
                    else if (status == "Succeeded")
                    {
                        await this.UpdateTranscriptionResultAsync(episode, jsonDoc.GetProperty("self").GetString());
                        await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.TranscriptionSucceeded);
                    }
                    else if (status == "Failed")
                    {
                        await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.Failed);
                    }
                    else if (status == "NotStarted")
                    {
                        await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId, transcriptionStatus: TranscriptionStatus.NotStarted);
                    }
                    else
                    {
                        _logger.LogError($"Unknown transcription status: {status} for URI: {episode.AzureSpeechURI}");
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


        public async Task<bool> UpdateTranscriptionResultAsync(Episode episode, string azureSpeechUri)
        {
            if (episode == null || string.IsNullOrEmpty(azureSpeechUri))
            {
                throw new ArgumentNullException("Episode or Azure Speech URI cannot be null");
            }

            string transcriptionResultUrl = $"{azureSpeechUri}/files";
            var response = await _httpClient.GetAsync(transcriptionResultUrl);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"Failed to get transcription result. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                _logger.LogError(errorMsg);
                throw new Exception(errorMsg);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
            if (jsonDoc.TryGetProperty("values", out JsonElement valuesEntry))
            {
                foreach (var value in valuesEntry.EnumerateArray())
                {
                    if (value.TryGetProperty("kind", out JsonElement kindEntry) && kindEntry.GetString() == "Transcription")
                    {
                        value.TryGetProperty("links", out JsonElement linksEntry);
                        if (linksEntry.TryGetProperty("contentUrl", out JsonElement contentUrlEntry))
                        {
                            var textContentUrl = contentUrlEntry.GetString();
                            // _logger.LogInformation($"Transcription result response: {jsonResponse}");
                            // _logger.LogInformation($"*** Transcription result URL: {contentUrl}");
                            string displayContent = await this.GetTranscriptionContentAsync(textContentUrl);
                            await _cosmosDbService.UpdateEpisodeEntryAsync(episodeId: episode.Id, transcriptionResultDisplay: displayContent);

                            return true;
                        }
                        else
                        {
                            throw new Exception("Response JSON does not contain 'contentUrl' field.");
                        }

                    }
                    return true;
                }
            }
            return false;
        }

        private async Task<string> GetTranscriptionContentAsync(string textContentUrl)
        {
            var response = await _httpClient.GetAsync(textContentUrl);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"Failed to get transcription content. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                _logger.LogError(errorMsg);
                throw new Exception(errorMsg);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResponse);

            // Process the content as needed
            // get contentEntry.combinedRecognizedPhrases[0].display
            jsonDoc.TryGetProperty("combinedRecognizedPhrases", out JsonElement combinedRecognizedPhrasesEntry);
            var displayTextContent = combinedRecognizedPhrasesEntry.EnumerateArray().First().GetProperty("display").GetString();
            _logger.LogInformation($"Transcription content: {displayTextContent}");
            return displayTextContent;
        }

    }
}