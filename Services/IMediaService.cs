using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

// Alias to avoid clash with our "MediaPlayer" root namespace.
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MediaPlayer.Services;

/// <summary>
/// Abstraction over the underlying media engine so ViewModels stay engine-agnostic.
/// All events are raised on background threads; callers must marshal to the UI thread.
/// </summary>
public interface IMediaService : IDisposable
{
    // ── Engine handles (needed by VideoView for native window embedding) ───────
    LibVLC    LibVlc  { get; }
    VlcPlayer Player  { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    event Action<float, long>     PositionChanged;
    event Action<VLCState>        StateChanged;
    event Action<TimeSpan,
                 IEnumerable<TrackDescription>,
                 IEnumerable<TrackDescription>> MediaOpened;
    event Action                  MediaEnded;
    event Action<string>          ErrorOccurred;

    // ── Transport ─────────────────────────────────────────────────────────────
    Task PlayAsync(Uri uri);
    void Pause();
    void Resume();
    void Stop();
    void Seek(float fraction);

    // ── Audio ─────────────────────────────────────────────────────────────────
    void SetVolume(int volume);
    void SetMuted(bool muted);
    void SetAudioTrack(int trackId);
    void SetSubtitleTrack(int trackId);

    // ── Equalizer ─────────────────────────────────────────────────────────────
    void ApplyEqualizer(float preampDb, float[] bandGainsDb);
    void DisableEqualizer();
}
