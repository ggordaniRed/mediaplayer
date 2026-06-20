using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace MediaPlayer.Controls;

public sealed class GreenHeadControl : Control
{
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new HeadDrawOp(bounds));
    }

    private sealed class HeadDrawOp(Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feat)
                return;
            using var lease = feat.Lease();
            var c = lease.SkCanvas;

            float w = (float)bounds.Width;
            float h = (float)bounds.Height;
            float cx = w / 2;

            c.Save();
            c.Clear(SKColors.Transparent);

            DrawHead(c, w, h, cx);
            DrawSpeakers(c, w, h);
            DrawFace(c, w, h, cx);
            DrawScreenFrame(c, w, h, cx);

            c.Restore();
        }

        private static void DrawHead(SKCanvas c, float w, float h, float cx)
        {
            // Main head shape — elongated oval, narrower at chin
            using var headPath = new SKPath();
            // Start at top center, go clockwise
            float topY = h * 0.08f;
            float botY = h * 0.92f;
            float sideW = w * 0.38f;

            // Top curve (wide forehead)
            headPath.MoveTo(cx, topY);
            headPath.CubicTo(cx + sideW * 1.1f, topY,
                             cx + sideW, h * 0.3f,
                             cx + sideW * 0.95f, h * 0.45f);
            // Right side curves down and inward to chin
            headPath.CubicTo(cx + sideW * 0.9f, h * 0.6f,
                             cx + sideW * 0.7f, h * 0.78f,
                             cx + w * 0.05f, botY);
            // Chin point
            headPath.CubicTo(cx + w * 0.02f, h * 0.95f,
                             cx - w * 0.02f, h * 0.95f,
                             cx - w * 0.05f, botY);
            // Left side back up
            headPath.CubicTo(cx - sideW * 0.7f, h * 0.78f,
                             cx - sideW * 0.9f, h * 0.6f,
                             cx - sideW * 0.95f, h * 0.45f);
            headPath.CubicTo(cx - sideW, h * 0.3f,
                             cx - sideW * 1.1f, topY,
                             cx, topY);
            headPath.Close();

            // 3D gradient: bright green on top, dark green at bottom
            using var headGrad = SKShader.CreateLinearGradient(
                new SKPoint(cx, topY), new SKPoint(cx, botY),
                new SKColor[]
                {
                    new(130, 230, 50),   // bright lime top
                    new(80, 200, 30),    // mid green
                    new(40, 140, 15),    // darker
                    new(20, 80, 5),      // dark bottom/chin
                },
                new float[] { 0f, 0.35f, 0.65f, 1f },
                SKShaderTileMode.Clamp);
            using var headPaint = new SKPaint { Shader = headGrad, IsAntialias = true };
            c.DrawPath(headPath, headPaint);

            // Subtle edge highlight
            using var edgePaint = new SKPaint
            {
                IsStroke = true, StrokeWidth = 1.5f, IsAntialias = true,
                Color = new SKColor(100, 200, 40, 80)
            };
            c.DrawPath(headPath, edgePaint);
        }

        private static void DrawSpeakers(SKCanvas c, float w, float h)
        {
            float spkCenterY = h * 0.3f;
            float[] xPositions = new[] { w * 0.07f, w * 0.93f };

            foreach (float sx in xPositions)
            {
                for (int i = 0; i < 3; i++)
                {
                    float sy = spkCenterY + (i - 1) * h * 0.09f;
                    float r = w * 0.045f;

                    // Speaker housing
                    using var housingPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Shader = SKShader.CreateRadialGradient(
                            new SKPoint(sx, sy), r,
                            new SKColor[] { new(160, 160, 160), new(80, 80, 80), new(40, 40, 40) },
                            new float[] { 0f, 0.7f, 1f },
                            SKShaderTileMode.Clamp)
                    };
                    c.DrawCircle(sx, sy, r, housingPaint);

                    // Speaker cone rings
                    using var ringPaint = new SKPaint
                    {
                        IsStroke = true, StrokeWidth = 1, IsAntialias = true,
                        Color = new SKColor(120, 120, 120)
                    };
                    c.DrawCircle(sx, sy, r * 0.7f, ringPaint);
                    c.DrawCircle(sx, sy, r * 0.35f, ringPaint);

                    // Center dot
                    using var dotPaint = new SKPaint { Color = new SKColor(50, 50, 50), IsAntialias = true };
                    c.DrawCircle(sx, sy, r * 0.15f, dotPaint);
                }

                // Green ear piece behind speakers
                float earX = sx < w / 2 ? sx + w * 0.03f : sx - w * 0.03f;
                using var earPaint = new SKPaint
                {
                    Color = new SKColor(60, 160, 30), IsAntialias = true
                };
                var earRect = new SKRect(
                    earX - w * 0.035f, spkCenterY - h * 0.14f,
                    earX + w * 0.035f, spkCenterY + h * 0.14f);
                c.DrawRoundRect(earRect, 6, 6, earPaint);
            }
        }

        private static void DrawFace(SKCanvas c, float w, float h, float cx)
        {
            // Eyes — angular, menacing, half-closed
            float eyeY = h * 0.53f;
            float eyeW = w * 0.1f;
            float eyeH = w * 0.03f;

            using var eyeShadow = new SKPaint { Color = new SKColor(10, 40, 5), IsAntialias = true };
            using var eyeGlow = new SKPaint { Color = new SKColor(60, 180, 30, 120), IsAntialias = true };

            // Left eye — angular slash
            using var leftEye = new SKPath();
            leftEye.MoveTo(cx - w * 0.2f, eyeY);
            leftEye.LineTo(cx - w * 0.1f, eyeY - eyeH);
            leftEye.LineTo(cx - w * 0.07f, eyeY);
            leftEye.LineTo(cx - w * 0.1f, eyeY + eyeH * 0.5f);
            leftEye.Close();
            c.DrawPath(leftEye, eyeShadow);
            c.DrawPath(leftEye, eyeGlow);

            // Right eye
            using var rightEye = new SKPath();
            rightEye.MoveTo(cx + w * 0.07f, eyeY);
            rightEye.LineTo(cx + w * 0.1f, eyeY - eyeH);
            rightEye.LineTo(cx + w * 0.2f, eyeY);
            rightEye.LineTo(cx + w * 0.1f, eyeY + eyeH * 0.5f);
            rightEye.Close();
            c.DrawPath(rightEye, eyeShadow);
            c.DrawPath(rightEye, eyeGlow);

            // Nose — subtle ridge line
            using var nosePaint = new SKPaint
            {
                Color = new SKColor(30, 90, 15), IsStroke = true,
                StrokeWidth = 2, IsAntialias = true
            };
            c.DrawLine(cx, h * 0.55f, cx, h * 0.65f, nosePaint);
            c.DrawLine(cx - 4, h * 0.65f, cx + 4, h * 0.65f, nosePaint);

            // Mouth — smirking line
            using var mouthPaint = new SKPaint
            {
                Color = new SKColor(15, 50, 8), IsStroke = true,
                StrokeWidth = 2.5f, IsAntialias = true
            };
            using var mouthPath = new SKPath();
            mouthPath.MoveTo(cx - w * 0.12f, h * 0.72f);
            mouthPath.CubicTo(cx - w * 0.05f, h * 0.74f,
                              cx + w * 0.05f, h * 0.73f,
                              cx + w * 0.1f, h * 0.7f);
            c.DrawPath(mouthPath, mouthPaint);

            // Chin shadow
            using var chinShadow = new SKPaint
            {
                Color = new SKColor(15, 50, 5, 100), IsAntialias = true
            };
            c.DrawOval(new SKRect(cx - w * 0.08f, h * 0.8f, cx + w * 0.08f, h * 0.88f), chinShadow);
        }

        private static void DrawScreenFrame(SKCanvas c, float w, float h, float cx)
        {
            // Screen bezel — dark border with green accent
            float sL = w * 0.18f, sR = w * 0.82f;
            float sT = h * 0.12f, sB = h * 0.42f;
            var screenRect = new SKRoundRect(new SKRect(sL, sT, sR, sB), 10, 10);

            // Dark screen background
            using var screenBg = new SKPaint { Color = new SKColor(3, 8, 3), IsAntialias = true };
            c.DrawRoundRect(screenRect, screenBg);

            // Green glowing border
            using var borderPaint = new SKPaint
            {
                IsStroke = true, StrokeWidth = 2.5f, IsAntialias = true,
                Color = new SKColor(80, 220, 40)
            };
            c.DrawRoundRect(screenRect, borderPaint);

            // Inner border shadow
            var innerRect = new SKRoundRect(new SKRect(sL + 3, sT + 3, sR - 3, sB - 3), 8, 8);
            using var innerBorder = new SKPaint
            {
                IsStroke = true, StrokeWidth = 1, IsAntialias = true,
                Color = new SKColor(40, 100, 20, 100)
            };
            c.DrawRoundRect(innerRect, innerBorder);

            // Scanline effect on screen
            using var scanPaint = new SKPaint
            {
                Color = new SKColor(0, 255, 0, 8), StrokeWidth = 1
            };
            for (float y = sT + 4; y < sB - 4; y += 3)
                c.DrawLine(sL + 4, y, sR - 4, y, scanPaint);
        }
    }
}
