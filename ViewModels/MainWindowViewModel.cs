using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using MediaPlayer.Models;
using MediaPlayer.Services;
using MediaPlayerLib = LibVLCSharp.Shared.MediaPlayer;

namespace MediaPlayer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IMediaService     _media;
    private readonly AudioCaptureService _capture;

    // ── Child ViewModels ──────────────────────────────────────────────────────

    public PlaylistViewModel   Playlist   { get; }
    public EqualizerViewModel  Equalizer  { get; }

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty] private MediaItem?      _currentItem;
    [ObservableProperty] private bool            _isPlaying;
    [ObservableProperty] private bool            _isMuted;
    [ObservableProperty] private double          _volume = 80;
    [ObservableProperty] private double          _position;        // 0.0–1.0
    private bool _isSeeking;
    [ObservableProperty] private TimeSpan        _elapsed;
    [ObservableProperty] private TimeSpan        _duration;
    [ObservableProperty] private string          _statusText = "Ready";
    [ObservableProperty] private bool            _isVisualizerVisible = true;
    [ObservableProperty] private bool            _isAudioOnly;
    [ObservableProperty] private bool            _isEqualizerPanelOpen;

    // Audio/subtitle track lists populated when a media is opened.
    public ObservableCollection<TrackDescription> AudioTracks    { get; } = [];
    public ObservableCollection<TrackDescription> SubtitleTracks { get; } = [];

    [ObservableProperty] private TrackDescription? _selectedAudioTrack;
    [ObservableProperty] private TrackDescription? _selectedSubtitleTrack;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindowViewModel(IMediaService media, AudioCaptureService capture)
    {
        _media          = media;
        _capture        = capture;
        CaptureService  = capture;

        Playlist  = new PlaylistViewModel();
        Equalizer = new EqualizerViewModel(media);

        Playlist.PlayRequested += item => _ = PlayAsync(item);

        // Wire up service callbacks (all arrive on background threads → marshal to UI).
        _media.PositionChanged  += OnPositionChanged;
        _media.StateChanged     += OnStateChanged;
        _media.MediaOpened      += OnMediaOpened;
        _media.MediaEnded       += OnMediaEnded;
        _media.ErrorOccurred    += OnError;

        // Sync volume to engine on startup.
        OnVolumeChanged(Volume);
    }

    // ── Playback commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFiles(IStorageProvider? storage)
    {
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Open Media Files",
            AllowMultiple  = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Video & Audio")
                {
                    Patterns = ["*.mp4","*.mkv","*.avi","*.mov","*.flac","*.mp3","*.aac","*.ogg","*.wav"]
                }
            ]
        });

        var paths = files.Select(f => f.Path.LocalPath);
        Playlist.AddFiles(paths);

        if (CurrentItem is null && Playlist.Items.Count > 0)
            await PlayAsync(Playlist.Items[0]);
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        if (CurrentItem is null && Playlist.Items.Count > 0)
        {
            await PlayAsync(Playlist.Items[0]);
            return;
        }

        if (IsPlaying)
            _media.Pause();
        else if (CurrentItem is not null && StatusText is "Stopped" or "Ready")
            await PlayAsync(CurrentItem);
        else
            _media.Resume();
    }

    [RelayCommand]
    private async Task Next()
    {
        if (CurrentItem is null) return;
        var next = Playlist.GetNext(CurrentItem);
        if (next is not null) await PlayAsync(next);
    }

    [RelayCommand]
    private async Task Previous()
    {
        if (CurrentItem is null) return;
        // If past 3 s, restart current; otherwise go to previous.
        if (Elapsed.TotalSeconds > 3)
        {
            _media.Seek(0);
            return;
        }
        var prev = Playlist.GetPrevious(CurrentItem);
        if (prev is not null) await PlayAsync(prev);
    }

    [RelayCommand]
    private void Stop()
    {
        _media.Stop();
        IsPlaying = false;
        StatusText = "Stopped";
    }

    // Exposed so the VideoView code-behind can pass the native window handle.
    public LibVLC?            LibVlcInstance  => _media.LibVlc;
    public MediaPlayerLib?    VlcPlayer       => _media.Player;
    public AudioCaptureService CaptureService  { get; }
    public SoundFlowAudioService? SoundFlowAudio => (_media as VlcMediaService)?.SoundFlowAudio;

    // ── Track selection ───────────────────────────────────────────────────────

    partial void OnSelectedAudioTrackChanged(TrackDescription? value)
    {
        if (value.HasValue) _media.SetAudioTrack(value.Value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackDescription? value)
    {
        if (value.HasValue) _media.SetSubtitleTrack(value.Value.Id);
    }

    // ── Volume / Seek ─────────────────────────────────────────────────────────

    partial void OnVolumeChanged(double value)
    {
        _media.SetVolume((int)value);
        _capture.SetVolume((int)value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        _media.SetMuted(value);
        _capture.SetMuted(value);
    }

    [RelayCommand]
    private void SeekTo(double fraction) => _media.Seek((float)fraction);

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek(double fraction)
    {
        _media.Seek((float)fraction);
        _isSeeking = false;
    }

    // ── UI toggles ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool            _isShaderMode;
    [RelayCommand] private void ToggleVisualizer()     => IsVisualizerVisible = !IsVisualizerVisible;
    [RelayCommand] private void ToggleShaderMode()     => IsShaderMode = !IsShaderMode;
    [RelayCommand] private void ToggleEqualizerPanel() => IsEqualizerPanelOpen = !IsEqualizerPanelOpen;
    [RelayCommand] private void ToggleMute()           => IsMuted = !IsMuted;

    public event Action? SkinToggleRequested;
    [RelayCommand] private void ToggleSkin() => SkinToggleRequested?.Invoke();

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task PlayAsync(MediaItem item)
    {
        CurrentItem = item;
        Playlist.SelectedItem = item;
        StatusText = $"Loading: {item.Title}";

        await _media.PlayAsync(item.Uri);
        IsPlaying = true;
    }

    // ── Service callbacks (invoked on background threads) ─────────────────────

    private void OnPositionChanged(float pos, long elapsedMs)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_isSeeking)
                Position = pos;
            Elapsed = TimeSpan.FromMilliseconds(elapsedMs);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnStateChanged(VLCState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (state)
            {
                case VLCState.Playing:
                    IsPlaying = true;
                    StatusText = CurrentItem?.Title ?? "Playing";
                    break;
                case VLCState.Paused:
                    IsPlaying = false;
                    StatusText = "Paused";
                    break;
                case VLCState.Error:
                    IsPlaying = false;
                    StatusText = "Error";
                    break;
                // Don't handle Stopped here — Stop() command already sets state.
                // Don't handle Buffering — it flickers constantly.
            }
        });
    }

    private void OnMediaOpened(TimeSpan totalDuration,
                               IEnumerable<TrackDescription> audio,
                               IEnumerable<TrackDescription> subs)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Duration = totalDuration;

            AudioTracks.Clear();
            foreach (var t in audio) AudioTracks.Add(t);
            SelectedAudioTrack = AudioTracks.FirstOrDefault();

            SubtitleTracks.Clear();
            foreach (var t in subs) SubtitleTracks.Add(t);

            IsAudioOnly = _media.Player.VideoTrack == -1;
        });
    }

    private void OnMediaEnded()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (CurrentItem is not null)
            {
                var next = Playlist.GetNext(CurrentItem);
                if (next is not null) await PlayAsync(next);
                else StatusText = "Playlist finished";
            }
        });
    }

    private void OnError(string msg)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = $"Error: {msg}");
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _media.PositionChanged  -= OnPositionChanged;
        _media.StateChanged     -= OnStateChanged;
        _media.MediaOpened      -= OnMediaOpened;
        _media.MediaEnded       -= OnMediaEnded;
        _media.ErrorOccurred    -= OnError;
        _media.Dispose();
    }
}

