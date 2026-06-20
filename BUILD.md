# Build & Run

## Prerequisites
- .NET 8 SDK
- VLC 3.x installed (or let the NuGet native packages supply the libraries)

## Restore & Build

```bash
# Restore packages
dotnet restore

# Build (native libraries resolved automatically by RID)
dotnet build -c Release -r osx-arm64   # macOS M-series
dotnet build -c Release -r win-x64     # Windows Intel
```

## Publish self-contained

```bash
dotnet publish -c Release -r osx-arm64 --self-contained -o publish/mac
dotnet publish -c Release -r win-x64   --self-contained -o publish/win
```

## Keyboard shortcuts

| Key | Action |
|---|---|
| Space | Play / Pause |
| ← / → | Previous / Next track |
| ↑ / ↓ | Volume +5 / −5 |
| M | Mute toggle |
| E | Toggle equalizer panel |
| V | Toggle visualizer |
| Ctrl+O | Open file picker |
| Drag & Drop | Add files to playlist |
