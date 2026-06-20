using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MediaPlayer.Services;

/// <summary>
/// Plays audio files using macOS AVFoundation (NSSound/afplay subprocess).
/// Guarantees audio goes through the system default output device.
/// Used as a fallback when VLC's auhal module doesn't route to the correct device.
/// </summary>
sealed class NativeAudioPlayer : IDisposable
{
    private System.Diagnostics.Process? _process;
    private volatile bool _disposed;

    public bool IsPlaying => _process is { HasExited: false };

    public void Play(string filePath, int volumePercent = 80)
    {
        Stop();
        float vol = Math.Clamp(volumePercent / 100f, 0f, 2f);
        _process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "afplay",
                ArgumentList = { filePath, "-v", vol.ToString("F2") },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        _process.Start();
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
        }
        _process = null;
    }

    public void SetVolume(int volumePercent)
    {
        // afplay doesn't support changing volume mid-stream.
        // Volume is set at Play() time.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
