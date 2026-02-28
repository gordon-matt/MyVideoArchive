# MyVideoArchive

A simple video archiving application inspired by TubeArchivist, built with ASP.NET Core MVC and OData.

## Features

- **Home Page**: View latest downloaded videos from your subscribed channels
- **Channels Page**: Manage channel subscriptions
- **Channel Details**: View all videos and playlists for a specific channel  
- **Downloads Page**: Browse and download available videos from your subscriptions
- **Background Services**: Automated video and metadata downloading using yt-dlp

## Technology Stack

- **ASP.NET Core 10.0**
- **PostgreSql** - Database
- **OData** - RESTful API for data access using Extenso.AspNetCore.OData
- **Entity Framework Core** - Data access with repository pattern using Extenso.Data.Entity
- **YoutubeDLSharp** - C# wrapper for yt-dlp video downloader
- **Knockout.js** - Frontend MVVM framework
- **Bootstrap 5** - Responsive UI
- **Autofac** - Dependency injection container

## Getting Started

For now, just run it as you would any standard .NET app. I will build an official Docker image at some point which will be more convenient.

### Configuration

- **Local development**: Use **User Secrets** for the connection string (Visual Studio: right-click project → **Manage User Secrets**).

- **Docker**: The container reads settings from environment variables in `docker-compose.yml` or from a `.env` file.

- **Initial admin user**: On first run, an admin user is seeded. Configure via `SeedAdmin:Email` and `SeedAdmin:Password` (User Secrets or appsettings) or `SeedAdmin__Email` and `SeedAdmin__Password` (environment/Docker). Defaults: `admin@myvideoarchive.local` / `Admin@123`.

**NOTE:** The application will automatically download yt-dlp and ffmpeg binaries on first run.