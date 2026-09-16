using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace VoiceKit;

/// <summary>
/// A connected streaming session (transcription or VAD).
/// Send raw PCM16 (16 kHz, mono, little-endian) with <see cref="SendAudioAsync"/>,
/// finalize with <see cref="StopAsync"/>, then read JSON events with
/// <see cref="EventsAsync"/>.
/// </summary>
public sealed class WsStream : IAsyncDisposable
{
    private readonly ClientWebSocket _ws;

    internal WsStream(ClientWebSocket ws)
    {
        _ws = ws;
    }

    /// <summary>Send raw PCM16 (16 kHz, mono, little-endian) audio as a binary frame.</summary>
    public Task SendAudioAsync(byte[] pcm16, CancellationToken ct = default) =>
        SendAsync(pcm16, WebSocketMessageType.Binary, ct);

    /// <summary>Send a text frame.</summary>
    public Task SendTextAsync(string text, CancellationToken ct = default) =>
        SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, ct);

    /// <summary>Send an object as a JSON text frame.</summary>
    public Task SendJsonAsync(object payload, CancellationToken ct = default) =>
        SendTextAsync(JsonSerializer.Serialize(payload), ct);

    /// <summary>Signal end of speech so the server finalizes the utterance.</summary>
    public Task StopAsync(CancellationToken ct = default) =>
        SendJsonAsync(new { type = "stop" }, ct);

    /// <summary>
    /// Receive JSON events (<c>session</c>, <c>vad</c>, <c>partial</c>, <c>final</c>, <c>error</c>).
    /// </summary>
    public async IAsyncEnumerable<JsonElement> EventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var receiveBuffer = new byte[64 * 1024];

        while (_ws.State == WebSocketState.Open)
        {
            var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    yield break;
                if (result.MessageType == WebSocketMessageType.Text)
                    message.Write(receiveBuffer, 0, result.Count);
            } while (!result.EndOfMessage);

            yield return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    /// <summary>Close the session.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _ws.Dispose();
        }
    }

    private async Task SendAsync(byte[] data, WebSocketMessageType type, CancellationToken ct)
    {
        await _ws.SendAsync(new ArraySegment<byte>(data), type, true, ct).ConfigureAwait(false);
    }
}
