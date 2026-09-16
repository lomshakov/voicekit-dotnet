namespace VoiceKit;

/// <summary>
/// Base64 helpers for <see cref="VoiceKitClient.BatchAnalyzeAsync"/>.
/// </summary>
public static class B64
{
    /// <summary>Encode raw bytes as base64.</summary>
    public static string FromBytes(byte[] data) => Convert.ToBase64String(data);

    /// <summary>Encode a local file's contents as base64.</summary>
    public static string FromFile(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

    /// <summary>Encode a local file's contents as base64.</summary>
    public static async Task<string> FromFileAsync(string path, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }
}
