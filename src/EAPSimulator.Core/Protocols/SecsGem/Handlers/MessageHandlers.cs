using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.Handlers;

public interface ISecsMessageHandler
{
    Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct);
}

/// <summary>
/// S1F1 (Are You There) / S1F2
/// Equipment sends S1F1 → Host replies S1F2.
/// </summary>
public class S1F1Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Host receives S1F1 from Equipment, replies S1F2
        if (role == ProtocolRole.Host)
        {
            var reply = new SecsMessage(1, 2, false,
                SecsItem.L(
                    SecsItem.A(model.ModelName),
                    SecsItem.A(model.SoftwareRevision)
                ));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S1F13 (Establish Communication) / S1F14
/// Host sends S1F13 → Equipment replies S1F14.
/// Equipment sends S1F13 → Host replies S1F14.
/// </summary>
public class S1F13Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        if (model.AcceptCommunication)
        {
            // ACK: COMMACK=0, include MDLN and SOFREV
            var reply = new SecsMessage(1, 14, false,
                SecsItem.L(
                    SecsItem.U1(0),           // COMMACK = 0 (success)
                    SecsItem.L(
                        SecsItem.A(model.ModelName),
                        SecsItem.A(model.SoftwareRevision)
                    )
                ));
            return Task.FromResult<SecsMessage?>(reply);
        }
        else
        {
            // NACK: COMMACK=1, empty MDLN/SOFREV list
            var reply = new SecsMessage(1, 14, false,
                SecsItem.L(
                    SecsItem.U1(1),           // COMMACK = 1 (denied)
                    SecsItem.L()              // empty — no equipment identity
                ));
            return Task.FromResult<SecsMessage?>(reply);
        }
    }
}

/// <summary>
/// S1F11 (Status Variable Namelist Request) / S1F12
/// Host sends S1F11 → Equipment replies S1F12.
/// </summary>
public class S1F11Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Equipment receives S1F11 from Host, replies S1F12
        if (role == ProtocolRole.Equipment)
        {
            var svItems = model.StatusVariables.Select(sv =>
                SecsItem.L(
                    SecsItem.U2(sv.Svid),
                    SecsItem.A(sv.Name),
                    SecsItem.A(sv.Unit)
                )).ToArray();

            var reply = new SecsMessage(1, 12, false, SecsItem.L(svItems));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S1F2 (Are You There Ack) — Equipment receives reply from Host.
/// </summary>
public class S1F2Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Equipment receives S1F2 from Host, no reply needed
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S1F14 (Establish Communication Ack) — sender receives reply.
/// </summary>
public class S1F14Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Received S1F14, check COMMACK
        var commAck = request.RootItem is SecsItem item
            ? "ACK=0"
            : "ACK=?";
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S2F13 (Process Program Send) / S2F14
/// Host sends S2F13 → Equipment replies S2F14.
/// </summary>
public class S2F13Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        if (role == ProtocolRole.Equipment)
        {
            var reply = new SecsMessage(2, 14, false, SecsItem.U1(0));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S2F41 (Host Command) / S2F42
/// Host sends S2F41 → Equipment replies S2F42.
/// </summary>
public class S2F41Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        if (role == ProtocolRole.Equipment)
        {
            var reply = new SecsMessage(2, 42, false,
                SecsItem.L(
                    SecsItem.U1(0),    // HCACK = 0 (success)
                    SecsItem.L()       // PARAMA (empty)
                ));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S5F1 (Alarm Report) / S5F2
/// Equipment sends S5F1 → Host replies S5F2.
/// </summary>
public class S5F1Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Host receives S5F1 from Equipment, replies S5F2
        if (role == ProtocolRole.Host)
        {
            var reply = new SecsMessage(5, 2, false, SecsItem.U1(0));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S6F11 (Collection Event Report) / S6F12
/// Equipment sends S6F11 → Host replies S6F12.
/// </summary>
public class S6F11Handler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        // Host receives S6F11 from Equipment, replies S6F12
        if (role == ProtocolRole.Host)
        {
            var reply = new SecsMessage(6, 12, false, SecsItem.U1(0));
            return Task.FromResult<SecsMessage?>(reply);
        }
        return Task.FromResult<SecsMessage?>(null);
    }
}

/// <summary>
/// S2F14, S2F42, S5F2, S6F12 — reply handlers (no further reply needed)
/// </summary>
public class NoReplyHandler : ISecsMessageHandler
{
    public Task<SecsMessage?> HandleAsync(SecsMessage request, EquipmentModel model, ProtocolRole role, CancellationToken ct)
    {
        return Task.FromResult<SecsMessage?>(null);
    }
}
