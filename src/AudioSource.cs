namespace VoiceKit;

/// <summary>
/// Audio input: raw bytes or a path to a local file.
/// Implicitly convertible from <see cref="byte"/>[] and <see cref="string"/> (a file path).
/// </summary>
public readonly struct AudioSource
{
    private readonly byte[]? _bytes;
    private readonly string? _path;

    /// <summary>File name used when uploading bytes (defaults to <c>audio.wav</c>).</summary>
    public string FileName { get; }

    private AudioSource(byte[]? bytes, string? path, string fileName)
    {
        _bytes = bytes;
        _path = path;
        FileName = fileName;
    }

    /// <summary>Wrap raw audio bytes.</summary>
    public static implicit operator AudioSource(byte[] data) => new(data, null, "audio.wav");

    /// <summary>Wrap a path to a local audio file.</summary>
    public static implicit operator AudioSource(string path) => new(null, path, System.IO.Path.GetFileName(path));

    internal byte[] Read() => _bytes ?? File.ReadAllBytes(_path!);
}
