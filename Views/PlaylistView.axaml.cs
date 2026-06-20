using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MediaPlayer.ViewModels;

namespace MediaPlayer.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();

        // Double-click a track to play it
        TrackList.DoubleTapped += (_, e) =>
        {
            if (DataContext is PlaylistViewModel vm && vm.SelectedItem is { } item)
                vm.PlayCommand.Execute(item);
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (this.Find<Button>("AddButton") is { } btn)
            btn.Click += OnAddButtonClick;
    }

    private async void OnAddButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title         = "Add Media Files",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Video & Audio")
                    {
                        Patterns = ["*.mp4","*.mkv","*.avi","*.mov","*.m4v",
                                    "*.flac","*.mp3","*.aac","*.ogg","*.wav","*.opus"]
                    }
                ]
            });

        var paths = System.Linq.Enumerable.Select(files, f => f.Path.LocalPath);
        vm.AddFiles(paths);
    }
}
