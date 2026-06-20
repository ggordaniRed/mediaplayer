using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaPlayer.Services;
using MediaPlayer.ViewModels;
using MediaPlayer.Views;

namespace MediaPlayer;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ── Compose the object graph ───────────────────────────────────
            // Show window immediately with a loading state
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            // Initialize VLC on a background thread to avoid blocking the UI
            // (VLC's plugin scan takes seconds and would freeze the window)
            System.Threading.Tasks.Task.Run(() =>
            {
                var fftProcessor        = new FftProcessor();
                var audioCaptureService = new AudioCaptureService(fftProcessor);
                var soundFlowAudio      = new SoundFlowAudioService();
                var mediaService        = new VlcMediaService(audioCaptureService, soundFlowAudio);
                var mainVm              = new MainWindowViewModel(mediaService, audioCaptureService);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    mainWindow.DataContext = mainVm;

                    // Toggle between normal and green head skin
                    GreenHeadWindow? skinWindow = null;
                    mainVm.SkinToggleRequested += () =>
                    {
                        if (skinWindow is null || !skinWindow.IsVisible)
                        {
                            skinWindow = new GreenHeadWindow { DataContext = mainVm };
                            skinWindow.Show();
                            mainWindow.Hide();
                            skinWindow.Closed += (_, _) =>
                            {
                                mainWindow.Show();
                                skinWindow = null;
                            };
                        }
                        else
                        {
                            skinWindow.Close();
                            skinWindow = null;
                            mainWindow.Show();
                        }
                    };
                });

                desktop.Exit += (_, _) =>
                {
                    mainVm.Dispose();
                    audioCaptureService.Dispose();
                    soundFlowAudio.Dispose();
                };
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
