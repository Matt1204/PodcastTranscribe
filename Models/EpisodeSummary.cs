namespace PodcastTranscribe.API.Models
{
    public class EpisodeSummary
    {
        public required string Id { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required TranscriptionStatus TranscriptionStatus { get; set; }
    }
}