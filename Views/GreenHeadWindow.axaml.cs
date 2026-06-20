using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MediaPlayer.ViewModels;

namespace MediaPlayer.Views;

public partial class GreenHeadWindow : Window
{
    public GreenHeadWindow()
    {
        InitializeComponent();

        // Make the transparent window draggable by clicking anywhere on the head
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        // Seek slider drag guard
        SeekSlider.PointerPressed += (_, _) =>
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

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
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
        }
    }
}
