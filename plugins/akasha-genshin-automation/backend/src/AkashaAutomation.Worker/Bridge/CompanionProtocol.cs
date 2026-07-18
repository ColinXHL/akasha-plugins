using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkashaAutomation.Worker.Bridge;

public static class CompanionProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumPayloadBytes = 256 * 1024;

    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Request = "request";
    public const string Response = "response";
    public const string Event = "event";
    public const string Shutdown = "shutdown";

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record CompanionEnvelope
{
    public required string Type { get; init; }

    public string? CorrelationId { get; init; }

    public string? Method { get; init; }

    public JsonElement? Payload { get; init; }

    public int? ProtocolVersion { get; init; }

    public string? Token { get; init; }

    public string? WorkerVersion { get; init; }

    public int? ParentProcessId { get; init; }

    public bool? Accepted { get; init; }

    public CompanionError? Error { get; init; }
}

public sealed record CompanionError(string Code, string Message);

public sealed record WorkerStatus(
    string State,
    int ProtocolVersion,
    string WorkerVersion,
    int ParentProcessId,
    DateTimeOffset StartedAtUtc,
    bool RealInputEnabled,
    EmergencyStopStatus EmergencyStop,
    GameWindowStatus GameWindow,
    SubsystemStatus Capture,
    SubsystemStatus Ocr,
    FeatureStatuses Features,
    WorkerErrorStatus? LastError);

public sealed record EmergencyStopStatus(
    bool IsActive,
    string? Reason,
    DateTimeOffset? TriggeredAtUtc);

public sealed record GameWindowStatus(
    string State,
    bool IsAvailable,
    int? ProcessId,
    string? Title);

public sealed record SubsystemStatus(
    string State,
    bool IsAvailable);

public sealed record FeatureStatus(
    bool IsEnabled,
    bool IsRunning)
{
    public AutoPickRecognitionStatus? Recognition { get; init; }

    public AutoDialogueRecognitionStatus? DialogueRecognition { get; init; }
}

public sealed record AutoPickRecognitionStatus(
    string? Text,
    string Reason,
    bool IntentSubmitted,
    long? FrameSequence,
    DateTimeOffset? UpdatedAtUtc);

public sealed record AutoDialogueRecognitionStatus(
    string UiCategory,
    IReadOnlyList<string> Options,
    string Reason,
    bool IntentSubmitted,
    bool VoiceWaitActive,
    bool VoiceWaitFallback,
    long? FrameSequence,
    DateTimeOffset? UpdatedAtUtc);

public sealed record FeatureStatuses(
    FeatureStatus AutoPick,
    FeatureStatus AutoDialogue);

public sealed record WorkerErrorStatus(
    string Code,
    string Message,
    DateTimeOffset OccurredAtUtc);
