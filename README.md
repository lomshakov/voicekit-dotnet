# VoiceKit — C# / .NET SDK

[![NuGet version](https://img.shields.io/nuget/v/VoiceKit.Client)](https://www.nuget.org/packages/VoiceKit.Client/)
[![.NET](https://img.shields.io/badge/.NET-net8.0-blue)](https://www.nuget.org/packages/VoiceKit.Client/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](./LICENSE)

Official C#/.NET wrapper for **[VoiceKit](https://ttsapi.ru)** — the REST API for Russian speech:
neural speech synthesis (TTS), transcription (STT) with diarization and timestamps,
sentiment analysis, voice cloning, voice biometrics, audio effects and batch operations.

> **Links:** [Website](https://ttsapi.ru) · [Documentation](https://ttsapi.ru/docs) · [API reference](https://ttsapi.ru/swagger) · [Pricing](https://ttsapi.ru/pricing) · [Blog](https://ttsapi.ru/blog)

No external dependencies — uses only the .NET base class library
(`HttpClient`, `System.Text.Json`, `System.Net.WebSockets`). Target framework: `net8.0`.

## Install

```bash
dotnet add package VoiceKit.Client
```

## Quick start

```csharp
using VoiceKit;

using var client = new VoiceKitClient("YOUR_KEY");

// Synthesis → raw audio bytes
byte[] audio = await client.SynthesizeAsync("Привет! Это синтез русской речи.", voice: "preset_anna", format: "mp3");
await File.WriteAllBytesAsync("speech.mp3", audio);

// Streaming (Pro/Business)
await foreach (var chunk in client.SynthesizeStreamAsync("Первое предложение. Второе."))
{
    // write chunks to a file or socket
}

// Transcription (async → poll)
var job = await client.TranscribeAsync("audio.wav", keyterms: new[] { "диагноз" });
var result = await client.GetTranscriptionJobAsync(job!["job_id"]!.GetValue<string>());
while (result!["status"]!.GetValue<string>() is not ("completed" or "failed"))
{
    await Task.Delay(1000);
    result = await client.GetTranscriptionJobAsync(job!["job_id"]!.GetValue<string>());
}

// Short-file sync transcription
var transcript = await client.TranscribeSyncAsync("audio.wav");

// Analysis (sentiment + keywords + entities)
var analysis = await client.AnalyzeSyncAsync("audio.wav");

// Text intelligence
var lang = await client.DetectLanguageAsync("Как дела?");
var topics = await client.TopicsAsync("Нейросети и алгоритмы");
var summary = await client.SummarizeAsync("Длинный текст для резюме.", maxSentences: 3);
var moderation = await client.ModerateAsync("Это оскорбительное сообщение.");
var redacted = await client.RedactAsync("Иван позвонил на +7 900 123-45-67 из Москвы.");

// Batches
var batch = await client.BatchSynthesizeAsync(new object[]
{
    new { text = "Первый текст", voice = "preset_anna" },
    new { text = "Второй текст", voice = "dmitri" },
});
var status = await client.GetBatchAsync(batch!["batch_id"]!.GetValue<string>());

var analysisBatch = await client.BatchAnalyzeAsync(new object[]
{
    new { audio = B64.FromFile("a.wav"), language = "ru" },
    new { audio = B64.FromFile("b.wav"), language = "ru" },
});

// Voice cloning (Pro/Business)
var clone = await client.CreateCloneVoiceAsync(
    name: "My voice",
    promptText: "Точный текст образца.",
    sample: "reference.wav");
Console.WriteLine((await client.ListCloneVoicesAsync())!.ToJsonString());
await client.DeleteCloneVoiceAsync(clone!["id"]!.GetValue<string>());

// VAD (speech segments)
var segments = await client.VadAsync("audio.wav");

// Account
var usage = await client.UsageAsync();
var balance = await client.BillingBalanceAsync();
```

### Audio effects (Pro/Business)

```csharp
// Inline during synthesis — the chain is applied to the synthesized audio
byte[] audio = await client.SynthesizeAsync(
    "Привет!",
    effects: "[{\"type\":\"reverb\",\"room_size\":0.5},{\"type\":\"pitch\",\"semitones\":2}]");

// Async processing of an existing file
var job = await client.ApplyAudioEffectsAsync(
    "voice.mp3",
    new object[] { new { type = "compressor", ratio = 3 } });
var result = await client.GetAudioEffectsJobAsync(job!["job_id"]!.GetValue<string>());
while (result!["status"]!.GetValue<string>() is not ("completed" or "failed"))
{
    await Task.Delay(1000);
    result = await client.GetAudioEffectsJobAsync(job!["job_id"]!.GetValue<string>());
}
byte[] fx = await client.DownloadAudioEffectsAsync(job!["job_id"]!.GetValue<string>());
await File.WriteAllBytesAsync("voice_fx.mp3", fx);
```

### Audio cleaning (Pro/Business)

```csharp
// Denoise + normalize an existing file as a background job
var cleanJob = await client.CleanAudioAsync("noisy.wav");   // one-click preset
// or with options:
// var cleanJob = await client.CleanAudioAsync("noisy.wav", options: new { denoise = new { strength = 0.8 } });

var cleanResult = await client.GetAudioCleaningJobAsync(cleanJob!["job_id"]!.GetValue<string>());
while (cleanResult!["status"]!.GetValue<string>() is not ("completed" or "failed"))
{
    await Task.Delay(1000);
    cleanResult = await client.GetAudioCleaningJobAsync(cleanJob!["job_id"]!.GetValue<string>());
}
byte[] clean = await client.DownloadAudioCleaningAsync(cleanJob!["job_id"]!.GetValue<string>());
await File.WriteAllBytesAsync("voice_clean.wav", clean);
```

### Search & Q&A (Pro/Business)

```csharp
// Hybrid semantic/full-text search over your recordings
var hits = await client.SearchAsync("почему клиент отказался?", limit: 5, keywords: "дорого", source: "upload");
foreach (var hit in hits!["hits"]!.AsArray())
    Console.WriteLine($"{hit!["score"]} {hit!["start"]} {hit!["text"]}");

// RAG question with verbatim citations
var answer = await client.AskAsync("почему клиент отказался от Pro?");
Console.WriteLine(answer!["answer"]);
foreach (var c in answer!["citations"]!.AsArray())
    Console.WriteLine($"{c!["recording_id"]} {c!["start"]} {c!["quote"]}");
```

### WebSocket streaming (Pro/Business)

```csharp
await using var stream = await client.TranscribeStreamAsync(language: "ru", keyterms: new[] { "диагноз" });
await stream.SendAudioAsync(pcm16Chunk1); // raw PCM16, 16 kHz mono
await stream.SendAudioAsync(pcm16Chunk2);
await stream.StopAsync();                  // finalize the utterance
await foreach (var evt in stream.EventsAsync())
{
    Console.WriteLine(evt);               // session / vad / partial / final / error
}

await using var vad = await client.VadStreamAsync(); // VAD events only (speech_started/ended)
await vad.SendAudioAsync(pcm16Chunk);
await vad.StopAsync();
await foreach (var evt in vad.EventsAsync())
{
    Console.WriteLine(evt);
}
```

### Voice ID (Pro/Business)

```csharp
// Voice passport: language, gender, age, emotion, speaker embedding, AI-vs-human
var passport = await client.VoiceIdAsync("recording.wav");

// Voice biometrics on your own profiles
var profile = await client.EnrollVoiceAsync("speaker.wav", name: "Alice");
// → { "profile_id": "voice_…" }

var check = await client.VerifyVoiceAsync("check.wav", profile!["profile_id"]!.GetValue<string>());
// → { "profile_id": "voice_…", "similarity": 0.81, "verified": true, "threshold": 0.7 }

var match = await client.IdentifyVoiceAsync("check.wav");          // 1:N across your profiles
// → { "best_match": {…}, "matches": […], "threshold": 0.7 }

var profiles = await client.ListVoiceProfilesAsync();
await client.DeleteVoiceProfileAsync(profile!["profile_id"]!.GetValue<string>());
```

### Recordings, QA & meeting intelligence (Pro/Business)

```csharp
// Recordings library (list / link channel / speakers)
var recordings = await client.ListRecordingsAsync(limit: 10);
var job = await client.StartRecordingFromLinkAsync("https://example.com/call.mp3"); // Link channel
var speakers = await client.GetRecordingSpeakersAsync(recordingId);
await client.UpdateRecordingSpeakerAsync(recordingId, "SPEAKER_00", displayName: "Alice", role: "operator");

// Call QA
var qa = await client.QaEvaluateAsync(recordingId, new object[] { new { id = "greeting", kind = "required", description = "..." } });
var trend = await client.QaAnalyticsAsync(days: 30);
byte[] csv = await client.QaExportAsync(format: "csv");

// Meeting protocol
var protocol = await client.MeetingProtocolAsync(recordingId, template: "standup");

// Translation & speech evaluation
var translated = await client.TranslateTranscriptAsync(jobId, targetLanguage: "en");
var wer = await client.EvaluateAsync("audio.wav", reference: "Ожидаемый текст");
```

## Examples

A runnable console app lives in [`examples/`](./examples): synthesis, streaming,
transcription, analysis and voice cloning. It reads the `VOICEKIT_API_KEY` environment
variable.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `apiKey` | — | API key (required) |
| `baseUrl` | `https://ttsapi.ru` | API base URL (e.g. `http://localhost:5080` for local dev) |
| `timeout` | `120` s | Per-request timeout (`TimeSpan`) |

Errors throw `VoiceKitError` with `.Status` (HTTP status), `.Code` (machine-readable
code) and `.Message`. JSON responses are returned as `System.Text.Json.Nodes.JsonNode`;
index them with `node["job_id"]` and convert with `GetValue<T>()`.

## Documentation

Full API reference and guides: **[ttsapi.ru/docs](https://ttsapi.ru/docs)**.

## License

[MIT](./LICENSE)
