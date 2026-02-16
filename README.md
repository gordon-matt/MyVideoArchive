# MyVideoArchive

A simple video archiving application inspired by TubeArchivist, built with ASP.NET Core MVC and OData.

## Features

- **Home Page**: View latest downloaded videos from your subscribed channels
- **Channels Page**: Manage channel subscriptions
- **Channel Details**: View all videos and playlists for a specific channel  
- **Downloads Page**: Browse and download available videos from your subscriptions
- **Background Service**: Automated video downloading using yt-dlp

## Technology Stack

- **ASP.NET Core 10.0** - Modern web framework
- **OData** - RESTful API for data access using Extenso.AspNetCore.OData
- **Entity Framework Core** - Data access with repository pattern using Extenso.Data.Entity
- **YoutubeDLSharp** - C# wrapper for yt-dlp video downloader
- **Knockout.js** - Frontend MVVM framework
- **Bootstrap 5** - Responsive UI
- **Autofac** - Dependency injection container

## Project Structure

### Domain Entities (`Data/Entities/`)

- **Channel**: Represents a YouTube channel subscription
- **Video**: Represents a video from a channel
- **Playlist**: Represents a YouTube playlist subscription

### Controllers

#### MVC Controllers (`Controllers/`)
- **HomeController**: Displays latest downloaded videos
- **ChannelsController**: Lists channels and displays channel details
- **DownloadsController**: Shows available videos for download

#### OData API Controllers (`Controllers/Api/`)
- **ChannelApiController**: CRUD operations for channels
- **VideoApiController**: CRUD operations for videos  
- **PlaylistApiController**: CRUD operations for playlists

### Infrastructure

- **ODataRegistrar**: Registers OData entity sets and routes
- **ApplicationDbContext**: EF Core database context
- **ApplicationDbContextFactory**: Factory for creating DB contexts (used by Extenso repositories)
- **VideoDownloadService**: Background service for downloading videos

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- SQL Server (or use in-memory database for development)

### Configuration

The application uses SQL Server by default. To use an in-memory database for development, leave the `DefaultConnection` connection string empty in `appsettings.json`.

### Running the Application

```bash
cd MyVideoArchive
dotnet run
```

The application will automatically download yt-dlp and ffmpeg binaries on first run.

## Usage

### Adding Channels

1. Navigate to the Channels page
2. Click "Add Channel"
3. Enter a YouTube channel URL
4. The channel will be added to your subscriptions

### Downloading Videos

1. Navigate to the Downloads page
2. Click "Check for Updates" to scan subscribed channels for new videos
3. Click "Download" on any video to queue it for download
4. Videos will be downloaded by the background service

### Viewing Downloaded Videos

The Home page displays all your downloaded videos in a grid layout with thumbnails, titles, and metadata.

## API Endpoints

OData endpoints are available at `/odata/`:

- `/odata/ChannelApi` - Channel operations
- `/odata/VideoApi` - Video operations  
- `/odata/PlaylistApi` - Playlist operations

All endpoints support standard OData query options: `$filter`, `$orderby`, `$expand`, `$select`, `$top`, `$skip`, etc.

### Example Queries

```
GET /odata/VideoApi?$filter=DownloadedAt ne null&$orderby=DownloadedAt desc
GET /odata/VideoApi?$expand=Channel&$top=20
GET /odata/ChannelApi?$orderby=Name
```

## Architecture

The application follows a clean, simple architecture:

- **Thin MVC Controllers**: Handle view rendering only
- **OData API Controllers**: Handle all business logic and data operations
- **Repository Pattern**: Data access abstracted through `IRepository<T>` from Extenso
- **Background Services**: Long-running tasks like video downloads

This pattern keeps the codebase maintainable and testable while leveraging the powerful querying capabilities of OData.

## Future Enhancements

- Video playback interface
- Download queue management
- Automatic channel/playlist monitoring
- Video metadata extraction from yt-dlp
- Search and filtering improvements
- User authentication and multi-user support
- Download progress tracking
- Subtitle download support

## License

This project is for educational and personal use.
