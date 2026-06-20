using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using MediaPlayer.Services;
using SkiaSharp;

namespace MediaPlayer.Controls;

/// <summary>
/// Custom Avalonia control that renders a retro Winamp-style spectrum analyser
/// directly on the Skia canvas — bypassing the Avalonia shape layer entirely.
///
/// Threading:
///   • A DispatcherTimer fires every ~16 ms on the UI thread and calls
///     InvalidateVisual(), which schedules a Render() call.
///   • Render() posts a custom draw op to the Avalonia render thread (separate
///     from the UI thread). The draw op calls into Skia on the render thread.
///   • AudioCaptureService.GetSpectrum() is safe to call from the render thread
///     because the FFT worker only swaps a reference (atomic pointer exchange).
/// </summary>
public sealed class VisualizerControl : Control
{
    // ── Dependency properties ─────────────────────────────────────────────────

    public static readonly StyledProperty<AudioCaptureService?> CaptureServiceProperty =
        AvaloniaProperty.Register<VisualizerControl, AudioCaptureService?>(nameof(CaptureService));

    public AudioCaptureService? CaptureService
    {
        get => GetValue(CaptureServiceProperty);
        set => SetValue(CaptureServiceProperty, value);
    }

    public static readonly StyledProperty<SoundFlowAudioService?> SoundFlowProperty =
        AvaloniaProperty.Register<VisualizerControl, SoundFlowAudioService?>(nameof(SoundFlow));

    public SoundFlowAudioService? SoundFlow
    {
        get => GetValue(SoundFlowProperty);
        set => SetValue(SoundFlowProperty, value);
    }

    // ── Styled properties (configurable from XAML/styles) ────────────────────

    public static readonly StyledProperty<VisualizerStyle> StyleModeProperty =
        AvaloniaProperty.Register<VisualizerControl, VisualizerStyle>(
            nameof(StyleMode), VisualizerStyle.SpectrumBars);

    public VisualizerStyle StyleMode
    {
        get => GetValue(StyleModeProperty);
        set => SetValue(StyleModeProperty, value);
    }

    // ── Particle state (persists across frames for fireworks/particles) ────────

    private static readonly List<Particle> _particles = new();
    private static float _time;
    private static float _lastBassHit;

