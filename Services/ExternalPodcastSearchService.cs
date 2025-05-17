using PodcastTranscribe.API.Models;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Configuration;

namespace PodcastTranscribe.API.Services
{
    public class ExternalPodcastSearchService : IExternalPodcastSearchService
    {
        private readonly ILogger<ExternalPodcastSearchService> _logger;
        private readonly HttpClient _httpClient;
        private readonly ListennotesSettings _listennotesSettings;
        private readonly CosmosDbService _cosmosDbService;
        public ExternalPodcastSearchService(
            ILogger<ExternalPodcastSearchService> logger,
            IOptions<ListennotesSettings> listennotesSettings,
            CosmosDbService cosmosDbService
        )
        {
            _logger = logger;
            _listennotesSettings = listennotesSettings.Value;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-ListenAPI-Key", _listennotesSettings.ApiKey);
            _cosmosDbService = cosmosDbService;
        }

        public async Task<List<Episode>> SearchEpisodesByTitleAsync(string titleQuery)
        {
            string url = $"https://listen-api-test.listennotes.com/api/v2/search_episode_titles?q={titleQuery}";
            var key = _listennotesSettings.ApiKey;
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"Failed to search episodes. Status code: {response.StatusCode}, Reason: {response.ReasonPhrase}";
                _logger.LogError(errorMsg);
                throw new Exception(errorMsg);
            }

            var resJson = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonSerializer.Deserialize<JsonElement>(resJson);

            List<Episode> episodesFound = new List<Episode>();
            if (jsonDoc.TryGetProperty("results", out JsonElement resultsEntry))
            {
                foreach (var result in resultsEntry.EnumerateArray())
                {
                    var title = result.GetProperty("title_original").GetString();
                    var id = result.GetProperty("id").GetString();
                    var audioUrl = result.GetProperty("audio").GetString();
                    var description = result.GetProperty("description_original").GetString();
                    var podcastId = result.GetProperty("podcast").GetProperty("id").GetString();
                    episodesFound.Add(new Episode
                    {
                        Id = id,
                        Title = title ?? "",
                        PodcastId = podcastId ?? "",
                        TranscriptionStatus = TranscriptionStatus.NotStarted,
                        Description = description ?? "",
                        AudioUrl = audioUrl ?? "",
                    });
                }
            }
            List<Episode> episodesCreated = await _cosmosDbService.CreateEpisodeBatchAsync(episodesFound);
            return episodesCreated.DistinctBy(e => e.Id).ToList();
        }
    }
}


/*
curl -X GET -s 'https://listen-api.listennotes.com/api/v2/search_episode_titles?q=Jerusalem%20Demsas%20on%20The%20Dispossessed' \
  -H 'X-ListenAPI-Key: 9f91c957406b4187885afa408a913fbd'


curl -X GET -s 'https://listen-api-test.listennotes.com/api/v2/search_episode_titles?q=Jerusalem%20Demsas%20on%20The%20Dispossessed' \
  -H 'X-ListenAPI-Key: 9f91c957406b4187885afa408a913fbd'


*/