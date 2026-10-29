# YouTubeDL

A desktop YouTube downloader for Windows built with .NET 10 and Avalonia UI.

YouTubeDL downloads video and audio, handles playlists, keeps a persistent download queue, and provides both a graphical interface and a headless command-line mode.

The InnerTube/HTTP layer is provided by **[AvaRuk/YouTubeCulvert](https://github.com/AvaRuk/YouTubeCulvert)**. A working copy of the library is vendored in [`YoutubeCulvert/`](YoutubeCulvert/) so the solution builds without additional setup.

## Features

- **Video and audio downloads** - MP4, WebM and MKV video; M4A, MP3, Opus and FLAC audio.
- **Format and quality picker** - one combined list, video formats first, then audio formats.
- **Playlists** - load a playlist, select individual videos, override options per item, and save everything into a subfolder named after the playlist.
- **Queue** - pause, resume, cancel and retry downloads; configurable concurrency; queue restored after restart.
- **Automatic type detection** - video and playlist URLs are recognised from the link itself, including `watch?v=...&list=...` and `playlist?list=...&si=...` links.
- **Optional sign-in** - Google device-code flow or a browser cookie header for private and age-restricted content.
- **Download artifacts** - subtitles, thumbnails, metadata embedding, descriptions, JSON metadata and chapters.
- **Live settings** - changes are applied and saved automatically; no separate Save button.
- **Appearance** - light, dark and system themes, English and Russian localisation, acrylic window background.
- **Automation** - watch folder, clipboard link detection, scheduled download windows.
- **Headless CLI** - the same download engine can be used without opening the UI.

## Built with

- [.NET 10](https://dotnet.microsoft.com/)
- [Avalonia UI 12](https://avaloniaui.net/)
- [AvaRuk/YouTubeCulvert](https://github.com/AvaRuk/YouTubeCulvert)
- [FFmpeg](https://ffmpeg.org/) (optional, required for muxing and audio conversion)

## Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source
- FFmpeg for merging separate video/audio streams and converting audio

FFmpeg can be installed from the Settings page, or an existing `ffmpeg.exe` path can be configured manually.

## Build and run

```powershell
git clone https://github.com/AvaRuk/YouTubeDL.git
cd YouTubeDL

dotnet build YouTubeDL.slnx -c Release
dotnet run --project YouTubeDL/YouTubeDL.csproj
```

The built executable is located at:

```text
YouTubeDL/bin/Release/net10.0/YouTubeDL.exe
```

To run the debug build:

```powershell
dotnet run --project YouTubeDL/YouTubeDL.csproj
```

## Command-line usage

The application switches to headless mode when the first argument is `download`, `dl`, `--cli` or `/download`.

```text
YouTubeDL.exe download --url <url> [--url <url2>]
                       [--audio] [--quality 1080] [--format mp4]
                       [--track <audio-track-id>]
                       [--output <folder>]
                       [--playlist]
                       [--cookie <header>] [--cookie-file <path>]
```

### Examples

Download a video at 1080p:

```powershell
YouTubeDL.exe download --url "https://www.youtube.com/watch?v=dQw4w9WgXcQ" --quality 1080 --format mp4
```

Download audio as MP3:

```powershell
YouTubeDL.exe download --url "https://www.youtube.com/watch?v=dQw4w9WgXcQ" --audio --format mp3
```

Download a playlist as audio:

```powershell
YouTubeDL.exe download --playlist --url "https://www.youtube.com/playlist?list=PL..." --audio
```

Save into a specific folder:

```powershell
YouTubeDL.exe download --url "https://youtu.be/dQw4w9WgXcQ" --output "D:\Downloads\YouTube"
```

Sign in with a cookie header for the current run:

```powershell
YouTubeDL.exe download --url "https://www.youtube.com/watch?v=..." --cookie "SAPISID=...; SID=...; HSID=..."
```

Options:

| Option | Description |
| --- | --- |
| `--url`, `-u` | Video, playlist or channel URL. May be repeated. |
| `--audio`, `-a` | Download audio instead of video. |
| `--quality`, `-q` | Preferred video quality, for example `1080`. |
| `--format`, `-f` | Preferred container, for example `mp4`, `webm`, `mkv`, `mp3`, `m4a`, `opus`, `flac`. |
| `--track`, `-t` | Preferred audio track ID. |
| `--output`, `-o` | Output directory for this run. |
| `--playlist` | Treat the URL as a playlist and download its videos. |
| `--cookie` | Cookie header used for this run. |
| `--cookie-file` | Read a cookie header from a browser-exported cookie file. |

## Data and configuration

All runtime data is stored in a `data` folder next to the executable:

```text
data/
|-- settings.json        Application settings
|-- queue.json           Persisted download queue
|-- history.json         Completed downloads (used for duplicate detection)
|-- diagnostics.log      Diagnostic log
|-- cookies.txt          Optional cookie file for sign-in
|-- languages/           Localisation files (en.json, ru.json)
|-- profiles/            Account/session data
`-- tools/               Downloaded tools such as FFmpeg
```

Deleting `data/settings.json` resets the application settings to their defaults.

The default download folders are:

- Videos: `%USERPROFILE%\Videos\YouTubeDL`
- Music: `%USERPROFILE%\Music\YouTubeDL`

Both can be changed on the Settings page and are saved automatically.

## Project structure

```text
YouTubeDL.slnx
|-- YouTubeDL/                  Avalonia desktop application
|   |-- Models/                 Settings, requests, queue and history models
|   |-- Services/               Download, playlist, queue, FFmpeg and automation services
|   |-- ViewModels/             MVVM view models
|   |-- data/                   Embedded and copied runtime data
|   |-- App.axaml               Application resources and theme dictionaries
|   `-- MainWindow.axaml        Main window and modal dialogs
`-- YoutubeCulvert/             Vendored InnerTube/HTTP library
    |-- src/YoutubeCulvert.Abstractions/
    `-- src/YoutubeCulvert/
```

The `YoutubeCulvert` folder is a vendored copy of the library published at **[github.com/AvaRuk/YouTubeCulvert](https://github.com/AvaRuk/YouTubeCulvert)**. Update it from the upstream repository when a newer library version is required.

## Notes

- YouTube changes its web client frequently. A failed request is usually an upstream change rather than a local settings problem; update `YoutubeCulvert` first.
- Some videos are region-, age- or account-restricted. Signing in may be required.
- This project is not affiliated with YouTube or Google.
- Download only content you have the rights to use and follow the terms of service of the platforms you access.

## Links

- Application repository: [AvaRuk/YouTubeDL](https://github.com/AvaRuk/YouTubeDL)
- InnerTube/HTTP library: [AvaRuk/YouTubeCulvert](https://github.com/AvaRuk/YouTubeCulvert)
- UI framework: [Avalonia UI](https://avaloniaui.net/)
