using System.Threading.Tasks;

namespace PodcastTranscribe.API.Services
{
    public interface ITranscriptionSubmissionService
    {
        Task<TranscriptionSubmissionResult> ProcessTranscriptionSubmissionAsync(string episodeId);
    }
} 