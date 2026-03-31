<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/MyVideoArchive/wwwroot/img/MVA-Logo.png" alt="Logo" width="250" />

# MyVideoArchive

A video archiving application inspired by [TubeArchivist](https://github.com/tubearchivist/tubearchivist). Archive your favourite videos, channels, and playlists from YouTube and BitChute, or create custom channels and upload your own. Built with ASP.NET Core.

## Features

### Channels: YouTube, BitChute & Custom

- **YouTube** — Subscribe to channels by URL; metadata and thumbnails are fetched via yt-dlp. Videos and playlists sync on a schedule.
- **BitChute** — Same workflow as YouTube: add a channel URL, and the app fetches metadata and supports downloading. Playlists are not yet supported, but watch this space...
- **Custom channels** — Create your own channels. Add playlists and videos manually, upload or copy files into the configured folder, and use the file system scan to import them.

The **Channels** page lists all channels (or “My Channels” for non-admins) with filters by platform (YouTube, BitChute, Custom) and view modes (banner or avatar grid). Add new channels via the **Add Channel** modal.

<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/_Misc/Screenshots/channel-index.png" alt="Channel Index" />

---

### Channel details

From a channel you can:

- **Videos** — Browse downloaded videos.
- **Available** — See all videos known from the platform (or custom playlists) that aren’t yet downloaded; search, ignore/unignore, and download selected or all (admin).
- **Playlists** — (YouTube) See all playlists for the channel, refresh the list from YouTube, subscribe to selected playlists or all (admin), and ignore playlists you don’t want. Subscribed playlists sync on a schedule.

<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/_Misc/Screenshots/channel-details.png" alt="Channel Details" />

---

### Tags for videos

- Tag videos for organisation and discovery. Tags can be **user-specific** or **global** (managed by admins).
- **Admin → Tags** — Create global tags that appear as suggestions for all users; consolidating tags with the same name into a global tag is supported.
- Use tags when **searching** (see below).

---

### File system scan

Manually copy video files into the app’s download folder and have the system import them automatically:

- **Regular channels:** Place files under `OutputPath/{ChannelId}/{VideoId}.ext` (e.g. `UCxxx/5eVq-W1BGlY.mp4`). The scan matches by channel and video ID and links or creates the video record.
- **Custom channels:** Place files under `OutputPath/_Custom/{ChannelId}/`. The scan enumerates files in that folder and imports them (file names can be flexible for Custom).

**Admin → Tools** provides **Scan File System** (all channels) and optional **Scan single channel** from the channel details page. Progress and results (imported, missing, etc.) are shown in the UI.

---

### Playlists: platform & custom

- **Platform playlists (YouTube)** — On a channel’s **Playlists** tab you see all playlists returned by the platform. Subscribe to the ones you want; only subscribed playlists are synced and shown in **Playlists** in the nav. You can ignore playlists to hide them from the list.
- **Custom playlists** — **My Playlists** lets you create **cross-channel playlists**: add any archived videos from any channel into your own playlists, reorder them, and optionally clone an existing playlist from a URL to copy its videos into a new custom playlist.

<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/_Misc/Screenshots/channel-playlist.png" alt="Channel Playlist" />

---

### Failed downloads (Admin)

Videos that fail to download (e.g. geo-blocked, removed, or network errors) are marked in the database. The **Admin** page has a **Failed Downloads** tab that lists them so you can:

- See title, channel, video ID, and platform.
- Re-queue for retry or remove from the list.
- If you obtain the file by other means: name it with the **Video ID** (e.g. `5eVq-W1BGlY.mp4`), put it in the correct channel folder under your download path, then run a **File System Scan** to import it.

<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/_Misc/Screenshots/admin.png" alt="Admin Page" />

---

### Dashboard: scheduled tasks (Hangfire)

Background jobs run on a schedule (channel sync, playlist sync, video downloads, metadata review, etc.). The **Hangfire Dashboard** at `/hangfire` (linked from **Admin → Monitoring & Diagnostics**) lets you:

- View recurring and one-off jobs.
- Inspect succeeded, failed, and processing jobs.
- Manually trigger or re-queue jobs as needed.

Access is restricted to authenticated admin users.

---

### Logging dashboard (Sejil)

Application logs are available in the [Sejil](https://github.com/alaatm/Sejil) log dashboard at `/sejil`, linked from **Admin → Monitoring & Diagnostics**. Use it to:

- Search and filter log entries by level, message, and time.
- Diagnose errors and trace behaviour of syncs, downloads, and file system scans.

<img src="https://github.com/gordon-matt/MyVideoArchive/blob/master/_Misc/Screenshots/sejil.png" alt="Sejil Log Dashboard" />

---

### Search videos by title, channel, or tags

The **Videos** page provides:

- **Search by title** — Text filter on video title.
- **Filter by channel** — Dropdown to restrict to one channel.
- **Filter by tags** — Tag-based filter to find videos with specific tags.

Results are paginated. Combined with tags and custom playlists, you can organise and find archived content quickly.

---

## Technology Stack

- **ASP.NET Core 10.0**
- **PostgreSQL** — Database
- **OData** — RESTful API for data access using Extenso.AspNetCore.OData
- **Entity Framework Core** — Data access with repository pattern using Extenso.Data.Entity
- **YoutubeDLSharp** — C# wrapper for yt-dlp video downloader
- **Hangfire** — Background jobs and dashboard (PostgreSQL storage)
- **Sejil** — Log dashboard (Serilog sink)
- **Knockout.js** — Frontend MVVM framework
- **Bootstrap 5** — Responsive UI
- **Autofac** — Dependency injection container

## Getting Started

Run the app as you would any standard .NET application. A Docker image is published to **GitHub Container Registry** for easy deployment.

### Running with the published image

The easiest way to use this app is with Docker. Here's what my stack looks like in Portainer:

```yaml
version: "3.9"

services:
  app:
    image: ghcr.io/gordon-matt/myvideoarchive:latest
    ports:
      - "7000:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=myvideoarchive;Username=postgres;Password=your_password"
      VideoDownload__VideoQuality: bestvideo[height=720]+bestaudio/bestvideo[height=720]+bestaudio/bestvideo[height=480]+bestaudio/best
      VideoDownload__OutputPath: /app/data
      YoutubeDL__ExecutablePath: /usr/local/bin/yt-dlp
      YoutubeDL__FFmpegPath: /usr/bin/ffmpeg
    volumes:
      - /volume1/docker/myvideoarchive/videos:/app/data
    networks:
      - postgres-net
    restart: unless-stopped

networks:
  postgres-net:
    external: true
```

You can add postgres directly in the stack if you prefer, but I find that adding a separate postgres install for every app that needs it gets messy and wastes resources. I use one postgres stack for all apps that need it and I include pgadmin with it, so I can manage the databases myself. Here's an example:

```yaml
version: "3.9"

services:
  db:
    image: postgres:18-alpine
    container_name: postgres
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: your_password
    volumes:
      - /volume1/docker/postgresql/data:/var/lib/postgresql:rw
    ports:
      - "7001:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5
    restart: unless-stopped
    networks:
      - postgres-net

  pgadmin:
    image: dpage/pgadmin4:latest
    container_name: pgadmin
    environment:
      PGADMIN_DEFAULT_EMAIL: your_email
      PGADMIN_DEFAULT_PASSWORD: your_password
    ports:
      - "7002:80"
    volumes:
      - /volume1/docker/postgresql/pgadmin:/var/lib/pgadmin:rw
      - /volume1/docker/postgresql/backups:/var/lib/pgadmin/storage/your_email_replace_at/backups:rw
    restart: unless-stopped
    networks:
      - postgres-net

networks:
  postgres-net:
    external: true
```

where `your_email_replace_at` is the `@` signed replaced by an underscore `_` for your email address. Example: `test_something.com` instead of `test@something.com`.

### Configuration

- **Local development:** Use **User Secrets** for the connection string (Visual Studio: right-click project → **Manage User Secrets**).

- **Docker:** The container reads settings from environment variables in `docker-compose.yml` or from a `.env` file.

- **Initial admin user:** On first run, an admin user is seeded. Configure via `SeedAdmin:Email` and `SeedAdmin:Password` (User Secrets or appsettings) or `SeedAdmin__Email` and `SeedAdmin__Password` (environment/Docker). Defaults: `admin@myvideoarchive.local` / `Admin@123`.

**NOTE:** The application will automatically download yt-dlp and ffmpeg binaries on first run.

---

## Did I use AI for this?

Yes—shamelessly. Without AI, this project would not exist. It is something I have wanted to build for years but never had the time. Life is busy: work, family, and everything in between. I am a senior developer and have been coding since the mid-2000s—I know what good code looks like and I know what sloppy code looks like. So yes: I used AI as a **productivity tool**, but the **architecture and decisions are mine**, and I am the one maintaining it.