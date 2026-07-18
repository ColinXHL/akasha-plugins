using AkashaAutomation.Core.Diagnostics;

namespace AkashaAutomation.Core.Abstractions;

public interface IDiagnosticsSink
{
    void Write(DiagnosticEvent diagnosticEvent);
}
