using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace PodcastTranscribe.API.Models
{
    /// <summary>
    /// Represents a podcast episode in the system.
    /// This model is used for both storing episode metadata and tracking transcription status.
    /// </summary>
    public class Episode
    {
        /// <summary>
        /// Unique identifier for the episode
        /// </summary>
        // [JsonPropertyName("id")]
        [JsonProperty("id")]
        public required string Id { get; set; }

        /// <summary>
        /// Title of the podcast episode
        /// </summary>
        [JsonProperty("title")]
        public required string Title { get; set; }

        /// <summary>
        /// Description of the episode
        /// </summary>
        [JsonProperty("description")]
        public required string Description { get; set; }

        /// <summary>
        /// Public URL to the audio file
        /// </summary>
        [JsonProperty("audio_url")]
        public required string AudioUrl { get; set; }

        /// <summary>
        /// Status of the transcription process
        /// </summary>
        [JsonProperty("transcription_status")]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public TranscriptionStatus TranscriptionStatus { get; set; }

        /// <summary>
        /// The transcribed text (if available)
        /// </summary>
        [JsonProperty("transcription_result_display")]
        public string? TranscriptionResultDisplay { get; set; }

        /// <summary>
        /// Azure Blob Storage URI for the processed audio file
        /// </summary>
        [JsonProperty("processed_audio_blob_uri")]
        public string? ProcessedAudioBlobUri { get; set; }

        /// <summary>
        /// Azure Speech URI for the transcription result
        /// </summary>
        [JsonProperty("azure_speech_uri")]
        public string? AzureSpeechURI { get; set; }
    }

    /// <summary>
    /// Represents the possible states of a transcription job
    /// </summary>
    public enum TranscriptionStatus
    {
        NotStarted,
        Processing,
        TranscriptionSubmitted,
        TranscriptionRunning,
        TranscriptionSucceeded,
        Failed
    }
}