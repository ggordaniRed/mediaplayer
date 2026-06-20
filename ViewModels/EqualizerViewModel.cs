using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaPlayer.Models;
using MediaPlayer.Services;

namespace MediaPlayer.ViewModels;

public partial class EqualizerViewModel : ViewModelBase
{
    private readonly IMediaService _media;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private float _preamp; // dB, range [-20, +20]

    public ObservableCollection<EqualizerBand> Bands { get; } = [];

    // ── Standard 10-band frequencies (ISO 1/3-octave subset) ─────────────────

    private static readonly float[] BandFrequencies =
        [32f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f];

    public EqualizerViewModel(IMediaService media)
    {
        _media = media;

        for (uint i = 0; i < BandFrequencies.Length; i++)
        {
            var band = new EqualizerBand(i, BandFrequencies[i]);
            band.PropertyChanged += (_, _) => ApplyToEngine();
            Bands.Add(band);
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsEnabled) or nameof(Preamp))
                ApplyToEngine();
        };
    }

    // ── Apply current EQ state to LibVLC in real-time ─────────────────────────

    private void ApplyToEngine()
    {
        if (IsEnabled)
            _media.ApplyEqualizer(Preamp, [.. Bands.Select(b => b.Gain)]);
        else
            _media.DisableEqualizer();
    }

    // ── Presets ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void LoadPreset(string name)
    {
        float[] gains = name switch
        {
            "Rock"     => [-2, 0,  2,  4,  3,  0, -1, -1,  2,  3],
            "Pop"      => [-1, 2,  4,  5,  3,  0, -1,  0,  2,  2],
            "Jazz"     => [ 3, 2,  1,  2,  0, -1, -1,  0,  2,  3],
            "Classical"=> [ 0, 0,  0,  0,  0,  0, -5, -5, -5, -7],
            "Bass"     => [ 8, 6,  4,  2,  0,  0,  0,  0,  0,  0],
            "Treble"   => [ 0, 0,  0,  0,  0,  2,  4,  6,  8,  8],
            _          => new float[10]                             // flat
        };

        for (int i = 0; i < Bands.Count && i < gains.Length; i++)
            Bands[i].Gain = gains[i];
    }

    [RelayCommand]
    private void Reset()
    {
        Preamp = 0;
        foreach (var b in Bands) b.Gain = 0;
    }
}
