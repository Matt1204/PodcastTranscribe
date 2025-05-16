using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    public interface IAzureSpeechHandlerService
    {
        Task<string> SubmitTranscriptionToAzureAsync(string blobUrl, string episodeId);
        Task<string> GetTranscriptionStatusAsync(string transcriptionId);
        // Task<string> GetTranscriptionResultAsync(string transcriptionId);
        Task<bool> GetTranscriptionResultAsync(Episode episode, string azureSpeechUri);
    }
}