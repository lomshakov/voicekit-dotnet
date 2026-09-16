namespace VoiceKit;

/// <summary>Raised when the API returns a non-2xx response.</summary>
public sealed class VoiceKitError : Exception
{
    /// <summary>HTTP status code.</summary>
    public int Status { get; }

    /// <summary>Machine-readable error code (may be empty).</summary>
    public string Code { get; }

    /// <summary>
    /// Creates a new <see cref="VoiceKitError"/>.
    /// </summary>
    /// <param name="status">HTTP status code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="code">Machine-readable error code.</param>
    public VoiceKitError(int status, string message, string code = "")
        : base(message)
    {
        Status = status;
        Code = code;
    }
}
