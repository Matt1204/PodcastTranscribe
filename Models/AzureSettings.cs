namespace PodcastTranscribe.API.Models
{
    /// <summary>
    /// Configuration settings for Azure services used in the application.
    /// This class is used to bind configuration values from appsettings.json.
    /// </summary>
    public class AzureSettings
    {
        /// <summary>
        /// Azure Cosmos DB connection settings
        /// </summary>
        public CosmosDbSettings CosmosDb { get; set; }

        /// <summary>
        /// Azure Blob Storage settings
        /// </summary>
        public BlobStorageSettings BlobStorage { get; set; }

        /// <summary>
        /// Azure Speech Services settings
        /// </summary>
        public SpeechServicesSettings SpeechServices { get; set; } = new SpeechServicesSettings();
    }

    /// <summary>
    /// Settings specific to Azure Cosmos DB
    /// </summary>
    public class CosmosDbSettings
    {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
    }

    /// <summary>
    /// Settings specific to Azure Blob Storage
    /// </summary>
    public class BlobStorageSettings
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
    }

    /// <summary>
    /// Settings specific to Azure Speech Services
    /// </summary>
    public class SpeechServicesSettings
    {
        public string SubscriptionKey { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
} 