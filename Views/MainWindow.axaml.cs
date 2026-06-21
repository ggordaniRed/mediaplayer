using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using MediaPlayer.ViewModels;

namespace MediaPlayer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent,     OnFileDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        // Pump audio data to the GPU shader visualizer
        var shaderTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        shaderTimer.Tick += (_, _) =>
        {
            if (DataContext is MainWindowViewModel { SoundFlowAudio: { HasSignal: true } sf })
            {
                var spec = sf.GetSpectrum();
                if (spec.Length > 0)
                {
                    float bass = 0, treble = 0, energy = 0;
                    int len = spec.Length;
                    for (int i = 0; i < len; i++)
                    {
                        energy += spec[i];
                        if (i < len / 4) bass += spec[i];
                        else if (i > len * 3 / 4) treble += spec[i];
                    }
                    bass /= Math.Max(1, len / 4);
                    treble /= Math.Max(1, len / 4);
                    energy /= len;
                    ShaderViz.UpdateAudio(bass, treble, energy);
                }
            }
        };
        shaderTimer.Start();

        // Wire up seek slider drag guard so user can drag without VLC snapping it back.
        SeekSlider.PointerPressed  += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.BeginSeek();
        };
        SeekSlider.PointerReleased += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.EndSeek(SeekSlider.Value);
        };
        SeekSlider.PointerCaptureLost += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.EndSeek(SeekSlider.Value);
        };
    }

#pragma warning disable CS0618 // IDataObject API is deprecated but still works in Avalonia 11.3
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        if (files is null) return;

        vm.Playlist.AddFiles(files.Select(f => f.Path.LocalPath));
    }
#pragma warning restore CS0618

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel vm) return;

        switch (e.Key)
        {
            case Key.Space:
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                vm.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                vm.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                vm.Volume = Math.Min(200, vm.Volume + 5);
                e.Handled = true;
                break;
            case Key.Down:
                vm.Volume = Math.Max(0, vm.Volume - 5);
                e.Handled = true;
                break;
            case Key.M:
                vm.IsMuted = !vm.IsMuted;
                e.Handled = true;
                break;
            case Key.E:
                vm.ToggleEqualizerPanelCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V:
                vm.ToggleVisualizerCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.O when e.KeyModifiers == KeyModifiers.Control:
                vm.OpenFilesCommand.Execute(StorageProvider);
                e.Handled = true;
                break;
            case Key.S:
                vm.ToggleSkinCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.G:
                vm.ToggleShaderModeCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
