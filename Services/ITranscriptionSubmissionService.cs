using System.Threading.Tasks;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    public interface ITranscriptionSubmissionService
    {
        Task<(bool isSuccess, string message)> ProcessTranscriptionSubmissionAsync(Episode episode);
    }
} 