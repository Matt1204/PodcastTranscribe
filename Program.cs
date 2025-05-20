using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using PodcastTranscribe.API.Models;
using PodcastTranscribe.API.Services;
using Microsoft.OpenApi.Models;
using PodcastTranscribe.API.Configuration;
using Newtonsoft.Json.Converters;
using DotNetEnv; // For loading .env files in development


// Dependency Injection (DI) in ASP.NET Core:
// - 3 types of Services lifetimes in ASP.NET Core:
// Singleton: Created at startup, used for the entire application lifetime
// Scoped: Created once per HTTP request
// Transient: Created each time they are requested
// - use DI for complex, dependent, business logic services
// not use DI (instantiate services directly in controllers) for simple, stateless services (helper)


Env.Load(); // Load environment variables from .env

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddNewtonsoftJson(options =>
    {
        // if you need any special converters, add them here:
        options.SerializerSettings.Converters.Add(new StringEnumConverter());
    });

// Add services to the container.
// EpisodeController is inherited from ControllerBase, which marks it as a "controller"
// all controllers are registered here.
builder.Services.AddControllers();

// get Cosmos DB credentials from environment variables
builder.Services.Configure<CosmosDbSettings>(options =>
{
    options.ConnectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
    options.DatabaseName = Environment.GetEnvironmentVariable("COSMOS_DB_NAME");
    options.ContainerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");
});

// Configure Cosmos DB
builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var cosmosSettings = sp.GetRequiredService<IOptions<CosmosDbSettings>>().Value;
    var cosmosClientOptions = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    };
    return new CosmosClient(
        cosmosSettings.ConnectionString,
        cosmosClientOptions
    );
});

// CosmosDbService is a singleton, 1 instance created at startup.
// Register Azure Blob Storage service as a singleton. AzureBlobStorageService instantiated.
builder.Services.AddSingleton<CosmosDbService>();

// get Azure Blob Storage credentials from environment variables
builder.Services.Configure<AzureBlobStorageSettings>(options =>
{
    options.ConnectionString = Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING");
    options.ContainerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME");
});
builder.Services.AddSingleton<IAzureBlobStorageService, AzureBlobStorageService>();

// EpisodeService and IEpisodeService are scoped services, 
// 1 instance per HTTP request.
builder.Services.AddScoped<IEpisodeService, EpisodeService>();
builder.Services.AddScoped<ITranscriptionSubmissionService, TranscriptionSubmissionService>();

// get Azure Speech credentials from environment variables
builder.Services.Configure<AzureSpeechSettings>(options =>
{
    options.SubscriptionKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
    options.Region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
    options.ApiVersion = Environment.GetEnvironmentVariable("AZURE_SPEECH_API_VERSION");
});
// Register AzureSpeechHandlerService with credentials from environment
builder.Services.AddScoped<IAzureSpeechHandlerService, AzureSpeechHandlerService>(sp =>
    new AzureSpeechHandlerService(
        sp.GetRequiredService<ILogger<AzureSpeechHandlerService>>(),
        sp.GetRequiredService<IAzureBlobStorageService>(),
        sp.GetRequiredService<IOptions<AzureSpeechSettings>>(),
        sp.GetRequiredService<CosmosDbService>()
    )
);

builder.Services.Configure<ListennotesSettings>(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("LISTENNOTES_API_KEY");
    options.EndPoint = Environment.GetEnvironmentVariable("LISTENNOTES_END_POINT");
});
// Register ExternalPodcastSearchService with API key from environment
builder.Services.AddScoped<IExternalPodcastSearchService, ExternalPodcastSearchService>(sp =>
    new ExternalPodcastSearchService(
        sp.GetRequiredService<ILogger<ExternalPodcastSearchService>>(),
        sp.GetRequiredService<IOptions<ListennotesSettings>>(),
        sp.GetRequiredService<CosmosDbService>()
    )
);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//
app.Lifetime.ApplicationStarted.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        var blobService = app.Services.GetRequiredService<IAzureBlobStorageService>();
        if (await blobService.InitializeAsync())
            logger.LogInformation("*** Successfully connected to Azure Blob Storage");
        else
            logger.LogError("*** Failed to connect to Azure Blob Storage");

        var cosmosClient = app.Services.GetRequiredService<CosmosClient>();
        var cosmosSettings = app.Services.GetRequiredService<IOptions<CosmosDbSettings>>().Value;
        var container = cosmosClient.GetContainer(cosmosSettings.DatabaseName, cosmosSettings.ContainerName);
        await container.ReadContainerAsync();
        logger.LogInformation("*** Successfully connected to Cosmos DB");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "*** Initialization error");
    }
});

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "API is running");
// app.Urls.Add("http://localhost:5050");
app.Urls.Add("http://0.0.0.0:5050");
// app.Urls.Add("https://localhost:7050");
app.Run();
