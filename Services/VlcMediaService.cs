using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MediaPlayer.Services;

public sealed class VlcMediaService : IMediaService
{
    [DllImport("libc")] private static extern int setenv(
        [MarshalAs(UnmanagedType.LPStr)] string name,
        [MarshalAs(UnmanagedType.LPStr)] string value,
        int overwrite);

    public LibVLC   LibVlc  { get; }
    public VlcPlayer Player  { get; }

    private Media?     _currentMedia;
    private string?    _currentFilePath;
    private float      _volumePercent = 80f;

    private readonly AudioCaptureService   _capture;
    private readonly SoundFlowAudioService _sfAudio;

    public SoundFlowAudioService SoundFlowAudio => _sfAudio;

    public event Action<float, long>? PositionChanged;
    public event Action<VLCState>?    StateChanged;
    public event Action<TimeSpan,
                        IEnumerable<TrackDescription>,
                        IEnumerable<TrackDescription>>? MediaOpened;
    public event Action?              MediaEnded;
    public event Action<string>?      ErrorOccurred;

    public VlcMediaService(AudioCaptureService capture, SoundFlowAudioService sfAudio)
    {
        _capture = capture;
        _sfAudio = sfAudio;

        if (System.OperatingSystem.IsMacOS())
        {
            var appDir = System.AppContext.BaseDirectory;
            var localPlugins = System.IO.Path.Combine(appDir, "plugins");
            var pluginPath = System.IO.Directory.Exists(localPlugins)
                ? localPlugins
                : "/Applications/VLC.app/Contents/MacOS/plugins";
            setenv("VLC_PLUGIN_PATH", pluginPath, 1);

            // Delete stale plugin cache so VLC rebuilds it cleanly (no error spam)
            var cacheFile = System.IO.Path.Combine(localPlugins, "plugins.dat");
            if (System.IO.File.Exists(cacheFile))
                try { System.IO.File.Delete(cacheFile); } catch { }

            LibVLCSharp.Shared.Core.Initialize(appDir);
        }

        LibVlc = new LibVLC(enableDebugLogs: false, "--quiet", "--no-video-title-show");
        Player  = new VlcPlayer(LibVlc);

        WirePlayerEvents();
    }

    public async Task PlayAsync(Uri uri)
    {
        Player.Stop();
        _sfAudio.Stop();
        _currentMedia?.Dispose();
        _currentMedia = new Media(LibVlc, uri);
        _currentFilePath = uri.IsFile ? uri.LocalPath : null;

        // Dummy audio callback so VLC's clock advances at the correct rate
        _capture.AttachToPlayer(Player);

        Player.Media = _currentMedia;
        Player.Play();

        // SoundFlow handles actual audio output
        if (_currentFilePath != null)
            _sfAudio.Play(_currentFilePath);
    }

    public void Pause()
    {
        Player.Pause();
        _sfAudio.Pause();
    }

    public void Resume()
    {
        Player.Play();
        _sfAudio.Resume();
    }

    public void Stop()
    {
        Player.Stop();
        _sfAudio.Stop();
    }

    public void Seek(float fraction)
    {
        Player.Position = fraction;
        _sfAudio.Seek(fraction);
    }

    public void SetVolume(int volume)
    {
        _volumePercent = Math.Clamp(volume, 0, 200);
        Player.Volume = (int)_volumePercent;
        _sfAudio.SetVolume(_volumePercent / 100f);
    }

    public void SetMuted(bool muted)
    {
        if (muted) _sfAudio.Mute();
        else _sfAudio.Unmute();
    }

    public void SetAudioTrack(int trackId)    => Player.SetAudioTrack(trackId);
    public void SetSubtitleTrack(int trackId) => Player.SetSpu(trackId);

    public void ApplyEqualizer(float preampDb, float[] bandGainsDb)
    {
        _sfAudio.SetPreamp(preampDb);
        for (int i = 0; i < bandGainsDb.Length; i++)
            _sfAudio.SetEqBand(i, bandGainsDb[i]);
    }

    public void DisableEqualizer()
    {
        for (int i = 0; i < 10; i++)
            _sfAudio.SetEqBand(i, 0f);
        _sfAudio.SetPreamp(0f);
    }

    private void WirePlayerEvents()
    {
        Player.TimeChanged += (_, e) => PositionChanged?.Invoke(Player.Position, e.Time);

        Player.Playing  += (_, _) =>
        {
            StateChanged?.Invoke(VLCState.Playing);
            PublishTrackInfo();
        };
        Player.Paused   += (_, _) => StateChanged?.Invoke(VLCState.Paused);
        Player.Stopped  += (_, _) => StateChanged?.Invoke(VLCState.Stopped);
        // Don't forward Buffering — it overwrites the track title constantly
        Player.EndReached += (_, _) => MediaEnded?.Invoke();
        Player.EncounteredError += (_, _) =>
        {
            StateChanged?.Invoke(VLCState.Error);
            ErrorOccurred?.Invoke("VLC encountered a playback error.");
        };
    }

    private void PublishTrackInfo()
    {
        if (_currentMedia is null) return;

        var duration = TimeSpan.FromMilliseconds(Player.Length);
        var audio    = Player.AudioTrackDescription
                             .Select(t => new TrackDescription(t.Id, t.Name))
                             .ToList();
        var subs     = Player.SpuDescription
                             .Select(t => new TrackDescription(t.Id, t.Name))
                             .ToList();

        MediaOpened?.Invoke(duration, audio, subs);
    }

    public void Dispose()
    {
        Player.Stop();
        _sfAudio.Stop();
        _capture.DetachFromPlayer(Player);
        _currentMedia?.Dispose();
        Player.Dispose();
        LibVlc.Dispose();
    }
}
