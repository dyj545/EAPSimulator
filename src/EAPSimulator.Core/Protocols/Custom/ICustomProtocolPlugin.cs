namespace EAPSimulator.Core.Protocols.Custom;

/// <summary>
/// Plugin interface for custom protocol extensions.
/// Implement this to add custom message handling logic.
/// </summary>
public interface ICustomProtocolPlugin
{
    string Name { get; }

    /// <summary>
    /// Called when the protocol is initialized. Register custom message handlers here.
    /// </summary>
    void Initialize(CustomProtocol protocol);

    /// <summary>
    /// Called when a message is received. Return a response message or null.
    /// </summary>
    Task<ProtocolMessage?> HandleMessageAsync(ProtocolMessage message, CancellationToken ct);
}
