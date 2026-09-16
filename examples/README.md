# C# / .NET examples

A runnable console app. Requires a `VOICEKIT_API_KEY` environment variable.

```bash
export VOICEKIT_API_KEY="rtt_…"   # PowerShell: $env:VOICEKIT_API_KEY = "rtt_…"
dotnet run -- synthesize
```

| Command | What it does |
| --- | --- |
| `dotnet run -- synthesize` | One-shot synthesis → `speech.mp3` |
| `dotnet run -- stream` | Streaming synthesis (Pro/Business) → `speech_stream.mp3` |
| `dotnet run -- transcribe` | Async transcription + diarization (speaker labels) |
| `dotnet run -- analyze` | Sync transcription + sentiment analysis |
| `dotnet run -- clone` | Voice cloning (Pro/Business) |
