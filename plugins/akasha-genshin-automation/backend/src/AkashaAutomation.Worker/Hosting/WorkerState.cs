namespace AkashaAutomation.Worker.Hosting;

public enum WorkerState
{
    Created,
    Connecting,
    Handshaking,
    Ready,
    Running,
    Stopping,
    Stopped,
}
