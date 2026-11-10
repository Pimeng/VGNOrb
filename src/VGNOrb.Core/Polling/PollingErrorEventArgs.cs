namespace VGNOrb.Core.Polling;

public sealed class PollingErrorEventArgs(Exception error) : EventArgs
{
    public Exception Error { get; } = error;
}
