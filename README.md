# 🎙️ Podcast Transcription API

> A personal study project demonstrating full-stack development skills using modern cloud technologies and best practices.

## 📝 Overview

This project is a RESTful API service that converts podcast episodes into text transcriptions. It's designed to showcase my capabilities in building scalable, cloud-native applications using C# and Azure services.

### Core Functionalities

- 🔍 **Episode Search**: Search for podcast episodes by name
- 🎯 **Transcription Submission**: Submit podcast episodes for transcription
- 📊 **Status Tracking**: Monitor transcription job status
- 📥 **Result Retrieval**: Download transcription results
- 🎧 **Audio Processing**: Automatic audio optimization for efficient processing

## 🛠️ Technology Stack

### Backend Development
- **Language**: C# 10
- **Framework**: ASP.NET Core 7.0
- **Architecture**: RESTful API with Clean Architecture principles
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
- **Infrastructure as Code**: Azure Resource Manager (ARM) templates
- **Monitoring**: Azure Application Insights
- **Logging**: Structured logging with ILogger

### Development Tools & Practices
- **Code Quality**: 
  - SonarQube for code analysis
  - StyleCop for code style enforcement
- **Testing**:
  - xUnit for unit testing
  - Moq for mocking
- **API Testing**: Postman
- **Documentation**: Markdown

## 🚀 Getting Started

> Note: This project is for educational purposes only and is not intended for production use.

### Prerequisites
- .NET 7.0 SDK
- Azure subscription (for cloud services)
- Docker (optional, for containerization)

### Local Development Setup
1. Clone the repository
2. Configure Azure services and update connection strings
3. Run the application locally
4. Access the Swagger UI at `https://localhost:5050/swagger`

## 📚 Project Structure

```
PodcastTranscribe.API/
├── Controllers/         # API endpoints
├── Services/           # Business logic
├── Models/            # Data models
├── Configuration/     # App settings
└── Program.cs        # Application entry point
```

## 🔒 Security & Best Practices

- Secure configuration management
- Input validation and sanitization
- Proper error handling and logging
- Rate limiting and request throttling
- Azure Key Vault integration for secrets

## 🎯 Future Enhancements

- Real-time transcription status updates using SignalR
- Batch processing capabilities
- Advanced audio processing features
- User authentication and authorization
- Multi-language support

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

*This project is created for educational purposes to demonstrate my technical skills in full-stack development, cloud architecture, and DevOps practices.* 