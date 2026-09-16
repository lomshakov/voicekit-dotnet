using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VoiceKit;

/// <summary>
/// Thin, typed wrapper over the VoiceKit REST API.
/// </summary>
public sealed class VoiceKitClient : IDisposable
{
    private const string DefaultBaseUrl = "https://ttsapi.ru";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    /// <summary>
    /// Creates a new client.
    /// </summary>
    /// <param name="apiKey">API key (required).</param>
    /// <param name="baseUrl">API base URL (defaults to <c>https://ttsapi.ru</c>).</param>
    /// <param name="timeout">Per-request timeout (defaults to 120 seconds).</param>
    public VoiceKitClient(string apiKey, string baseUrl = DefaultBaseUrl, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("apiKey is required", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is required", nameof(baseUrl));

        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(120),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // ──────────────────────── Synthesis ────────────────────────────────

    /// <summary>Synthesize speech and return the raw audio bytes.</summary>
    /// <param name="text">Text to synthesize.</param>
    /// <param name="voice">Voice id (default <c>preset_anna</c>).</param>
    /// <param name="format">Audio format: <c>mp3</c>, <c>wav</c>, <c>ogg</c>.</param>
    /// <param name="effects">Optional JSON-encoded array of effect descriptors applied after synthesis (Pro/Business).</param>
    public Task<byte[]> SynthesizeAsync(
        string text,
        string voice = "preset_anna",
        string format = "mp3",
        int? sampleRate = null,
        double? speed = null,
        double? pitch = null,
        string? emotion = null,
        bool? ssml = null,
        bool? putAccent = null,
        bool? putYo = null,
        bool? normalize = null,
        string? model = null,
        string? language = null,
        string? effects = null,
        CancellationToken ct = default)
    {
        var body = Compact(
            ("text", text),
            ("voice", voice),
            ("format", format),
            ("sample_rate", sampleRate),
            ("speed", speed),
            ("pitch", pitch),
            ("emotion", emotion),
            ("ssml", ssml),
            ("put_accent", putAccent),
            ("put_yo", putYo),
            ("normalize", normalize),
            ("model", model),
            ("language", language),
            ("effects", effects));

        return PostBytesAsync("/v1/synthesize", body, ct);
    }

    /// <summary>Synthesize and yield audio chunks as they are produced (Pro/Business).</summary>
    public async IAsyncEnumerable<byte[]> SynthesizeStreamAsync(
        string text,
        string voice = "preset_anna",
        string format = "mp3",
        int? sampleRate = null,
        double? speed = null,
        double? pitch = null,
        string? emotion = null,
        bool? ssml = null,
        bool? putAccent = null,
        bool? putYo = null,
        bool? normalize = null,
        string? model = null,
        string? language = null,
        string? effects = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = Compact(
            ("text", text),
            ("voice", voice),
            ("format", format),
            ("sample_rate", sampleRate),
            ("speed", speed),
            ("pitch", pitch),
            ("emotion", emotion),
            ("ssml", ssml),
            ("put_accent", putAccent),
            ("put_yo", putYo),
            ("normalize", normalize),
            ("model", model),
            ("language", language),
            ("effects", effects));

        using var request = NewRequest(HttpMethod.Post, "/v1/synthesize/stream");
        request.Content = JsonContent(JsonSerializer.Serialize(body, JsonOptions));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                yield break;

            var chunk = new byte[read];
            Array.Copy(buffer, chunk, read);
            yield return chunk;
        }
    }

    /// <summary>
    /// Queue a long-form (audiobook) synthesis job; poll with
    /// <see cref="GetSynthesisJobAsync"/> and download with
    /// <see cref="DownloadSynthesisAudioAsync"/>. Long-form synthesis returns WAV only.
    /// </summary>
    public async Task<JsonNode?> SynthesizeLongFormAsync(
        string text,
        string voice = "preset_anna",
        string format = "wav",
        int? sampleRate = null,
        double? speed = null,
        string? model = null,
        string? language = null,
        string? webhookUrl = null,
        CancellationToken ct = default)
    {
        var body = Compact(
            ("text", text),
            ("voice", voice),
            ("format", format),
            ("sample_rate", sampleRate),
            ("speed", speed),
            ("model", model),
            ("language", language));

        var query = webhookUrl is null ? string.Empty : $"?webhookUrl={Uri.EscapeDataString(webhookUrl)}";
        return await PostJsonAsync($"/v1/synthesize/async{query}", body, ct).ConfigureAwait(false);
    }

    /// <summary>Poll a long-form synthesis job; returns the audiobook manifest when done.</summary>
    public Task<JsonNode?> GetSynthesisJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/synthesize/async/{jobId}", ct);

    /// <summary>Download the produced WAV for a completed long-form synthesis job.</summary>
    public Task<byte[]> DownloadSynthesisAudioAsync(string jobId, CancellationToken ct = default) =>
        GetBytesAsync($"/v1/synthesize/async/{jobId}/audio", ct);

    /// <summary>Return the voice catalog.</summary>
    public Task<JsonNode?> VoicesAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v1/voices", ct);

    /// <summary>Return one voice by id (e.g. <c>preset_anna</c>).</summary>
    public Task<JsonNode?> VoiceAsync(string id, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/voices/{id}", ct);

    // ──────────────────────── Voice cloning ────────────────────────────

    /// <summary>
    /// Create a cloned voice from reference audio (Pro/Business).
    /// <paramref name="samples"/> is one or more reference audio files;
    /// <paramref name="promptText"/> is the exact transcript of the reference clip.
    /// </summary>
    public Task<JsonNode?> CreateCloneVoiceAsync(
        string name,
        string promptText,
        AudioSource sample,
        string? language = null,
        CancellationToken ct = default) =>
        CreateCloneVoiceAsync(name, promptText, new[] { sample }, language, ct);

    /// <summary>
    /// Create a cloned voice from reference audio (Pro/Business).
    /// <paramref name="samples"/> is one or more reference audio files;
    /// <paramref name="promptText"/> is the exact transcript of the reference clip.
    /// </summary>
    public Task<JsonNode?> CreateCloneVoiceAsync(
        string name,
        string promptText,
        IEnumerable<AudioSource> samples,
        string? language = null,
        CancellationToken ct = default)
    {
        var files = samples.Select(s => ("samples", s.Read(), s.FileName, "audio/wav")).ToArray();
        var form = new List<KeyValuePair<string, string?>>
        {
            new("name", name),
            new("prompt_text", promptText),
            new("language", language),
        };
        return PostFormAsync("/v1/voices/clone", files, form, ct);
    }

    /// <summary>List the caller's cloned voices.</summary>
    public Task<JsonNode?> ListCloneVoicesAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v1/voices/clone", ct);

    /// <summary>Return one cloned voice by id.</summary>
    public Task<JsonNode?> GetCloneVoiceAsync(string cloneId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/voices/clone/{cloneId}", ct);

    /// <summary>Delete a cloned voice (and its reference latents).</summary>
    public async Task DeleteCloneVoiceAsync(string cloneId, CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Delete, $"/v1/voices/clone/{cloneId}");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    // ──────────────────────── Transcription ────────────────────────────

    /// <summary>Start an async transcription job; poll with <see cref="GetTranscriptionJobAsync"/>.</summary>
    public Task<JsonNode?> TranscribeAsync(
        AudioSource audio,
        string? language = null,
        bool diarization = false,
        string? webhookUrl = null,
        IEnumerable<string>? keyterms = null,
        CancellationToken ct = default)
    {
        var form = TranscribeForm(language, diarization, webhookUrl, keyterms);
        return PostFormAsync("/v1/transcribe", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, form, ct);
    }

    /// <summary>Transcribe a short file (≤ 3 min) synchronously.</summary>
    public Task<JsonNode?> TranscribeSyncAsync(
        AudioSource audio,
        string? language = null,
        bool diarization = false,
        IEnumerable<string>? keyterms = null,
        CancellationToken ct = default)
    {
        var form = TranscribeForm(language, diarization, webhookUrl: null, keyterms);
        return PostFormAsync("/v1/transcribe/sync", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, form, ct);
    }

    /// <summary>Poll a transcription job; returns the transcript and segments when done.</summary>
    public Task<JsonNode?> GetTranscriptionJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/transcribe/{jobId}", ct);

    /// <summary>Download VTT/SRT subtitles for a completed transcription job.</summary>
    public async Task<string> SubtitlesAsync(string jobId, string format = "vtt", CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Get, $"/v1/transcribe/{jobId}/subtitles?format={Uri.EscapeDataString(format)}");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Detect speech segments in an audio file (Silero VAD).</summary>
    public Task<JsonNode?> VadAsync(AudioSource audio, CancellationToken ct = default) =>
        PostFormAsync("/v1/vad", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, null, ct);

    // ──────────────────────── Voice ID ────────────────────────────────

    /// <summary>
    /// Analyze a voice recording and return a voice passport: language, gender,
    /// age group, emotional background, a speaker embedding (fingerprint) and an
    /// AI-vs-human probability (beta). The <c>ai_probability</c> field is null
    /// when the anti-spoofing detector is not configured.
    /// </summary>
    public Task<JsonNode?> VoiceIdAsync(AudioSource audio, CancellationToken ct = default) =>
        PostFormAsync("/v1/voice-id", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, null, ct);

    /// <summary>Enroll an audio clip as a reusable voice profile (voiceprint). Returns <c>{"profile_id": "voice_…"}</c>.</summary>
    public Task<JsonNode?> EnrollVoiceAsync(AudioSource audio, string? name = null, CancellationToken ct = default) =>
        PostFormAsync(
            "/v1/voice-id/enroll",
            new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") },
            new List<KeyValuePair<string, string?>> { new("name", name) },
            ct);

    /// <summary>Verify an audio clip against an enrolled profile (1:1).</summary>
    public Task<JsonNode?> VerifyVoiceAsync(AudioSource audio, string profileId, CancellationToken ct = default) =>
        PostFormAsync(
            "/v1/voice-id/verify",
            new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") },
            new List<KeyValuePair<string, string?>> { new("profile_id", profileId) },
            ct);

    /// <summary>Identify the closest matching profile for an audio clip (1:N); optionally restrict the search with <paramref name="profileIds"/>.</summary>
    public Task<JsonNode?> IdentifyVoiceAsync(
        AudioSource audio,
        IEnumerable<string>? profileIds = null,
        CancellationToken ct = default) =>
        PostFormAsync(
            "/v1/voice-id/identify",
            new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") },
            profileIds is null || !profileIds.Any()
                ? null
                : new List<KeyValuePair<string, string?>> { new("profile_ids", string.Join(",", profileIds)) },
            ct);

    /// <summary>List the caller's enrolled voice profiles.</summary>
    public Task<JsonNode?> ListVoiceProfilesAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v1/voice-id/profiles", ct);

    /// <summary>Delete an enrolled voice profile by id.</summary>
    public async Task DeleteVoiceProfileAsync(string profileId, CancellationToken ct = default)
    {
        using var request = NewRequest(HttpMethod.Delete, $"/v1/voice-id/profiles/{profileId}");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    // ──────────────────────── Analysis ─────────────────────────────────

    /// <summary>Start an async analysis job; poll with <see cref="GetAnalysisJobAsync"/>.</summary>
    public Task<JsonNode?> AnalyzeAsync(
        AudioSource audio,
        string? language = null,
        bool diarization = false,
        string? webhookUrl = null,
        IEnumerable<string>? keyterms = null,
        CancellationToken ct = default)
    {
        var form = TranscribeForm(language, diarization, webhookUrl, keyterms);
        return PostFormAsync("/v1/analyze", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, form, ct);
    }

    /// <summary>Analyze a short file (≤ 3 min) synchronously.</summary>
    public Task<JsonNode?> AnalyzeSyncAsync(
        AudioSource audio,
        string? language = null,
        bool diarization = false,
        bool emotions = true,
        bool keywords = true,
        bool entities = true,
        IEnumerable<string>? keyterms = null,
        CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string?>>
        {
            new("language", language),
            new("diarization", diarization ? "true" : "false"),
            new("emotions", emotions ? "true" : "false"),
            new("keywords", keywords ? "true" : "false"),
            new("entities", entities ? "true" : "false"),
        };
        AddKeyterms(form, keyterms);
        return PostFormAsync("/v1/analyze/sync", new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") }, form, ct);
    }

    /// <summary>Poll an analysis job; returns transcript, keywords, entities, speakers.</summary>
    public Task<JsonNode?> GetAnalysisJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/analyze/{jobId}", ct);

    // ──────────────────────── Text intelligence ────────────────────────

    /// <summary>Detect the language of the given text.</summary>
    public Task<JsonNode?> DetectLanguageAsync(string text, CancellationToken ct = default) =>
        PostJsonAsync("/v1/detect-language", new Dictionary<string, object?> { ["text"] = text }, ct);

    /// <summary>Redact PII (names, phones, addresses) from text.</summary>
    public Task<JsonNode?> RedactAsync(string text, string? language = null, CancellationToken ct = default) =>
        PostJsonAsync("/v1/redact", Compact(("text", text), ("language", language)), ct);

    /// <summary>Extract topics from text.</summary>
    public Task<JsonNode?> TopicsAsync(string text, string? language = null, CancellationToken ct = default) =>
        PostJsonAsync("/v1/analyze/topics", Compact(("text", text), ("language", language)), ct);

    /// <summary>Summarize text.</summary>
    public Task<JsonNode?> SummarizeAsync(string text, string? language = null, int? maxSentences = null, CancellationToken ct = default) =>
        PostJsonAsync("/v1/analyze/summarize", Compact(("text", text), ("language", language), ("max_sentences", maxSentences)), ct);

    /// <summary>Flag profanity, insults and hate speech in raw text.</summary>
    public Task<JsonNode?> ModerateAsync(string text, string? language = null, CancellationToken ct = default) =>
        PostJsonAsync("/v1/moderate", Compact(("text", text), ("language", language)), ct);

    // ──────────────────────── Audio / video effects ───────────────────

    /// <summary>
    /// Start an async audio-effects job; poll with <see cref="GetAudioEffectsJobAsync"/>.
    /// <paramref name="effects"/> is a list of effect descriptors, e.g.
    /// <c>new[] { new { type = "reverb", room_size = 0.5 } }</c>.
    /// </summary>
    public Task<JsonNode?> ApplyAudioEffectsAsync(
        AudioSource audio,
        IEnumerable<object> effects,
        string outputFormat = "wav",
        string? webhookUrl = null,
        CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string?>>
        {
            new("effects", JsonSerializer.Serialize(effects, JsonOptions)),
            new("output_format", outputFormat),
        };
        var query = webhookUrl is null ? string.Empty : $"?webhookUrl={Uri.EscapeDataString(webhookUrl)}";
        return PostFormAsync(
            $"/v1/audio/effects{query}",
            new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") },
            form,
            ct);
    }

    /// <summary>Poll an audio-effects job; returns the manifest when completed.</summary>
    public Task<JsonNode?> GetAudioEffectsJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/audio/effects/{jobId}", ct);

    /// <summary>Download the produced audio for a completed audio-effects job.</summary>
    public Task<byte[]> DownloadAudioEffectsAsync(string jobId, CancellationToken ct = default) =>
        GetBytesAsync($"/v1/audio/effects/{jobId}/audio", ct);

    /// <summary>
    /// Start an async video-effects job; poll with <see cref="GetVideoEffectsJobAsync"/>.
    /// <paramref name="mode"/> is <c>mux</c> (video output) or <c>audio</c>
    /// (processed audio extracted from the video). For <c>mux</c> you may pass a
    /// separate <paramref name="audio"/> file to replace the video's audio track.
    /// </summary>
    public Task<JsonNode?> ApplyVideoEffectsAsync(
        AudioSource video,
        IEnumerable<object> effects,
        string mode = "mux",
        AudioSource? audio = null,
        string? outputFormat = null,
        string? webhookUrl = null,
        CancellationToken ct = default)
    {
        var files = new List<(string Field, byte[] Data, string FileName, string ContentType)>
        {
            ("video", video.Read(), video.FileName, "video/mp4"),
        };
        if (audio is { } a)
            files.Add(("audio", a.Read(), a.FileName, "audio/wav"));

        var form = new List<KeyValuePair<string, string?>>
        {
            new("effects", JsonSerializer.Serialize(effects, JsonOptions)),
            new("mode", mode),
            new("output_format", outputFormat),
        };
        var query = webhookUrl is null ? string.Empty : $"?webhookUrl={Uri.EscapeDataString(webhookUrl)}";
        return PostFormAsync($"/v1/video/effects{query}", files, form, ct);
    }

    /// <summary>Poll a video-effects job; returns the manifest when completed.</summary>
    public Task<JsonNode?> GetVideoEffectsJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/video/effects/{jobId}", ct);

    /// <summary>Download the produced artifact (video or audio) for a video-effects job.</summary>
    public Task<byte[]> DownloadVideoEffectsAsync(string jobId, CancellationToken ct = default) =>
        GetBytesAsync($"/v1/video/effects/{jobId}/file", ct);

    // ──────────────────────── Audio cleaning ──────────────────────────

    /// <summary>
    /// Start an async audio-cleaning job; poll with <see cref="GetAudioCleaningJobAsync"/>.
    /// With no <paramref name="options"/> the one-click preset (denoise + normalize)
    /// is applied. <paramref name="options"/> may contain <c>denoise</c>
    /// (<c>strength</c>, <c>stationary</c>), <c>normalize</c> (<c>target_db</c>),
    /// <c>high_pass</c> and <c>low_pass</c> cutoff frequencies.
    /// </summary>
    public Task<JsonNode?> CleanAudioAsync(
        AudioSource audio,
        object? options = null,
        string outputFormat = "wav",
        string? webhookUrl = null,
        CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string?>> { new("output_format", outputFormat) };
        if (options is not null)
            form.Add(new("options", JsonSerializer.Serialize(options, JsonOptions)));
        var query = webhookUrl is null ? string.Empty : $"?webhookUrl={Uri.EscapeDataString(webhookUrl)}";
        return PostFormAsync(
            $"/v1/audio/clean{query}",
            new[] { ("audio", audio.Read(), audio.FileName, "audio/wav") },
            form,
            ct);
    }

    /// <summary>Poll an audio-cleaning job; returns the manifest when completed.</summary>
    public Task<JsonNode?> GetAudioCleaningJobAsync(string jobId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/audio/clean/{jobId}", ct);

    /// <summary>Download the cleaned audio for a completed audio-cleaning job.</summary>
    public Task<byte[]> DownloadAudioCleaningAsync(string jobId, CancellationToken ct = default) =>
        GetBytesAsync($"/v1/audio/clean/{jobId}/audio", ct);

    // ──────────────────────── Batch ────────────────────────────────────

    /// <summary>Queue a batch of synthesis requests; poll with <see cref="GetBatchAsync"/>.</summary>
    public Task<JsonNode?> BatchSynthesizeAsync(IEnumerable<object> items, CancellationToken ct = default) =>
        PostJsonAsync("/v1/batch/synthesize", new Dictionary<string, object?> { ["items"] = items }, ct);

    /// <summary>Queue a batch of analysis requests; each item needs inline base64 <c>audio</c>.</summary>
    public Task<JsonNode?> BatchAnalyzeAsync(IEnumerable<object> items, CancellationToken ct = default) =>
        PostJsonAsync("/v1/batch/analyze", new Dictionary<string, object?> { ["items"] = items }, ct);

    /// <summary>Poll a batch job; returns per-item results when done.</summary>
    public Task<JsonNode?> GetBatchAsync(string batchId, CancellationToken ct = default) =>
        GetJsonAsync($"/v1/batch/{batchId}", ct);

    // ──────────────────────── Account ──────────────────────────────────

    /// <summary>Current monthly character usage for the authenticated key.</summary>
    public Task<JsonNode?> UsageAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v1/usage", ct);

    /// <summary>Current balance, plan and recent transactions.</summary>
    public Task<JsonNode?> BillingBalanceAsync(CancellationToken ct = default) =>
        GetJsonAsync("/v1/billing/balance", ct);

    // ──────────────────────── Streaming (WebSocket) ────────────────────

    /// <summary>
    /// Open a streaming transcription session (Pro/Business). Send PCM16 chunks with
    /// <see cref="WsStream.SendAudioAsync"/>, finalize with <see cref="WsStream.StopAsync"/>,
    /// then read JSON events with <see cref="WsStream.EventsAsync"/>.
    /// </summary>
    public Task<WsStream> TranscribeStreamAsync(
        string? language = null,
        IEnumerable<string>? keyterms = null,
        bool interim = true,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["language"] = language,
            ["interim"] = interim ? "true" : "false",
        };
        if (keyterms is not null)
            query["keyterms"] = string.Join(",", keyterms);

        return ConnectWsAsync(BuildWsUri("/v1/transcribe/stream", query), ct);
    }

    /// <summary>Open a streaming turn-detection session (VAD events only).</summary>
    public Task<WsStream> VadStreamAsync(CancellationToken ct = default) =>
        ConnectWsAsync(BuildWsUri("/v1/vad/stream"), ct);

    // ──────────────────────── Transport ────────────────────────────────

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", _apiKey);
        return request;
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private async Task<JsonNode?> GetJsonAsync(string path, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<JsonNode?> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Post, path);
        request.Content = JsonContent(JsonSerializer.Serialize(body, JsonOptions));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<byte[]> PostBytesAsync(string path, object body, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Post, path);
        request.Content = JsonContent(JsonSerializer.Serialize(body, JsonOptions));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]> GetBytesAsync(string path, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<JsonNode?> PostFormAsync(
        string path,
        IEnumerable<(string Field, byte[] Data, string FileName, string ContentType)>? files,
        IEnumerable<KeyValuePair<string, string?>>? form,
        CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Post, path);
        var content = new MultipartFormDataContent();

        if (files is not null)
        {
            foreach (var (field, data, fileName, contentType) in files)
            {
                var fileContent = new ByteArrayContent(data);
                if (!string.IsNullOrEmpty(contentType))
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(fileContent, field, fileName);
            }
        }

        if (form is not null)
        {
            foreach (var (key, value) in form)
            {
                if (value is not null)
                    content.Add(new StringContent(value), key);
            }
        }

        request.Content = content;
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<WsStream> ConnectWsAsync(Uri uri, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("X-Api-Key", _apiKey);
        await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
        return new WsStream(ws);
    }

    private Uri BuildWsUri(string path, IReadOnlyDictionary<string, string?>? query = null)
    {
        var baseUri = new Uri(_baseUrl);
        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = path,
            Query = string.Empty,
        };

        if (query is not null)
        {
            var parts = new List<string>();
            foreach (var (key, value) in query)
            {
                if (string.IsNullOrEmpty(value))
                    continue;
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }

            if (parts.Count > 0)
                builder.Query = string.Join("&", parts);
        }

        return builder.Uri;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = string.Empty;
        var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var codeEl))
                    code = codeEl.GetString() ?? string.Empty;
                if (root.TryGetProperty("detail", out var detailEl))
                    detail = detailEl.GetString() ?? detail;
                else if (root.TryGetProperty("title", out var titleEl))
                    detail = titleEl.GetString() ?? detail;
            }
            catch (JsonException)
            {
                // Response body is not JSON; keep it as the message.
            }
        }

        throw new VoiceKitError((int)response.StatusCode, detail, code);
    }

    private static List<KeyValuePair<string, string?>> TranscribeForm(
        string? language,
        bool diarization,
        string? webhookUrl,
        IEnumerable<string>? keyterms)
    {
        var form = new List<KeyValuePair<string, string?>>
        {
            new("language", language),
            new("diarization", diarization ? "true" : "false"),
            new("webhookUrl", webhookUrl),
        };
        AddKeyterms(form, keyterms);
        return form;
    }

    private static void AddKeyterms(List<KeyValuePair<string, string?>> form, IEnumerable<string>? keyterms)
    {
        if (keyterms is not null)
        {
            var joined = string.Join(",", keyterms);
            if (!string.IsNullOrEmpty(joined))
                form.Add(new KeyValuePair<string, string?>("keyterms", joined));
        }
    }

    private static Dictionary<string, object?> Compact(params (string Key, object? Value)[] entries)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in entries)
        {
            if (value is not null)
                dict[key] = value;
        }

        return dict;
    }
}
