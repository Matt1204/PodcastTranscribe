using System.Collections.Generic;
using System.Threading.Tasks;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    /// <summary>
    /// Interface for episode service operations.
    /// Defines the contract for podcast episode-related business logic.
    /// </summary>
    public interface IEpisodeService
    {
        /// <summary>
        /// Searches for episodes by name.
        /// </summary>
        /// <param name="name">The name to search for.</param>
        /// <returns>A collection of matching episodes.</returns>
        Task<IEnumerable<Episode>> SearchEpisodesAsync(string name);

        /// <summary>
        /// Retrieves an episode by its ID.
        /// </summary>
        /// <param name="id">The episode ID.</param>
        /// <returns>The episode if found, null otherwise.</returns>
        Task<Episode> GetEpisodeByIdAsync(string id);

        /// <summary>
        /// Submits a transcription request for an episode.
        /// </summary>
        /// <param name="episodeId">The ID of the episode to transcribe.</param>
        /// <returns>A tuple containing success status and message.</returns>
        Task<(bool success, string message)> SubmitTranscriptionAsync(string episodeId);

        /// <summary>
        /// Gets the current status of a transcription job.
        /// </summary>
        /// <param name="episodeId">The ID of the episode.</param>
        /// <returns>The current transcription status.</returns>
        Task<TranscriptionResultResponse> GetTranscriptionResultAsync(string episodeId);
    }
} 