using Avalonia;
using System;

namespace MediaPlayer;

sealed class Program
{
    // Entry point: keep this minimal — Avalonia app lifetime handles the rest.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()           // auto-selects Win32/macOS/Linux backend
            .WithInterFont()
            .LogToTrace()
            .UseSkia();                    // force Skia renderer (required for VisualizerControl)
}
