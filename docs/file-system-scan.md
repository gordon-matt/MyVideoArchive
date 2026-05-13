# File system scan

This document describes how **MyVideoArchive** scans your configured download folder, links files to database records, and—**for Custom channels only**—how folder layout drives automatic **series** and **playlist** creation.

## Configuration and entry points

- **Download root** comes from `VideoDownload:OutputPath` in configuration.
- Scans run from **Admin → Tools** (**Scan File System** for all channels) or from a channel’s details page (**Scan single channel**).
- If `OutputPath` does not exist, the scan logs a warning and returns without processing folders.

The implementation lives in `FileSystemScanJob` in the **MyVideoArchive.Services** project.

---

## Platform-specific behavior

### Regular channels (e.g. YouTube, BitChute)

These channels use the **same identifiers as the hosting platform**. The app stores those IDs when it syncs metadata; your on-disk paths must use **exactly** those IDs so the scan can match files to the rows that already exist.

**Channel folder name**

- Use the platform’s **channel ID**, not the display name or slug.
- **YouTube example:** for a channel URL like  
  `https://www.youtube.com/channel/UCiEKDhv4v0YTc-ZtpABkXZA`  
  the channel ID is **`UCiEKDhv4v0YTc-ZtpABkXZA`**. That string is the folder name under `OutputPath` for that channel (i.e. `{OutputPath}/UCiEKDhv4v0YTc-ZtpABkXZA/`).
- **BitChute** (and other platforms) follow the same idea: the folder name is the channel identifier the platform and this app use for that channel.

**Video file name**

- The **filename without extension** must be the platform’s **video ID**.
- **YouTube example:** for  
  `https://www.youtube.com/watch?v=7Na4hhckdzE`  
  the video ID is **`7Na4hhckdzE`**, so the file must be named like **`7Na4hhckdzE.mp4`** (or another supported video extension). That matches the metadata already downloaded for that video.

**Layout and scan behavior**

