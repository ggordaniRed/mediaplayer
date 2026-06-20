using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace MediaPlayer.Services;

public sealed class SoundFlowAudioService : IDisposable
{
    private const string PlayerDll = "/tmp/sf_player_proj/bin/Release/net10.0/sf_player.dll";
    private const int SpectrumBands = 64;

    private Process? _proc;
    private Thread? _readerThread;
    private volatile bool _disposed;
    private string? _currentFile;
    private float _lastVolume = 0.8f;

    private float[] _spectrumFront = new float[SpectrumBands];
    private float[] _spectrumBack = new float[SpectrumBands];
    private readonly float[] _smoothed = new float[SpectrumBands];
    public readonly float[] PeakHold = new float[SpectrumBands];
    private const float PeakDecay = 0.985f;
    private const float SmoothAttack = 0.4f;
    private const float SmoothRelease = 0.06f;
    public volatile bool HasSignal;

    public ReadOnlySpan<float> GetSpectrum() => _spectrumFront;

    public void Play(string filePath)
    {
        Stop();
        _currentFile = filePath;

        HasSignal = false;
        Array.Clear(_smoothed);
        Array.Clear(PeakHold);
        Array.Clear(_spectrumFront);
        Array.Clear(_spectrumBack);

        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        _proc.StartInfo.ArgumentList.Add(PlayerDll);
        _proc.StartInfo.ArgumentList.Add(filePath);

        try
        {
            _proc.Start();
            _readerThread = new Thread(ReadSpectrumLoop) { IsBackground = true, Name = "SF-Spectrum" };
            _readerThread.Start();
        }
        catch
        {
            _proc?.Dispose();
            _proc = null;
        }
    }

    /// <summary>Restart playback from the beginning (used after Stop + Play).</summary>
    public void Restart()
    {
        if (_currentFile != null) Play(_currentFile);
    }

    private void ReadSpectrumLoop()
    {
        try
        {
            while (_proc is { HasExited: false })
            {
                var line = _proc.StandardOutput.ReadLine();
                if (line == null) break;
                if (!line.StartsWith("SPECTRUM:")) continue;

                var parts = line.Substring(9).Split(',');
                for (int idx = 0; idx < parts.Length && idx < SpectrumBands; idx++)
                {
                    if (float.TryParse(parts[idx], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var v))
                    {
                        float norm = v > 0.001f
                            ? Math.Clamp((MathF.Log10(v) + 2f) / 3f, 0f, 1f)
                            : 0f;

                        if (norm > _smoothed[idx])
                            _smoothed[idx] += (norm - _smoothed[idx]) * SmoothAttack;
                        else
                            _smoothed[idx] += (norm - _smoothed[idx]) * SmoothRelease;

                        _spectrumBack[idx] = _smoothed[idx];

                        if (_smoothed[idx] > PeakHold[idx])
                            PeakHold[idx] = _smoothed[idx];
                        else
                            PeakHold[idx] *= PeakDecay;
                    }
                }

                (_spectrumFront, _spectrumBack) = (_spectrumBack, _spectrumFront);
                HasSignal = true;
            }
        }
        catch { }
        HasSignal = false;
    }

    public void Pause() => Send("PAUSE");
    public void Resume() => Send("RESUME");

    public void SetVolume(float vol)
    {
        _lastVolume = Math.Clamp(vol, 0f, 2f);
        Send($"VOL:{_lastVolume.ToString("F2", CultureInfo.InvariantCulture)}");
    }

    public void Mute() => Send("VOL:0.00");
    public void Unmute() => Send($"VOL:{_lastVolume.ToString("F2", CultureInfo.InvariantCulture)}");

    public void Seek(float fraction) =>
        Send($"SEEK:{Math.Clamp(fraction, 0f, 1f).ToString("F4", CultureInfo.InvariantCulture)}");

    public void SetEqBand(int band, float gainDb) =>
        Send($"EQ:{band},{gainDb.ToString("F1", CultureInfo.InvariantCulture)}");

    public void SetPreamp(float gainDb) =>
        Send($"PREAMP:{gainDb.ToString("F1", CultureInfo.InvariantCulture)}");

    public void Stop()
    {
        HasSignal = false;
        if (_proc == null) return;

        Send("STOP");
        try
        {
            if (!_proc.HasExited)
                _proc.WaitForExit(1000);
            if (!_proc.HasExited)
                _proc.Kill();
        }
        catch { }

        _proc.Dispose();
        _proc = null;
        _readerThread = null;
    }

    private void Send(string cmd)
    {
        try
        {
            if (_proc is { HasExited: false })
                _proc.StandardInput.WriteLine(cmd);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
