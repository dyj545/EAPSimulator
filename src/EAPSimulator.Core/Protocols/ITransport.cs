namespace EAPSimulator.Core.Protocols;

public interface ITransport : IAsyncDisposable
{
    bool IsConnected { get; }
    string? RemoteEndpoint { get; }
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();
    Task SendAsync(byte[] data, CancellationToken ct);
    event EventHandler<byte[]> DataReceived;
    event EventHandler<string?> Disconnected;
}
