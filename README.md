# 🎙️ PodcastTranscribe

> A personal study project 

## 📝 Overview

This is a RESTful API service that converts podcast episodes into text transcriptions.

### Core Functionalities

- 🔍 **Episode Search**: Search for podcast episodes by name
- 🎯 **Transcription Submission**: Submit podcast episodes for transcription
- 📊 **Status Tracking**: Monitor transcription job status
- 📥 **Result Retrieval**: Download transcription results
- 🎧 **Audio Processing**: Automatic audio optimization for efficient processing

## 🛠️ Technology Stack

### Backend Development
- **Language**: C#
- **Framework**: ASP.NET Core 9.0
- **API**: RESTful API
- **Dependency Injection**: Built-in .NET Core DI container
- **API Documentation**: Swagger/OpenAPI

### Cloud Services (Azure)
- **Database**: Azure Cosmos DB (NoSQL)
- **Storage**: Azure Blob Storage
- **Speech Processing**: Azure Speech-to-Text Batch API
- **Hosting**: Azure App Service

### DevOps & Infrastructure
- **Version Control**: Git
- **CI/CD**: GitHub Actions
- **Containerization**: Docker
- **Logging**: Structured logging with ILogger


## 📚 Project Structure

```
PodcastTranscribe.API/
├── Controllers/         # API endpoints
├── Services/           # Business logic
├── Models/            # Data models
├── Configuration/     # App settings
└── Program.cs        # Application entry point
```

## 🎯 Future Enhancements

- Real-time transcription status updates using SignalR
- Batch processing capabilities
- User authentication and authorization