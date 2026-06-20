namespace MediaPlayer.Services;

/// <summary>Lightweight descriptor for an audio or subtitle track.</summary>
public readonly record struct TrackDescription(int Id, string Name)
{
    public override string ToString() => Name;
}
