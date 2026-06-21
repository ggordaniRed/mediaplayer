using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using MediaPlayer.Services;
using SkiaSharp;
using System.Numerics;

namespace MediaPlayer.Controls;

/// <summary>
/// GPU-accelerated shader visualizer using SkSL (SKRuntimeEffect).
/// Shaders run on the GPU via Skia's runtime effect pipeline.
/// Audio-reactive uniforms (iBass, iTreble, iEnergy) are passed each frame.
/// </summary>
public class ShaderVisualizerControl : UserControl
{
    public static readonly StyledProperty<SoundFlowAudioService?> SoundFlowProperty =
        AvaloniaProperty.Register<ShaderVisualizerControl, SoundFlowAudioService?>(nameof(SoundFlow));

    public SoundFlowAudioService? SoundFlow
    {
        get => GetValue(SoundFlowProperty);
        set => SetValue(SoundFlowProperty, value);
    }

    public static readonly StyledProperty<string?> ShaderFileProperty =
        AvaloniaProperty.Register<ShaderVisualizerControl, string?>(nameof(ShaderFile));

    public string? ShaderFile
    {
        get => GetValue(ShaderFileProperty);
        set => SetValue(ShaderFileProperty, value);
    }

    private CompositionCustomVisual? _visual;
    private static readonly string[] ShaderFiles = { "drive_home.sksl", "audio_sphere.sksl" };
    private int _currentShaderIndex;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var elemVisual = ElementComposition.GetElementVisual(this);
        var compositor = elemVisual?.Compositor;
        if (compositor is null) return;

        _visual = compositor.CreateCustomVisual(new ShaderHandler());
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);

        LoadCurrentShader();

        LayoutUpdated += (_, _) =>
        {
            if (_visual != null)
            {
                _visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
                _visual.SendHandlerMessage(new ShaderMsg(ShaderCmd.Resize, null, Bounds.Size));
            }
        };
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _currentShaderIndex = (_currentShaderIndex + 1) % ShaderFiles.Length;
        LoadCurrentShader();
    }

    public void NextShader()
    {
        _currentShaderIndex = (_currentShaderIndex + 1) % ShaderFiles.Length;
        LoadCurrentShader();
    }

    private void LoadCurrentShader()
    {
        var file = ShaderFiles[_currentShaderIndex];
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", file);
        if (!File.Exists(path)) path = file;
        _visual?.SendHandlerMessage(new ShaderMsg(ShaderCmd.Start, path, Bounds.Size));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _visual?.SendHandlerMessage(new ShaderMsg(ShaderCmd.Stop, null, default));
    }

    // Pass audio data to the shader handler each frame via property
    public void UpdateAudio(float bass, float treble, float energy)
    {
        _visual?.SendHandlerMessage(new AudioMsg(bass, treble, energy));
    }

    // ── Messages ──

    private enum ShaderCmd { Start, Stop, Resize }
    private record struct ShaderMsg(ShaderCmd Cmd, string? Path, Size Size);
    private record struct AudioMsg(float Bass, float Treble, float Energy);

    // ── Composition handler (runs on render thread) ──

    private class ShaderHandler : CompositionCustomVisualHandler
    {
        private SKRuntimeEffect? _effect;
        private SKRuntimeEffectUniforms? _uniforms;
        private bool _running;
        private Size _size;
        private float _bass, _treble, _energy;
        private readonly object _lock = new();

        public override void OnMessage(object message)
        {
            lock (_lock)
            {
                switch (message)
                {
                    case ShaderMsg { Cmd: ShaderCmd.Start, Path: { } path }:
                        LoadShader(path);
                        _running = true;
                        RegisterForNextAnimationFrameUpdate();
                        break;

                    case ShaderMsg { Cmd: ShaderCmd.Stop }:
                        _running = false;
                        _effect?.Dispose();
                        _effect = null;
                        break;

                    case ShaderMsg { Cmd: ShaderCmd.Resize } msg:
                        _size = msg.Size;
                        break;

                    case AudioMsg audio:
                        _bass = audio.Bass;
                        _treble = audio.Treble;
                        _energy = audio.Energy;
                        break;
                }
            }
        }

        private void LoadShader(string path)
        {
            string code;
            if (File.Exists(path))
                code = File.ReadAllText(path);
            else
            {
                Console.Error.WriteLine($"[Shader] File not found: {path}");
                return;
            }

            _effect?.Dispose();
            _effect = SKRuntimeEffect.CreateShader(code, out var errors);
            if (_effect == null)
                Console.Error.WriteLine($"[Shader] Compile error: {errors}");
            else
                Console.Error.WriteLine("[Shader] Compiled OK");

            _uniforms = _effect != null ? new SKRuntimeEffectUniforms(_effect) : null;
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_running) return;
            Invalidate();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            lock (_lock)
            {
                if (!_running || _effect == null || _uniforms == null) return;

                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature is null) return;

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;

                float w = (float)(_size.Width > 0 ? _size.Width : GetRenderBounds().Width);
                float h = (float)(_size.Height > 0 ? _size.Height : GetRenderBounds().Height);
                if (w <= 0 || h <= 0) return;

                _uniforms["iResolution"] = new[] { w, h };
                _uniforms["iTime"] = (float)CompositionNow.TotalSeconds;
                _uniforms["iBass"] = _bass;
                _uniforms["iTreble"] = _treble;
                _uniforms["iEnergy"] = _energy;

                using var shader = _effect.ToShader(_uniforms);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(SKRect.Create(w, h), paint);
            }
        }
    }
}
