using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Models;
using PodcastTranscribe.API.Configuration;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace PodcastTranscribe.API.Services
{
    public class CosmosDbService
    {
        private readonly Container _dbContainer;
        private readonly ILogger<CosmosDbService> _logger;

        public CosmosDbService(CosmosClient cosmosClient, IOptions<CosmosDbSettings> settings, ILogger<CosmosDbService> logger)
        {
            _dbContainer = cosmosClient.GetContainer(settings.Value.DatabaseName, settings.Value.ContainerName);
            _logger = logger;
        }

        public async Task<List<Episode>> SearchEpisodesAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new List<Episode>();

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE CONTAINS(UPPER(c.title), @name)")
                .WithParameter("@name", name.ToUpperInvariant());

            var results = new List<Episode>();
            using var iterator = _dbContainer.GetItemQueryIterator<Episode>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.Resource);
            }

            return results;
        }

        public async Task<Episode?> GetEpisodeByIdAsync(string id)
        {
            try
            {
                var response = await _dbContainer.ReadItemAsync<Episode>(id, new PartitionKey(id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<Episode> UpdateEpisodeAsync(Episode episode)
        {
            if (string.IsNullOrEmpty(episode.Id))
                throw new ArgumentException("Episode ID is required");

            try
            {
                var response = await _dbContainer.UpsertItemAsync(
                    episode,
                    new PartitionKey(episode.Id),
                    new ItemRequestOptions { EnableContentResponseOnWrite = true });
                return response.Resource;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Cosmos DB error on update");
                throw;
            }
        }

        public async Task<Episode> CreateEpisodeAsync(Episode episode)
        {
            if (string.IsNullOrEmpty(episode.Id))
                episode.Id = "episode-" + Guid.NewGuid();

            _logger.LogInformation($"Creating episode with ID: {episode.Id}");
            _logger.LogDebug(JsonConvert.SerializeObject(episode));

            try
            {
                var partitionKey = new PartitionKey(episode.Id);
                var response = await _dbContainer.CreateItemAsync(
                    episode,
                    partitionKey,
                    new ItemRequestOptions { EnableContentResponseOnWrite = true });

                _logger.LogInformation($"Successfully created episode with ID: {response.Resource.Id}");
                return response.Resource;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Failed to create episode in Cosmos DB");
                throw;
            }
        }

        public async Task<Episode> UpdateEpisodeEntryAsync(
            string episodeId,
            string? transcriptionResultDisplay = null,
            string? processedAudioBlobUri = null,
            string? azureSpeechUri = null,
            TranscriptionStatus? transcriptionStatus = null)
        {
            var episode = await GetEpisodeByIdAsync(episodeId)
                ?? throw new Exception($"Episode {episodeId} not found");

            var updates = new List<string>();

            if (transcriptionResultDisplay != null)
            {
                episode.TranscriptionResultDisplay = transcriptionResultDisplay;
                updates.Add($"transcriptionResultDisplay: {transcriptionResultDisplay}");
            }
            if (processedAudioBlobUri != null)
            {
                episode.ProcessedAudioBlobUri = processedAudioBlobUri;
                updates.Add($"processedAudioBlobUri: {processedAudioBlobUri}");
            }
            if (azureSpeechUri != null)
            {
                episode.AzureSpeechURI = azureSpeechUri;
                updates.Add($"azureSpeechUri: {azureSpeechUri}");
            }
            if (transcriptionStatus != null)
            {
                episode.TranscriptionStatus = transcriptionStatus.Value;
                updates.Add($"transcriptionStatus: {transcriptionStatus.Value}");
            }

            _logger.LogInformation($"Updating episode with ID: {episodeId}. Updates: {string.Join(", ", updates)}");

            return await UpdateEpisodeAsync(episode);
        }
    }
}