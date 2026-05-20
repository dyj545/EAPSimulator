namespace EAPSimulator.Core.Protocols;

public interface IProtocol : IAsyncDisposable
{
    string Name { get; }
    ProtocolRole Role { get; set; }
    ProtocolState State { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SendAsync(ProtocolMessage message, CancellationToken ct);
    event EventHandler<ProtocolMessageEventArgs> MessageReceived;
    event EventHandler<ProtocolMessageEventArgs> MessageSent;
    event EventHandler<ProtocolStateEventArgs> StateChanged;
}
