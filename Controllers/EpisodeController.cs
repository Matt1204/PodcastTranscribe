using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PodcastTranscribe.API.Models;
using PodcastTranscribe.API.Services;

namespace PodcastTranscribe.API.Controllers
{
    /// <summary>
    /// Controller responsible for handling podcast episode-related operations.
    /// This includes searching for episodes and managing their transcriptions.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EpisodeController : ControllerBase
    {
        private readonly ILogger<EpisodeController> _logger;
        private readonly IEpisodeService _episodeService;
        private readonly CosmosDbService _cosmosDbService;

        public EpisodeController(
            ILogger<EpisodeController> logger,
            IEpisodeService episodeService,
            CosmosDbService cosmosDbService)
        {
            _logger = logger;
            _episodeService = episodeService;
            _cosmosDbService = cosmosDbService;
        }

        /// <summary>
        /// Test endpoint for Cosmos DB CRUD operations
        /// </summary>
        [HttpPost("test_db")]
        public async Task<IActionResult> TestDb([FromBody] Episode episode)
        {
            try
            {
                _logger.LogInformation($"Received episode data: {episode.Title}");

                // Generate a unique ID if not provided
                if (string.IsNullOrEmpty(episode.Id))
                {
                    episode.Id = $"episode-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                }

                // Set default values if not provided
                if (episode.TranscriptionStatus == 0) // Default enum value, for test only
                {
                    episode.TranscriptionStatus = TranscriptionStatus.NotStarted;
                }

                // Create
                var createdEpisode = await _cosmosDbService.CreateEpisodeAsync(episode);
                _logger.LogInformation("!!!!! Created episode: {@Episode}", createdEpisode);
                
                // Read
                var retrievedEpisode = await _cosmosDbService.GetEpisodeByIdAsync(createdEpisode.Id);
                _logger.LogInformation("!!!!!Retrieved episode: {@Episode}", retrievedEpisode);
                
                // // Update
                // retrievedEpisode.title = "Updated Title";
                // var updatedEpisode = await _cosmosDbService.UpdateEpisodeAsync(retrievedEpisode);
                // _logger.LogInformation("Updated episode: {@Episode}", updatedEpisode);
                
                // // Search
                // var searchResults = await _cosmosDbService.SearchEpisodesAsync(episode.title);
                // _logger.LogInformation("Search results: {@Results}", searchResults);

                return Ok(new
                {
                    created = createdEpisode,
                    // retrieved = retrievedEpisode,
                    // updated = updatedEpisode,
                    // searchResults = searchResults
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid episode data");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing DB operations");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Demo endpoint to test the API
        /// </summary>
        [HttpGet("hello")]
        public IActionResult HelloWorld()
        {
            return Ok(new { message = "Hello from Podcast Transcribe API!" });
        }

        /// <summary>
        /// Searches for podcast episodes by name
        /// </summary>
        /// <param name="name">The name to search for</param>
        /// <returns>A list of matching episodes</returns>
        [HttpGet]
        public async Task<IActionResult> SearchEpisodes([FromQuery] string name)
        {
            var episodes = await _episodeService.SearchEpisodesAsync(name);
            return Ok(episodes);
        }

        /// <summary>
        /// Submits a transcription job for a specific episode
        /// </summary>
        /// <param name="id">The episode ID</param>
        /// <returns>The status of the transcription request</returns>
        [HttpPost("{id}/transcription")]
        public async Task<IActionResult> SubmitTranscription(string id)
        {
            var success = await _episodeService.SubmitTranscriptionAsync(id);
            return success ? Ok(
                new { message = $"Transcription job for episode {id} submitted successfully" }
            ) : BadRequest();
        }

        /// <summary>
        /// Gets the transcription status and result for an episode
        /// </summary>
        /// <param name="id">The episode ID</param>
        /// <returns>The transcription status and result if available</returns>
        [HttpGet("{id}/transcription")]
        public async Task<IActionResult> GetTranscription(string id)
        {
            var status = await _episodeService.GetTranscriptionStatusAsync(id);
            return Ok(new { status });
        }
    }
} 