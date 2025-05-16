using Microsoft.Extensions.Options;
using PodcastTranscribe.API.Models;

namespace PodcastTranscribe.API.Services
{
    public interface IAzureSpeechHandlerService
    {
        Task<string> SubmitTranscriptionToAzureAsync(string blobUrl, string episodeId);
        Task<string> syncTranscriptionStatusAsync(string transcriptionId);
        // Task<string> GetTranscriptionResultAsync(string transcriptionId);
        Task<bool> UpdateTranscriptionResultAsync(Episode episode, string azureSpeechUri);
    }
}