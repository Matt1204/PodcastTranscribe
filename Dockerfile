# 1) Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src/PodcastTranscribe.API

# copy project file and restore dependencies
COPY ["PodcastTranscribe.API.csproj", "./"]
RUN dotnet restore "PodcastTranscribe.API.csproj"

# copy everything else and publish
COPY . .
RUN dotnet publish -c Release -o /app/publish


# 2) Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
# Install FFmpeg for Xabe.FFmpeg usage
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app

# copy published output
COPY --from=build /app/publish .

# expose the same ports Kestrel uses
EXPOSE 5050
EXPOSE 7050

# entry point
ENTRYPOINT ["dotnet", "PodcastTranscribe.API.dll"]