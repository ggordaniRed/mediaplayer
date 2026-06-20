using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaPlayer.Models;

namespace MediaPlayer.ViewModels;

public partial class PlaylistViewModel : ViewModelBase
{
    // ── State ──────────────────────────────────────────────────────────────────

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty] private MediaItem? _selectedItem;
    [ObservableProperty] private bool _isLooping;
    [ObservableProperty] private bool _isShuffling;

    // ── Events raised to parent ────────────────────────────────────────────────

    public event Action<MediaItem>? PlayRequested;

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Play(MediaItem? item)
    {
        if (item is null) return;
        SelectedItem = item;
        PlayRequested?.Invoke(item);
    }

    [RelayCommand]
    private void Remove(MediaItem? item)
    {
        if (item is null) return;
        Items.Remove(item);
        if (SelectedItem == item) SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand]
    private void MoveUp(MediaItem? item)
    {
        if (item is null) return;
        int idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown(MediaItem? item)
    {
        if (item is null) return;
        int idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void Clear() => Items.Clear();

    // ── Navigation helpers (called by MainWindowViewModel) ────────────────────

    public MediaItem? GetNext(MediaItem current)
    {
        if (Items.Count == 0) return null;

        if (IsShuffling)
            return Items[System.Random.Shared.Next(Items.Count)];

        int idx = Items.IndexOf(current);
        int next = idx + 1;
        if (next >= Items.Count)
            return IsLooping ? Items[0] : null;
        return Items[next];
    }

    public MediaItem? GetPrevious(MediaItem current)
    {
        if (Items.Count == 0) return null;
        int idx = Items.IndexOf(current);
        int prev = idx - 1;
        if (prev < 0)
            return IsLooping ? Items[^1] : null;
        return Items[prev];
    }

    // ── Drag-and-drop entry point ──────────────────────────────────────────────

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var item = new MediaItem(path);
            if (item.IsValid && !Items.Contains(item))
                Items.Add(item);
        }
    }
}
