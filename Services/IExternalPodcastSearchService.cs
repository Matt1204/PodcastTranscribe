using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    public interface IExternalPodcastSearchService
    {
        Task<List<Episode>> SearchEpisodesByTitleAsync(string title);
    }
}