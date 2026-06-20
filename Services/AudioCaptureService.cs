using System;
using System.Threading;

// Alias required because our project namespace is "MediaPlayer", which clashes
// with the LibVLCSharp.Shared.MediaPlayer class name.
using VlcPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MediaPlayer.Services;

/// <summary>
/// Intercepts raw PCM audio from LibVLC via its audio-callback API and
/// continuously produces FFT magnitude spectra on a dedicated background thread.
///
/// Threading model:
///   VLC audio thread  → writes PCM into a lock-free ring buffer
///   FFT worker thread → reads ring buffer, computes FFT, writes _spectrum
///   UI/render thread  → reads _spectrum (volatile snapshot) via GetSpectrum()
///
/// The ring buffer + volatile snapshot avoids any locks on the hot audio path.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const uint   SampleRate    = 44100;
    private const uint   Channels      = 2;                            // stereo interleaved
    private const int    BytesPerSample = 2;                           // S16N = 16-bit signed
    private const int    RingFrames    = FftProcessor.DefaultFftSize * 8; // ring size in frames
    private const int    FftSize       = FftProcessor.DefaultFftSize;
    public  const int    SpectrumBands = FftSize / 2;

    // ── Ring buffer (single-producer / single-consumer, thread-safe without locks) ─

    // Interleaved stereo float32; array length = frames × channels.
    private readonly float[] _ring     = new float[RingFrames * (int)Channels];
    private volatile int     _writePos; // frame index; only written by VLC audio thread
    private volatile int     _readPos;  // frame index; only written by FFT worker thread

    // ── FFT ───────────────────────────────────────────────────────────────────

    private readonly FftProcessor _fft;
    private readonly float[]      _rawSpectrum = new float[SpectrumBands];

    // Double-buffer: FFT thread writes _spectrumBack; UI thread reads _spectrumFront.
    private float[] _spectrumFront = new float[SpectrumBands];
    private float[] _spectrumBack  = new float[SpectrumBands];

    // Peak-hold state for visualizer bar animation.
    public readonly float[] PeakHold = new float[SpectrumBands];
    private const float     PeakDecay = 0.96f;

    // Smoothing: exponential moving average.
    private readonly float[] _smoothed = new float[SpectrumBands];
    private const float      SmoothAlpha = 0.35f;

    // ── Worker thread ─────────────────────────────────────────────────────────

    private readonly Thread                  _worker;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool                    _hasSignal;

    // Volume/mute are handled by SoundFlowAudioService — these are no-ops for API compat
    public void SetVolume(int vlcVolume) { }
    public void SetMuted(bool muted) { }

    // ── VLC callback delegates (kept as fields to pin them from GC) ───────────

    private VlcPlayer.LibVLCAudioPlayCb?    _playCallback;
    private VlcPlayer.LibVLCAudioPauseCb?   _pauseCallback;
    private VlcPlayer.LibVLCAudioResumeCb?  _resumeCallback;
    private VlcPlayer.LibVLCAudioFlushCb?   _flushCallback;
    private VlcPlayer.LibVLCAudioDrainCb?   _drainCallback;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AudioCaptureService(FftProcessor fft)
    {
        _fft = fft;

        _worker = new Thread(FftLoop)
        {
            IsBackground = true,
            Priority     = ThreadPriority.AboveNormal,
            Name         = "FFT-Worker"
        };
        _worker.Start(_cts.Token);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Latest magnitude spectrum snapshot. Safe to call from any thread.
    /// Values are normalised to [0, 1].
    /// </summary>
    public ReadOnlySpan<float> GetSpectrum() => _spectrumFront;

    // ── VLC attachment / detachment ───────────────────────────────────────────

    public void AttachToPlayer(VlcPlayer player)
    {
        // Dummy S16N callback — VLC decodes audio and sends it here so its internal
        // clock advances at the correct rate. We discard the data (SoundFlow plays audio).
        // Without this, VLC with no audio output races to EndReached instantly.
        player.SetAudioFormat("S16N", SampleRate, Channels);

        _playCallback   = (IntPtr d, IntPtr s, uint c, long p) => { };
        _pauseCallback  = (IntPtr d, long p) => { };
        _resumeCallback = (IntPtr d, long p) => { };
        _flushCallback  = (IntPtr d, long p) => { };
        _drainCallback  = (IntPtr d) => { };

        player.SetAudioCallbacks(_playCallback, _pauseCallback,
                                 _resumeCallback, _flushCallback, _drainCallback);
    }

    public void DetachFromPlayer(VlcPlayer player)
    {
        VlcPlayer.LibVLCAudioPlayCb noop = (d, s, c, p) => {};
        VlcPlayer.LibVLCAudioPauseCb noopP = (d, p) => {};
        VlcPlayer.LibVLCAudioResumeCb noopR = (d, p) => {};
        VlcPlayer.LibVLCAudioFlushCb noopF = (d, p) => {};
        VlcPlayer.LibVLCAudioDrainCb noopD = (d) => {};
        try { player.SetAudioCallbacks(noop, noopP, noopR, noopF, noopD); } catch { }
    }

    // ── Ring buffer ───────────────────────────────────────────────────────────

    private void WriteRing(ReadOnlySpan<float> src)
    {
        int ch         = (int)Channels;
        int frameCount = src.Length / ch;
        for (int f = 0; f < frameCount; f++)
        {
            int ringBase = _writePos * ch;
            for (int c = 0; c < ch; c++)
                _ring[ringBase + c] = src[f * ch + c];
            _writePos = (_writePos + 1) % RingFrames;
        }
    }

    private void ResetRing()
    {
        _writePos = 0;
        _readPos  = 0;
        _ring.AsSpan().Clear();
    }

    // ── FFT worker loop ───────────────────────────────────────────────────────

    private void FftLoop(object? state)
    {
        var ct       = (CancellationToken)state!;
        int ch       = (int)Channels;
        var fftInput = new float[FftSize * ch];

        while (!ct.IsCancellationRequested)
        {
            if (!_hasSignal)
            {
                Thread.Sleep(16);
                continue;
            }

            int available = AvailableFrames();
            if (available < FftSize)
            {
                Thread.Sleep(5);
                continue;
            }

            ReadRing(fftInput.AsSpan(), FftSize);
            _fft.ComputeMagnitudeSpectrum(fftInput.AsSpan(), _rawSpectrum.AsSpan());

            for (int i = 0; i < SpectrumBands; i++)
            {
                float v = _rawSpectrum[i];
                _smoothed[i] = v > _smoothed[i]
                    ? _smoothed[i] + (v - _smoothed[i]) * SmoothAlpha
                    : _smoothed[i] * (1f - SmoothAlpha * 0.3f);

                float norm = Math.Clamp(_smoothed[i] * 6f, 0f, 1f);
                _spectrumBack[i] = norm;

                if (norm > PeakHold[i]) PeakHold[i] = norm;
                else                    PeakHold[i] *= PeakDecay;
            }

            // Swap front/back (pointer exchange is atomic on CLR).
            (_spectrumFront, _spectrumBack) = (_spectrumBack, _spectrumFront);
        }
    }

    private int AvailableFrames()
    {
        int w = _writePos, r = _readPos;
        return w >= r ? w - r : RingFrames - r + w;
    }

    private void ReadRing(Span<float> dst, int frames)
    {
        int ch = (int)Channels;
        for (int f = 0; f < frames; f++)
        {
            int pos = (_readPos + f) % RingFrames;
            for (int c = 0; c < ch; c++)
                dst[f * ch + c] = _ring[pos * ch + c];
        }
        _readPos = (_readPos + frames) % RingFrames;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _worker.Join(500);
        _cts.Dispose();
    }
}
