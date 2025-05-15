using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    /// <summary>
    /// Implementation of the episode service interface.
    /// This service handles all business logic related to podcast episodes.
    /// </summary>
    public class EpisodeService : IEpisodeService
    {
        private readonly ILogger<EpisodeService> _logger;
        private readonly ITranscriptionSubmissionService _transcriptionSubmissionService;

        public EpisodeService(
            ILogger<EpisodeService> logger,
            ITranscriptionSubmissionService transcriptionSubmissionService)
        {
            _logger = logger;
            _transcriptionSubmissionService = transcriptionSubmissionService;
        }

        public Task<IEnumerable<Episode>> SearchEpisodesAsync(string name)
        {
            throw new NotImplementedException();
        }

        public Task<Episode> GetEpisodeByIdAsync(string id)
        {
            throw new NotImplementedException();
        }

        public async Task<(bool success, string message)> SubmitTranscriptionAsync(string episodeId)
        {
            try
            {
                _logger.LogInformation($"Submitting transcription for episode {episodeId}");
                var result = await _transcriptionSubmissionService.ProcessTranscriptionSubmissionAsync(episodeId);
                return (result.Success, result.Message);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error submitting transcription for episode {episodeId}: {ex.Message}";
                _logger.LogError(ex, errorMessage);
                return (false, errorMessage);
            }
        }

        public Task<TranscriptionStatus> GetTranscriptionStatusAsync(string episodeId)
        {
            throw new NotImplementedException();
        }
    }
} 