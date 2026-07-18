using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Core.Diagnostics;

public sealed class InMemoryDiagnosticsSink : IDiagnosticsSink
{
    private readonly object _gate = new();
    private readonly List<DiagnosticEvent> _events = [];

    public IReadOnlyList<DiagnosticEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        lock (_gate)
        {
            _events.Add(diagnosticEvent);
        }
    }
}
