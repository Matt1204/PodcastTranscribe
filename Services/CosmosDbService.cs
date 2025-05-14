using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    public class CosmosDbService
    {
        private readonly Container _container;
        private readonly CosmosClient _cosmosClient;

        public CosmosDbService(CosmosClient cosmosClient, IConfiguration configuration)
        {
            _cosmosClient = cosmosClient;
            var databaseName = configuration.GetValue<string>("CosmosDb:DatabaseName");
            var containerName = configuration.GetValue<string>("CosmosDb:ContainerName");
            _container = _cosmosClient.GetContainer(databaseName, containerName);
        }

        public async Task<IEnumerable<Episode>> SearchEpisodesAsync(string name)
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE CONTAINS(c.title, @name)")
                .WithParameter("@name", name);

            var results = new List<Episode>();
            var queryResultSetIterator = _container.GetItemQueryIterator<Episode>(query);

            while (queryResultSetIterator.HasMoreResults)
            {
                var currentResultSet = await queryResultSetIterator.ReadNextAsync();
                results.AddRange(currentResultSet);
            }

            return results;
        }

        public async Task<Episode?> GetEpisodeByIdAsync(string id)
        {
            try
            {
                var response = await _container.ReadItemAsync<Episode>(id, new PartitionKey(id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<Episode> UpdateEpisodeAsync(Episode episode)
        {
            try
            {
                if (string.IsNullOrEmpty(episode.Id))
                {
                    throw new ArgumentException("Episode ID is required");
                }

                var response = await _container.UpsertItemAsync(
                    episode,
                    new PartitionKey(episode.Id),
                    new ItemRequestOptions
                    {
                        EnableContentResponseOnWrite = true
                    }
                );
                return response.Resource;
            }
            catch (CosmosException ex)
            {
                throw new Exception($"Cosmos DB error: {ex.Message}", ex);
            }
        }

        public async Task<Episode> CreateEpisodeAsync(Episode episode)
        {
            try
            {
                // Generate a new ID if not provided
                if (string.IsNullOrEmpty(episode.Id))
                {
                    episode.Id = "episode-" + Guid.NewGuid().ToString();
                }
                Console.WriteLine($"!!!!!!!! Creating episode with ID: {episode.Id}");
                // print episode object
                Console.WriteLine(episode.ToString());

                // Ensure the partition key is set
                var partitionKey = new PartitionKey(episode.Id);

                // Create the document in Cosmos DB
                var response = await _container.CreateItemAsync(
                    episode,
                    partitionKey,
                    new ItemRequestOptions
                    {
                        EnableContentResponseOnWrite = true
                    }
                );

                Console.WriteLine($"Successfully created episode with ID: {response.Resource.Id}");
                return response.Resource;
            }
            catch (CosmosException ex)
            {
                throw new Exception($"Failed to create episode in Cosmos DB: {ex.Message}", ex);
            }
        }
    }
}