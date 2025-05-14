using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Cosmos;
using PodcastTranscribe.API.Models;
using PodcastTranscribe.API.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure Azure Settings
builder.Services.Configure<AzureSettings>(
    builder.Configuration.GetSection("AzureSettings"));

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

builder.Services.AddSingleton<CosmosDbService>();

// Register services
builder.Services.AddScoped<IEpisodeService, EpisodeService>();

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

// Verify Cosmos DB connection
var cosmosClient = app.Services.GetRequiredService<CosmosClient>();
var configuration = app.Services.GetRequiredService<IConfiguration>();
var databaseName = configuration.GetValue<string>("CosmosDb:DatabaseName");
var containerName = configuration.GetValue<string>("CosmosDb:ContainerName");

try
{
    var database = cosmosClient.GetDatabase(databaseName);
    var container = database.GetContainer(containerName);
    await container.ReadContainerAsync();
    app.Logger.LogInformation("Successfully connected to Cosmos DB");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to connect to Cosmos DB");
    throw;
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
