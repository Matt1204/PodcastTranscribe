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


// Dependency Injection (DI) in ASP.NET Core:
// - 3 types of Services lifetimes in ASP.NET Core:
// Singleton: Created at startup, used for the entire application lifetime
// Scoped: Created once per HTTP request
// Transient: Created each time they are requested
// - use DI for complex, dependent, business logic services
// not use DI (instantiate services directly in controllers) for simple, stateless services (helper)



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

// Configure Azure Settings
// Removed unused AzureSettings configuration, as it is not used.

// Configure Cosmos DB
builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var options = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    };
    return new CosmosClient(
        connectionString: configuration.GetValue<string>("CosmosDb:ConnectionString"),
        options
    );
});

// CosmosDbService is a singleton, 1 instance created at startup.
builder.Services.AddSingleton<CosmosDbService>();

// Register Azure Blob Storage service as a singleton. AzureBlobStorageService instantiated.
builder.Services.AddSingleton<IAzureBlobStorageService, AzureBlobStorageService>();

// EpisodeService and IEpisodeService are scoped services, 
// 1 instance per HTTP request.
builder.Services.AddScoped<IEpisodeService, EpisodeService>();
builder.Services.AddScoped<ITranscriptionSubmissionService, TranscriptionSubmissionService>();
builder.Services.AddScoped<IAzureSpeechHandlerService, AzureSpeechHandlerService>();
// Add configuration sections
builder.Services.Configure<CosmosDbSettings>(
    builder.Configuration.GetSection("CosmosDb"));
builder.Services.Configure<AzureBlobStorageSettings>(
    builder.Configuration.GetSection("AzureBlobStorage"));
builder.Services.Configure<AzureSpeechSettings>(
    builder.Configuration.GetSection("AzureSpeech"));



// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
app.Urls.Add("http://localhost:5050");
app.Urls.Add("https://localhost:7050");
app.Run();
