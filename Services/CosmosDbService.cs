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

        public async Task<List<Episode>> SearchEpisodesAsync(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return new List<Episode>();

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE CONTAINS(UPPER(c.title), @title)")
                .WithParameter("@title", title.ToUpperInvariant());

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

        /// <summary>
        /// Creates multiple episode items in Cosmos DB in a single transactional batch.
        /// All episodes must have the same partition key (the same Id in this implementation).
        /// If any operation in the batch fails, none of the items are created (atomic operation).
        /// </summary>
        /// <param name="episodes">List of episodes to create. All episodes should have the same Id (partition key).</param>
        /// <returns>A task that represents the asynchronous batch create operation.</returns>
        public async Task<List<Episode>> CreateEpisodeBatchAsync(List<Episode> episodes)
        {
            var partitionKey = new PartitionKey(episodes[0].Id);
            var batch = _dbContainer.CreateTransactionalBatch(partitionKey);

            List<Episode> episodesToCreate = new List<Episode>();
            List<Episode> episodesExisting = new List<Episode>();
            foreach (var episode in episodes)
            {
                var existingEpisode = await GetEpisodeByIdAsync(episode.Id);
                if (existingEpisode != null)
                {
                    _logger.LogWarning($"Episode with ID {episode.Id} already exists. Skipping.");
                    episodesExisting.Add(existingEpisode);
                    continue;
                }

                batch.CreateItem(episode);
                episodesToCreate.Add(episode);
            }

            if (episodesToCreate.Count > 0)
            {
                await batch.ExecuteAsync();
            }

            return episodesExisting.Concat(episodesToCreate).ToList();
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

            // _logger.LogInformation($"Updating episode with ID: {episodeId}. Updates: {string.Join(", ", updates)}");

            return await UpdateEpisodeAsync(episode);
        }
    }
}