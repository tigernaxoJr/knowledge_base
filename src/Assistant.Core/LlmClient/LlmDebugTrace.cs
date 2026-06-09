using System.Diagnostics;
using System.Threading;

namespace Assistant.Core.LlmClient;

public sealed record LlmDebugEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Status { get; init; } = "started";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public long? DurationMs { get; init; }
    public string? Endpoint { get; init; }
    public string? Model { get; init; }
    public int? InputCount { get; init; }
    public int? InputChars { get; init; }
    public int? SystemPromptChars { get; init; }
    public int? UserMessageChars { get; init; }
    public int? ResponseChars { get; init; }
    public string? Preview { get; init; }
    public string? Error { get; init; }
    public string? RequestPayload { get; init; }
}

public sealed class LlmDebugTrace
{
    private readonly List<LlmDebugEvent> _events = [];
    private readonly object _lock = new();
    private readonly Action<LlmDebugEvent>? _onEvent;

    public LlmDebugTrace(Action<LlmDebugEvent>? onEvent = null)
    {
        _onEvent = onEvent;
    }

    public IReadOnlyList<LlmDebugEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
    }

    internal void Add(LlmDebugEvent item)
    {
        lock (_lock)
        {
            _events.Add(item);
        }

        _onEvent?.Invoke(item);
    }
}

public static class LlmDebugScope
{
    private static readonly AsyncLocal<LlmDebugTrace?> CurrentTrace = new();

    public static LlmDebugTrace? Current => CurrentTrace.Value;

    public static IDisposable Begin(LlmDebugTrace trace)
    {
        var previous = CurrentTrace.Value;
        CurrentTrace.Value = trace;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(LlmDebugTrace? previous) : IDisposable
    {
        public void Dispose() => CurrentTrace.Value = previous;
    }
}

internal sealed class LlmDebugCall : IDisposable
{
    private readonly LlmDebugTrace? _trace;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly LlmDebugEvent _started;
    private bool _completed;

    private LlmDebugCall(LlmDebugTrace? trace, LlmDebugEvent started)
    {
        _trace = trace;
        _started = started;
        _trace?.Add(started);
    }

    public static LlmDebugCall Start(LlmDebugEvent started) =>
        new(LlmDebugScope.Current, started with { Status = "started", StartedAt = DateTimeOffset.UtcNow });

    public void Succeed(string? preview = null, int? responseChars = null)
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();
        _trace?.Add(_started with
        {
            Status = "completed",
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = _stopwatch.ElapsedMilliseconds,
            Preview = preview,
            ResponseChars = responseChars
        });
    }

    public void Fail(Exception ex)
    {
        if (_completed) return;
        _completed = true;
        _stopwatch.Stop();
        _trace?.Add(_started with
        {
            Status = "failed",
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = _stopwatch.ElapsedMilliseconds,
            Error = ex.Message
        });
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _stopwatch.Stop();
            _trace?.Add(_started with
            {
                Status = "abandoned",
                CompletedAt = DateTimeOffset.UtcNow,
                DurationMs = _stopwatch.ElapsedMilliseconds
            });
        }
    }
}
