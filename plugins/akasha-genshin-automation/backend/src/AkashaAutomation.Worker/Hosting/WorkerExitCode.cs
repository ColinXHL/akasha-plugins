namespace AkashaAutomation.Worker.Hosting;

public enum WorkerExitCode
{
    Success = 0,
    InvalidArguments = 2,
    ParentProcessUnavailable = 3,
    ConnectionFailed = 4,
    HandshakeRejected = 5,
    ProtocolError = 6,
    UnexpectedError = 10,
}
