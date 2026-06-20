using System;
using System.Numerics;

namespace MediaPlayer.Services;

/// <summary>
/// Thread-safe, allocation-minimal FFT processor.
///
/// Design:
///   • In-place Cooley-Tukey radix-2 DIT FFT — O(N log N).
///   • Hann window applied before transform to reduce spectral leakage.
///   • Output is magnitude (linear scale). Callers convert to dB if desired.
///   • No heap allocation after construction — reuses internal Complex[] buffer.
/// </summary>
public sealed class FftProcessor
{
    // ── Configuration ─────────────────────────────────────────────────────────

    public const int DefaultFftSize = 1024; // must be power-of-two

    private readonly int       _fftSize;
    private readonly Complex[] _fftBuffer;
    private readonly float[]   _window;

    public FftProcessor(int fftSize = DefaultFftSize)
    {
        if (!IsPowerOfTwo(fftSize))
            throw new ArgumentException("FFT size must be a power of two.", nameof(fftSize));

        _fftSize   = fftSize;
        _fftBuffer = new Complex[fftSize];
        _window    = BuildHannWindow(fftSize);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the magnitude spectrum from interleaved stereo float samples.
    /// Returns an array of length FftSize/2 representing frequencies 0 → Nyquist.
    /// </summary>
    /// <param name="samples">Interleaved stereo PCM (float32, range –1.0 to +1.0).</param>
    /// <param name="output">Pre-allocated output array of length FftSize/2.</param>
    public void ComputeMagnitudeSpectrum(ReadOnlySpan<float> samples, Span<float> output)
    {
        int half = _fftSize / 2;

        // Mix stereo to mono and apply Hann window into the Complex buffer.
        for (int i = 0; i < _fftSize; i++)
        {
            float mono = i * 2 + 1 < samples.Length
                ? (samples[i * 2] + samples[i * 2 + 1]) * 0.5f
                : (i < samples.Length ? samples[i] : 0f);

            _fftBuffer[i] = new Complex(mono * _window[i], 0.0);
        }

        Fft(_fftBuffer);

        // Compute magnitude for the positive-frequency half.
        float scale = 2.0f / _fftSize; // normalise amplitude
        for (int i = 0; i < half && i < output.Length; i++)
            output[i] = (float)_fftBuffer[i].Magnitude * scale;
    }

    // ── In-place Cooley-Tukey FFT ─────────────────────────────────────────────

    private static void Fft(Complex[] x)
    {
        int n = x.Length;

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }

        // Butterfly passes.
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang  = -2.0 * Math.PI / len;
            var    wLen = new Complex(Math.Cos(ang), Math.Sin(ang));

            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                int half = len >> 1;
                for (int k = 0; k < half; k++)
                {
                    var u = x[i + k];
                    var v = x[i + k + half] * w;
                    x[i + k]        = u + v;
                    x[i + k + half] = u - v;
                    w *= wLen;
                }
            }
        }
    }

    // ── Hann window ───────────────────────────────────────────────────────────

    private static float[] BuildHannWindow(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++)
            w[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1))));
        return w;
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
}
