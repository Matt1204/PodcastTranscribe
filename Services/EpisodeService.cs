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
        private readonly IExternalPodcastSearchService _externalPodcastSearchService;
        public EpisodeService(
            ILogger<EpisodeService> logger,
            ITranscriptionSubmissionService transcriptionSubmissionService,
            CosmosDbService cosmosDbService,
            IAzureSpeechHandlerService azureSpeechHandlerService,
            IExternalPodcastSearchService externalPodcastSearchService
            )
        {
            _logger = logger;
            _transcriptionSubmissionService = transcriptionSubmissionService;
            _cosmosDbService = cosmosDbService;
            _azureSpeechHandlerService = azureSpeechHandlerService;
            _externalPodcastSearchService = externalPodcastSearchService;
        }

        public async Task<List<EpisodeSummary>> SearchEpisodesAsync(string title)
        {
            List<Episode> episodeDb = await _cosmosDbService.SearchEpisodesAsync(title);
            if (episodeDb.Count == 0)
            {
                List<Episode> externalEpisodes = await _externalPodcastSearchService.SearchEpisodesByTitleAsync(title);
                episodeDb.AddRange(externalEpisodes);
            }
            List<EpisodeSummary> episodeSummaryList = episodeDb.Select(episode =>
            {
                return new EpisodeSummary
                {
                    Id = episode.Id,
                    Title = episode.Title,
                    Description = episode.Description,
                    TranscriptionStatus = episode.TranscriptionStatus
                };
            }).ToList();

            // List<Episode> externalEpisodes = await _externalPodcastSearchService.SearchEpisodesByTitleAsync(title);

            return episodeSummaryList;
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
                return (false, $"Episode {episodeId}: does not exist");
            }

            if (episode.TranscriptionStatus == TranscriptionStatus.TranscriptionSucceeded && !string.IsNullOrEmpty(episode.TranscriptionResultDisplay))
            {
                _logger.LogInformation($"!!! Transcription already generated for episode {episodeId}");
                return (true, $"Episode {episodeId}: Transcription already generated, no need to submit.");
            }
            else if (
                episode.TranscriptionStatus == TranscriptionStatus.Processing ||
                episode.TranscriptionStatus == TranscriptionStatus.TranscriptionSubmitted ||
                episode.TranscriptionStatus == TranscriptionStatus.TranscriptionRunning
                )
            {
                return (true, $"Episode {episodeId}: Transcription task is in progress, please check back later for results");
            }
            else if (episode.TranscriptionStatus == TranscriptionStatus.NotStarted)
            {
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

                return (true, $"Transcription task submitted for episode {episodeId}, please check back later for results");
            }
            else if (episode.TranscriptionStatus == TranscriptionStatus.Failed)
            {
                return (false, $"Transcription task failed for episode {episodeId}");
            }
            else
            {
                return (false, $"Unknown transcription status for episode {episodeId}");
            }
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