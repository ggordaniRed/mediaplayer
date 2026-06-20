using System;
using System.IO;

namespace MediaPlayer.Models;

/// <summary>
/// Represents a single playable item in the playlist.
/// Immutable by design — the playlist ViewModel wraps it in an ObservableObject if
/// mutable state (e.g. "currently playing" flag) is needed at the VM layer.
/// </summary>
public sealed record MediaItem(string FilePath)
{
    public string Title    { get; } = Path.GetFileNameWithoutExtension(FilePath);
    public string FileName { get; } = Path.GetFileName(FilePath);
    public string Extension{ get; } = Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();

    // Media URI understood by LibVLC (handles spaces & unicode correctly).
    public Uri Uri { get; } = new Uri(FilePath);

    public bool IsValid => File.Exists(FilePath);

    public override string ToString() => Title;
}
