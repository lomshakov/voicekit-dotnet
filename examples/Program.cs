using System.Text.Json.Nodes;
using VoiceKit;

// Usage:
//   dotnet run -- synthesize
//   dotnet run -- stream
//   dotnet run -- transcribe
//   dotnet run -- analyze
//   dotnet run -- clone

var apiKey = Environment.GetEnvironmentVariable("VOICEKIT_API_KEY")
    ?? throw new InvalidOperationException("Set VOICEKIT_API_KEY environment variable.");

using var client = new VoiceKitClient(apiKey);
var command = args.Length > 0 ? args[0] : "synthesize";

switch (command)
{
    case "synthesize":
        await SynthesizeAsync(client);
        break;
    case "stream":
        await StreamAsync(client);
        break;
    case "transcribe":
        await TranscribeAsync(client);
        break;
    case "analyze":
        await AnalyzeAsync(client);
        break;
    case "clone":
        await CloneAsync(client);
        break;
    default:
        Console.WriteLine("Unknown command: " + command);
        break;
}

static async Task SynthesizeAsync(VoiceKitClient client)
{
    byte[] audio = await client.SynthesizeAsync(
        "Привет! Это синтез русской речи через VoiceKit.",
        voice: "preset_anna",
        format: "mp3");
    await File.WriteAllBytesAsync("speech.mp3", audio);
    Console.WriteLine($"saved speech.mp3 ({audio.Length} bytes)");
}

static async Task StreamAsync(VoiceKitClient client)
{
    await using var output = File.Create("speech_stream.mp3");
    await foreach (var chunk in client.SynthesizeStreamAsync("Первое предложение. Второе. Третье."))
    {
        await output.WriteAsync(chunk);
    }
    Console.WriteLine("saved speech_stream.mp3");
}

static async Task TranscribeAsync(VoiceKitClient client)
{
    var job = await client.TranscribeAsync("meeting.wav", language: "ru", diarization: true);
    var jobId = job!["job_id"]!.GetValue<string>();

    JsonNode? result;
    while (true)
    {
        result = await client.GetTranscriptionJobAsync(jobId);
        if (result!["status"]!.GetValue<string>() is "completed" or "failed")
            break;
        await Task.Delay(2000);
    }

    Console.WriteLine(result!["transcript"]!.GetValue<string>());
    foreach (var segment in result["segments"]?.AsArray() ?? new JsonArray())
    {
        var speaker = segment?["speaker"]?.GetValue<string>() ?? "";
        Console.WriteLine(
            $"[{segment!["start"]!.GetValue<double>():F2}–{segment!["end"]!.GetValue<double>():F2}] {speaker}: {segment!["text"]!.GetValue<string>()}");
    }
}

static async Task AnalyzeAsync(VoiceKitClient client)
{
    var transcript = await client.TranscribeSyncAsync("audio.wav");
    Console.WriteLine("transcript: " + transcript!["transcript"]!.GetValue<string>());

    var analysis = await client.AnalyzeSyncAsync("audio.wav");
    Console.WriteLine("analysis: " + analysis?.ToJsonString());
}

static async Task CloneAsync(VoiceKitClient client)
{
    var clone = await client.CreateCloneVoiceAsync(
        name: "My voice",
        promptText: "Точный текст образца.",
        sample: "reference.wav");
    var cloneId = clone!["id"]!.GetValue<string>();
    Console.WriteLine("clone: " + cloneId);

    var voices = await client.ListCloneVoicesAsync();
    Console.WriteLine("voices: " + voices?.ToJsonString());

    await client.DeleteCloneVoiceAsync(cloneId);
    Console.WriteLine("deleted " + cloneId);
}