    // Click to cycle visualizer modes
    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var modes = Enum.GetValues<VisualizerStyle>();
        int next = ((int)StyleMode + 1) % modes.Length;
        StyleMode = modes[next];
    }

    // ── Animation timer ───────────────────────────────────────────────────────

    private DispatcherTimer? _timer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 fps
        };
        _timer.Tick += (_, _) => InvalidateVisual();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        // Prefer SoundFlow spectrum (real-time from the audio being played)
        var sf = SoundFlow;
        if (sf is { HasSignal: true })
        {
            var spectrum = sf.GetSpectrum();
            var peakHold = sf.PeakHold;
            var bounds   = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.Custom(new VisualizerDrawOp(bounds, spectrum.ToArray(), peakHold, StyleMode));
            return;
        }

        // Fallback to VLC-based FFT capture
        var capture = CaptureService;
        if (capture is null) return;

        var spectrum2 = capture.GetSpectrum();
        var peakHold2 = capture.PeakHold;
        var bounds2   = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new VisualizerDrawOp(bounds2, spectrum2.ToArray(), peakHold2, StyleMode));
    }

    // ── Custom draw operation ─────────────────────────────────────────────────

    private sealed class VisualizerDrawOp(
        Rect         bounds,
        float[]      spectrum,
        float[]      peakHold,
        VisualizerStyle style) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false; // always re-render

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
                return;

            using var lease  = leaseFeature.Lease();
            var       canvas = lease.SkCanvas;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));

            float w = (float)bounds.Width, h = (float)bounds.Height;
            _time += 0.033f;

            switch (style)
            {
                case VisualizerStyle.SpectrumBars:
                    DrawSpectrumBars(canvas, spectrum, peakHold, w, h);
                    break;
                case VisualizerStyle.Oscilloscope:
                    DrawOscilloscope(canvas, spectrum, w, h);
                    break;
                case VisualizerStyle.CircularSpectrum:
                    DrawCircularSpectrum(canvas, spectrum, peakHold, w, h);
                    break;
                case VisualizerStyle.Fireworks:
                    DrawFireworks(canvas, spectrum, w, h);
                    break;
                case VisualizerStyle.ParticleWave:
                    DrawParticleWave(canvas, spectrum, w, h);
                    break;
            }

            canvas.Restore();
        }

        // ── Classic Winamp-style segmented spectrum bars ──────────────────────

        private static void DrawSpectrumBars(SKCanvas canvas,
                                             float[]  spectrum,
                                             float[]  peakHold,
                                             float    w,
                                             float    h)
        {
            // Black background like Winamp
            canvas.DrawRect(0, 0, w, h, new SKPaint { Color = new SKColor(0, 0, 0) });

            int   bands    = Math.Min(spectrum.Length, 64);
            int   segments = 20;                         // number of lit segments per bar
            float barW     = w / (bands * 1.2f);
            float gapW     = barW * 0.2f;
            float stepW    = barW + gapW;
            float segGap   = 1.5f;                       // gap between segments
            float segH     = (h - segGap * segments) / segments;
            if (segH < 1f) segH = 1f;

            using var segPaint  = new SKPaint { IsAntialias = false };
            using var peakPaint = new SKPaint { IsAntialias = false };
            using var dimPaint  = new SKPaint { IsAntialias = false, Color = new SKColor(10, 20, 10) };

            int stride = Math.Max(1, spectrum.Length / bands);

            for (int b = 0; b < bands; b++)
            {
                float mag = 0;
                for (int s = 0; s < stride && b * stride + s < spectrum.Length; s++)
                    mag += spectrum[b * stride + s];
                mag /= stride;

                int litSegs  = (int)(mag * segments);
                float x      = b * stepW + gapW * 0.5f;

                // Draw each segment from bottom to top
                for (int seg = 0; seg < segments; seg++)
                {
                    float segY = h - (seg + 1) * (segH + segGap);
                    float t    = (float)seg / segments; // 0=bottom, 1=top

                    if (seg < litSegs)
                    {
                        // Winamp green gradient: green at bottom → yellow in middle → red at top
                        byte r, g;
                        if (t < 0.6f)
                        {
                            // Green to yellow
                            float p = t / 0.6f;
                            r = (byte)(p * 255);
                            g = 255;
                        }
                        else
                        {
                            // Yellow to red
                            float p = (t - 0.6f) / 0.4f;
                            r = 255;
                            g = (byte)((1f - p) * 255);
                        }
                        segPaint.Color = new SKColor(r, g, 0);
                        canvas.DrawRect(x, segY, barW, segH, segPaint);
                    }
                    else
                    {
                        // Dim unlit segment
                        canvas.DrawRect(x, segY, barW, segH, dimPaint);
                    }
                }

                // Peak indicator — bright green/white line
                float peakVal = b < peakHold.Length ? peakHold[b * stride < peakHold.Length ? b * stride : 0] : 0;
                int peakSeg = (int)(peakVal * segments);
                if (peakSeg > 0 && peakSeg <= segments)
                {
                    float peakY = h - peakSeg * (segH + segGap);
                    peakPaint.Color = peakSeg > segments * 0.8f
                        ? new SKColor(255, 50, 50)   // red peak
                        : new SKColor(180, 255, 180); // green/white peak
                    canvas.DrawRect(x, peakY, barW, segH, peakPaint);
                }
            }
        }

        // ── Retro oscilloscope renderer ────────────────────────────────────────

        private static void DrawOscilloscope(SKCanvas canvas, float[] spectrum, float w, float h)
        {
            canvas.DrawRect(0, 0, w, h, new SKPaint { Color = new SKColor(0, 8, 0) });

            using var path = new SKPath();
            float cy    = h * 0.5f;
            float xStep = w / spectrum.Length;
            float amp   = h * 0.35f;

            path.MoveTo(0, cy);
            for (int i = 0; i < spectrum.Length; i++)
            {
                float x = i * xStep;
                // Clamp the value so it stays within the screen
                float v = Math.Clamp(spectrum[i], -1f, 1f);
                float y = cy - v * amp;
                path.LineTo(x, y);
            }

            // Glow layer (wide, dim)
            using var glowPaint = new SKPaint
            {
                Color       = new SKColor(0, 200, 40, 60),
                StrokeWidth = 6f,
                IsStroke    = true,
                IsAntialias = true,
            };
            canvas.DrawPath(path, glowPaint);

            // Main bright line
            using var paint = new SKPaint
            {
                Color       = new SKColor(0, 255, 80),
                StrokeWidth = 2.5f,
                IsStroke    = true,
                IsAntialias = true,
            };
            canvas.DrawPath(path, paint);

            // Hot center line (white-green core)
            using var corePaint = new SKPaint
            {
                Color       = new SKColor(180, 255, 200, 200),
                StrokeWidth = 1f,
                IsStroke    = true,
                IsAntialias = true,
            };
            canvas.DrawPath(path, corePaint);

            // Horizontal center reference line (very dim)
            using var refPaint = new SKPaint
            {
                Color = new SKColor(0, 60, 0, 40), StrokeWidth = 0.5f
            };
            canvas.DrawLine(0, cy, w, cy, refPaint);
        }

        // ── Circular spectrum ──────────────────────────────────────────────────

        private static void DrawCircularSpectrum(SKCanvas canvas, float[] spectrum,
                                                  float[] peakHold, float w, float h)
        {
            canvas.DrawRect(0, 0, w, h, new SKPaint { Color = new SKColor(5, 5, 15) });

            float cx = w / 2, cy = h / 2;
            float innerR = Math.Min(w, h) * 0.2f;
            float maxR   = Math.Min(w, h) * 0.45f;
            int bands = Math.Min(spectrum.Length, 64);

            using var paint = new SKPaint { IsAntialias = true };
            using var peakPaint = new SKPaint { IsAntialias = true };

            for (int i = 0; i < bands; i++)
            {
                float angle1 = (float)(i * 2 * Math.PI / bands) - MathF.PI / 2;
                float angle2 = (float)((i + 0.8f) * 2 * Math.PI / bands) - MathF.PI / 2;
                float mag = Math.Min(spectrum.Length > i ? spectrum[i] : 0, 1f);
                float barR = innerR + mag * (maxR - innerR);

                float t = (float)i / bands;
                byte r = (byte)(MathF.Sin(t * MathF.PI * 2 + _time) * 80 + 175);
                byte g = (byte)(MathF.Sin(t * MathF.PI * 2 + _time + 2) * 80 + 100);
                byte b = (byte)(MathF.Sin(t * MathF.PI * 2 + _time + 4) * 80 + 175);
                paint.Color = new SKColor(r, g, b);

                using var path = new SKPath();
                path.MoveTo(cx + innerR * MathF.Cos(angle1), cy + innerR * MathF.Sin(angle1));
                path.LineTo(cx + barR * MathF.Cos(angle1), cy + barR * MathF.Sin(angle1));
                path.LineTo(cx + barR * MathF.Cos(angle2), cy + barR * MathF.Sin(angle2));
                path.LineTo(cx + innerR * MathF.Cos(angle2), cy + innerR * MathF.Sin(angle2));
                path.Close();
                canvas.DrawPath(path, paint);

                // Peak dot
                float peakR = innerR + (peakHold.Length > i ? peakHold[i] : 0) * (maxR - innerR);
                float midAngle = (angle1 + angle2) / 2;
                peakPaint.Color = new SKColor(255, 255, 255, 180);
                canvas.DrawCircle(cx + peakR * MathF.Cos(midAngle),
                                  cy + peakR * MathF.Sin(midAngle), 2, peakPaint);
            }

            // Center glow
            using var glowPaint = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy), innerR,
                    new SKColor[] { new(100, 60, 200, 60), new(0, 0, 0, 0) },
                    SKShaderTileMode.Clamp),
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, innerR, glowPaint);
        }

        // ── Fireworks ─────────────────────────────────────────────────────────

        private static void DrawFireworks(SKCanvas canvas, float[] spectrum, float w, float h)
        {
            canvas.DrawRect(0, 0, w, h, new SKPaint { Color = new SKColor(2, 2, 8) });

            // Detect bass hit (average of first few bands)
            float bass = 0;
            for (int i = 0; i < Math.Min(8, spectrum.Length); i++)
                bass += spectrum[i];
            bass /= 8;

            // Launch fireworks on bass hits
            if (bass > 0.3f && _time - _lastBassHit > 0.15f)
            {
                _lastBassHit = _time;
                float launchX = w * 0.2f + (float)(Math.Abs(Math.Sin(_time * 7.3)) * w * 0.6f);
                float launchY = h * 0.2f + (float)(Math.Abs(Math.Cos(_time * 5.1)) * h * 0.3f);
                byte cr = (byte)(Math.Abs(Math.Sin(_time * 3.1)) * 200 + 55);
                byte cg = (byte)(Math.Abs(Math.Cos(_time * 4.7)) * 200 + 55);
                byte cb = (byte)(Math.Abs(Math.Sin(_time * 2.3 + 1)) * 200 + 55);
                var color = new SKColor(cr, cg, cb);

                int count = 20 + (int)(bass * 30);
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)(i * 2 * Math.PI / count + Math.Sin(_time) * 0.5);
                    float speed = 1.5f + bass * 3f + (float)(Math.Sin(i * 1.7) * 0.8);
                    _particles.Add(new Particle
                    {
                        X = launchX, Y = launchY,
                        VX = MathF.Cos(angle) * speed,
                        VY = MathF.Sin(angle) * speed,
                        Life = 1f,
                        Decay = 0.015f + (float)(Math.Abs(Math.Sin(i * 0.9)) * 0.01),
                        Size = 2f + bass * 2,
                        Color = color
                    });
                }
            }

            // Update and draw particles
            using var paint = new SKPaint { IsAntialias = true };
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.X += p.VX;
                p.Y += p.VY;
                p.VY += 0.04f; // gravity
                p.VX *= 0.99f; // drag
                p.Life -= p.Decay;
                _particles[i] = p;

                if (p.Life <= 0) { _particles.RemoveAt(i); continue; }

                byte alpha = (byte)(p.Life * 255);
                paint.Color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, alpha);
                canvas.DrawCircle(p.X, p.Y, p.Size * p.Life, paint);

                // Trail
                if (p.Life > 0.3f)
                {
                    paint.Color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, (byte)(alpha / 3));
                    canvas.DrawCircle(p.X - p.VX, p.Y - p.VY, p.Size * p.Life * 0.6f, paint);
                }
            }

            // Cap particles
            if (_particles.Count > 2000)
                _particles.RemoveRange(0, _particles.Count - 1500);
        }

        // ── Particle wave ─────────────────────────────────────────────────────

        private static void DrawParticleWave(SKCanvas canvas, float[] spectrum, float w, float h)
        {
            canvas.DrawRect(0, 0, w, h, new SKPaint { Color = new SKColor(3, 3, 12) });

            float energy = 0;
            for (int i = 0; i < spectrum.Length; i++)
                energy += spectrum[i];
            energy /= spectrum.Length;

            // Spawn ambient particles
            if (_particles.Count < 300)
            {
                for (int i = 0; i < 5; i++)
                {
                    float px = (float)(Math.Abs(Math.Sin(_time * (i + 1) * 1.3 + i * 7.7)) * w);
                    float py = h + 5;
                    byte cr = (byte)(Math.Abs(Math.Sin(_time + i)) * 100 + 100);
                    byte cb = (byte)(Math.Abs(Math.Cos(_time + i * 0.7)) * 155 + 100);
                    _particles.Add(new Particle
                    {
                        X = px, Y = py,
                        VX = (float)(Math.Sin(_time * 3 + i) * 0.5),
                        VY = -0.5f - energy * 3,
                        Life = 1f,
                        Decay = 0.005f + energy * 0.01f,
                        Size = 2 + energy * 4,
                        Color = new SKColor(cr, 80, cb)
                    });
                }
            }

            // Audio-reactive force field
            using var paint = new SKPaint { IsAntialias = true };
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];

                // Wave motion influenced by spectrum
                int band = (int)(p.X / w * Math.Min(spectrum.Length, 64));
                band = Math.Clamp(band, 0, spectrum.Length - 1);
                float freq = spectrum[band];

                p.X += p.VX + MathF.Sin(_time * 2 + p.Y * 0.02f) * freq * 2;
                p.Y += p.VY;
                p.VY -= freq * 0.1f; // audio pushes particles up
                p.Life -= p.Decay;
                _particles[i] = p;

                if (p.Life <= 0 || p.Y < -10) { _particles.RemoveAt(i); continue; }

                byte alpha = (byte)(p.Life * 200);
                float size = p.Size * (0.5f + freq * 2);
                paint.Color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, alpha);
                canvas.DrawCircle(p.X, p.Y, size, paint);

                // Glow
                paint.Color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, (byte)(alpha / 4));
                canvas.DrawCircle(p.X, p.Y, size * 2, paint);
            }

            if (_particles.Count > 1000)
                _particles.RemoveRange(0, _particles.Count - 800);
        }

        // ── Gradient lerp ─────────────────────────────────────────────────────

        private static SKColor LerpGradient(SKColor[] stops, float t)
        {
            if (stops.Length == 1) return stops[0];
            t = Math.Clamp(t, 0f, 1f);
            float scaled = t * (stops.Length - 1);
            int   lo     = (int)scaled;
            int   hi     = Math.Min(lo + 1, stops.Length - 1);
            float frac   = scaled - lo;

            return new SKColor(
                (byte)(stops[lo].Red   + (stops[hi].Red   - stops[lo].Red)   * frac),
                (byte)(stops[lo].Green + (stops[hi].Green - stops[lo].Green) * frac),
                (byte)(stops[lo].Blue  + (stops[hi].Blue  - stops[lo].Blue)  * frac));
        }
    }
}

// ── Visualizer style enum ─────────────────────────────────────────────────────

public enum VisualizerStyle
{
    SpectrumBars,
    Oscilloscope,
    CircularSpectrum,
    Fireworks,
    ParticleWave
}

public struct Particle
{
    public float X, Y, VX, VY, Life, Decay, Size;
    public SKColor Color;
}
