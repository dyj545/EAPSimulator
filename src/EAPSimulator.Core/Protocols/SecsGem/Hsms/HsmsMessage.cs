using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.Hsms;

public class HsmsMessage
{
    public HsmsHeader Header { get; set; } = new();
    public byte[]? Data { get; set; } // SECS-II item payload (Data messages only)

    public bool IsDataMessage => Header.IsDataMessage;
    public bool IsControlMessage => !IsDataMessage;

    /// <summary>
    /// Create a Data message from a SecsMessage.
    /// HSMS framing: 14-byte header + SECS-II item data (no separate SECS header).
    /// </summary>
    public static HsmsMessage CreateDataMessage(SecsMessage secsMsg, ushort sessionId, uint systemBytes)
    {
        var itemData = secsMsg.RootItem?.Encode() ?? Array.Empty<byte>();

        var msg = new HsmsMessage
        {
            Header = new HsmsHeader
            {
                SessionId = sessionId,
                Stream = secsMsg.Stream,
                Function = secsMsg.Function,
                WBit = secsMsg.WBit,
                SType = HsmsMessageType.Data,
                SystemBytes = systemBytes,
                Length = (uint)(10 + itemData.Length), // 10 = header bytes after Length field
            },
            Data = itemData,
        };

        return msg;
    }

    /// <summary>
    /// Create a Control message (Select, LinkTest, etc.)
    /// </summary>
    public static HsmsMessage CreateControlMessage(HsmsMessageType type, ushort sessionId, uint systemBytes)
    {
        return new HsmsMessage
        {
            Header = new HsmsHeader
            {
                Length = 10, // Control messages have no payload
                SessionId = sessionId,
                SType = type,
                SystemBytes = systemBytes,
            },
        };
    }

    /// <summary>
    /// Encode to wire format: 14-byte header + optional data
    /// </summary>
    public byte[] Encode()
    {
        var headerBytes = Header.Encode();
        if (Data == null || Data.Length == 0)
            return headerBytes;

        var result = new byte[headerBytes.Length + Data.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        Buffer.BlockCopy(Data, 0, result, headerBytes.Length, Data.Length);
        return result;
    }

    /// <summary>
    /// Decode a SecsMessage from this HSMS Data message
    /// </summary>
    public SecsMessage? DecodeSecsMessage()
    {
        if (!IsDataMessage || Data == null || Data.Length == 0)
            return null;

        int offset = 0;
        var rootItem = SecsItem.Decode(Data, ref offset);

        return new SecsMessage
        {
            Stream = Header.Stream,
            Function = Header.Function,
            WBit = Header.WBit,
            SessionId = Header.SessionId,
            SystemBytes = Header.SystemBytes,
            RootItem = rootItem,
        };
    }

    public string ToFormattedString()
    {
        if (IsControlMessage)
            return $"[HSMS {Header.SType}] Dev={Header.SessionId} Sys={Header.SystemBytes}";

        var secsMsg = DecodeSecsMessage();
        if (secsMsg != null)
            return secsMsg.ToFormattedString();

        return $"[HSMS Data] Dev={Header.SessionId} Sys={Header.SystemBytes} (no items)";
    }
}
