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
        private readonly CosmosDbService _cosmosDbService;
        private readonly IAzureSpeechHandlerService _azureSpeechHandlerService;
        public EpisodeService(
            ILogger<EpisodeService> logger,
            ITranscriptionSubmissionService transcriptionSubmissionService,
            CosmosDbService cosmosDbService,
            IAzureSpeechHandlerService azureSpeechHandlerService)
        {
            _logger = logger;
            _transcriptionSubmissionService = transcriptionSubmissionService;
            _cosmosDbService = cosmosDbService;
            _azureSpeechHandlerService = azureSpeechHandlerService;

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
            var episode = await _cosmosDbService.GetEpisodeByIdAsync(episodeId);
            if (episode == null)
            {
                _logger.LogError($"Episode {episodeId} not found");
                return (false, $"Episode {episodeId} not found in db");
            }
            // Trigger the transcription process asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    var (isSuccess, message) = await _transcriptionSubmissionService.ProcessTranscriptionSubmissionAsync(episode);
                    if (isSuccess)
                    {
                        _logger.LogInformation($"!!! Transcription task submitted to Azure for episode {episodeId}");
                    }
                    else
                    {
                        _logger.LogError($"*** Error processing transcription for episode {episodeId}: {message}");
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"*** Error processing transcription for episode {episodeId}");
                }
            });

            return (true, "Transcription task submitted in progress");


            // try
            // {
            //     _logger.LogInformation($"Submitting transcription for episode {episodeId}");
            //     var result = await _transcriptionSubmissionService.ProcessTranscriptionSubmissionAsync(episodeId);
            //     return (result.Success, result.Message);
            // }
            // catch (Exception ex)
            // {
            //     var errorMessage = $"Error submitting transcription for episode {episodeId}: {ex.Message}";
            //     _logger.LogError(ex, errorMessage);
            //     return (false, errorMessage);
            // }
        }

        public async Task<TranscriptionResultResponse> GetTranscriptionResultAsync(string episodeId)
        {
            await _azureSpeechHandlerService.syncTranscriptionStatusAsync(episodeId);
            Episode? episode = await _cosmosDbService.GetEpisodeByIdAsync(episodeId);

            if (episode == null)
            {
                _logger.LogError($"Episode {episodeId} not found");
                return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.Failed, TranscriptionResultDisplay = "Error: Episode not found" };
            }

            switch (episode.TranscriptionStatus)
            {
                case TranscriptionStatus.TranscriptionSucceeded:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.TranscriptionSucceeded, TranscriptionResultDisplay = episode.TranscriptionResultDisplay ?? "Empty transcription result" };
                case TranscriptionStatus.TranscriptionRunning:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.TranscriptionRunning };
                case TranscriptionStatus.TranscriptionSubmitted:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.TranscriptionSubmitted };
                case TranscriptionStatus.NotStarted:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.NotStarted };
                case TranscriptionStatus.Processing:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.Processing };
                case TranscriptionStatus.Failed:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.Failed };
                default:
                    return new TranscriptionResultResponse { TranscriptionStatus = TranscriptionStatus.Failed, TranscriptionResultDisplay = "Error: Unknown transcription status" };
            }
        }
    }

    public class TranscriptionResultResponse
    {
        public required TranscriptionStatus TranscriptionStatus { get; set; }
        public string TranscriptionResultDisplay { get; set; }
    }

}