- Expected layout: **`{OutputPath}/{ChannelId}/{VideoId}.{ext}`** — same `ChannelId` and `VideoId` as above.
- The scan **does not discover unknown files** by enumerating the folder. The **database is the source of truth**: for each video row, it either finds `ChannelId/VideoId.ext` on disk (trying common video extensions) or clears `FilePath` if the file disappeared.
- Supported extensions are the same as for Custom channels (see [Video extensions](#video-extensions)).

Use this when you manually place a file obtained elsewhere: name it with the correct **platform video ID**, put it under the **platform channel ID** folder, then run a scan.

### Custom channels

- Root folder: **`{OutputPath}/_Custom/{ChannelId}/`**
  - `ChannelId` here is the **custom channel’s ID string** (same segment used in paths), not necessarily the numeric database primary key.
- The scan **recursively enumerates** all video files under that folder. Any file not already tied to a tracked path is **imported**: a new video row is created with `VideoId` and default title set to the **filename without extension**.
- **Sidecar thumbnails**: an image next to the video with the same base name (`jpg`, `jpeg`, `png`, `webp`, `gif`) is stored as the video thumbnail URL.
- **Subtitles**: `.srt` files under the custom channel tree are converted to `.vtt` alongside the source (existing `.vtt` is left as-is).
- **Thumbnails**: if there is no sidecar image, the scan may generate a thumbnail from the video (e.g. via FFmpeg) when possible.

After videos are processed, the scan may create **series** and **playlist** records from subfolders (see below), then import **additional content** (non-video files) where applicable.

---

## Automatic series and playlists (Custom channels only)

Automatic grouping applies **only** under **`_Custom/{ChannelId}/`**. The scan chooses **one** of two layouts using detection rules:

1. **Series → playlist → videos** (two-level hierarchy), if that structure is detected.
2. Otherwise **playlist-only** (one-level hierarchy), if that structure is detected.

If neither matches, videos still import, but **no** automatic series/playlists are created from folder names.

### Layout A: Series and playlists (recommended for shows / seasons)

Use **two** nested folder levels under the custom channel folder:

- **Level 1** folder name → **Series** name (in the database for that channel).
- **Level 2** folder name → **Playlist** name, linked under that series.
- **Video files** go inside each level-2 folder (including nested subfolders within that playlist folder).

Example:

```text
_Custom/
  MyChannelId/
    Cooking Show/           ← Series
      Season 1/             ← Playlist (linked to “Cooking Show”)
        episode-01.mp4
        episode-02.mkv
      Season 2/               ← Another playlist under the same series
        ep-01.mp4
    Travel Diaries/         ← Another series
      Europe 2024/
        part-1.mp4
```

**Detection:** The folder is treated as “series layout” if there is **at least one** immediate subfolder that itself contains **at least one** non-reserved subfolder (see [Folders starting with `_`](#folders-starting-with-_)). So you need real **Series → Playlist** nesting, not only a single flat list of folders.

**Internals (for troubleshooting):**

- Playlist IDs are stable strings such as `{ChannelId}/{SeriesName}/{PlaylistFolderName}` so the same playlist title can exist under different series without colliding.
- Series and playlists are **created if missing** and linked (`SeriesPlaylist`). Videos are linked to playlists (`PlaylistVideo`) in **folder name sort order** for series and playlists; **video order** within a playlist follows the scan’s enumeration order.

### Layout B: Playlists only (no series)

Use **one** folder level under the custom channel folder: each immediate subfolder becomes a **standalone playlist** (no parent series).

Example:

```text
_Custom/
  MyChannelId/
    Behind the Scenes/      ← Playlist
      clip-a.mp4
    Highlights/             ← Playlist
      best-of.mp4
```

**Detection:** At least one immediate subfolder must:

- **Not** start with `_`.
- **Not** contain any non-reserved **subdirectories** (otherwise the scanner assumes you meant layout A or a mixed tree).
- Contain **at least one video file in that folder’s root** (not only in deeper subfolders)—this is how the scan decides the folder “looks like” a playlist leaf.

If your files sit only in nested directories under the playlist folder, the **playlist-only** detector may not activate; prefer **Layout A** or place at least one video file directly in the playlist folder.

**Playlist ID** shape: `{ChannelId}/{PlaylistFolderName}`.

### Folders starting with `_`

Any directory whose **name starts with `_`** is **ignored** for series/playlist discovery (for example `_extras`, `_temp`). Use this prefix for folders that should not become series or playlists.

---

## Additional content and extras

- **Non-video files** inside a custom channel tree (excluding known video/subtitle extensions and sidecar thumbnails next to videos) can be imported as **additional content**. When series/playlists were created in the same scan, files **inside a playlist folder** (or deeper subfolders) are associated with that **playlist**.
- **`_extras`:** Under each channel folder (`_Custom/...` or non-custom `ChannelId/`), a **`_extras`** subtree can hold extra files. Import rules differ slightly from playlist-folder scanning; subtitle extensions are skipped.

Place supplementary assets (PDFs, images, etc.) next to videos or under `_extras` as fits your workflow.

---

## Video extensions

Files with these extensions are treated as videos:

`.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.flv`, `.m4v`, `.wmv`

---

## Practical tips

1. **Custom imports:** Use descriptive **filenames**—they become the default **video ID** and title until you edit metadata.
2. **Platform channels:** Use each platform’s **channel ID** as the folder name and **video ID** as the filename (see examples above). Those IDs must match what is already in the database from sync—typically copied from the platform URL (`/channel/…`, `watch?v=…`, etc.).
3. **Organise Custom channels:** Prefer **Layout A** (`Series/Playlist/videos`) for long-running shows; use **Layout B** for simple topical folders.
4. **Reserved folders:** Prefix scratch or tooling folders with **`_`** so they never appear as series or playlists.
