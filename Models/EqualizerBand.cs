using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaPlayer.Models;

/// <summary>
/// One band of the graphic equalizer.
/// Gain is in dB; LibVLC accepts values in the range [-20, +20].
/// </summary>
public sealed partial class EqualizerBand : ObservableObject
{
    [ObservableProperty] private float _gain; // dB

    public uint   Index     { get; }
    public float  Frequency { get; } // Hz
    public string Label     { get; } // e.g. "100 Hz", "1 kHz"

    public EqualizerBand(uint index, float frequency)
    {
        Index     = index;
        Frequency = frequency;
        Label     = frequency >= 1000f
            ? $"{frequency / 1000f:0.#} kHz"
            : $"{frequency:0} Hz";
    }
}
