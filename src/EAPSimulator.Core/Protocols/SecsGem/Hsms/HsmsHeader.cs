namespace EAPSimulator.Core.Protocols.SecsGem.Hsms;

/// <summary>
/// HSMS message types per SEMI E37.1
/// </summary>
public enum HsmsMessageType : byte
{
    Data = 0x00,
    SelectReq = 0x01,
    SelectRsp = 0x02,
    DeselectReq = 0x03,
    DeselectRsp = 0x04,
    LinkTestReq = 0x05,
    LinkTestRsp = 0x06,
    RejectReq = 0x07,
    SeparateReq = 0x09,
}

/// <summary>
/// HSMS 14-byte header per SEMI E37.1.
/// For Data messages: bytes 6-7 carry Stream/Function/W-bit, PType=0x00, SType=0x00.
/// For Control messages: bytes 6-7 are 0, PType=0x00, SType=control_type.
/// </summary>
public class HsmsHeader
{
    public const int HeaderLength = 14;

    public uint Length { get; set; }
    public ushort SessionId { get; set; }
    public byte HeaderByte2 { get; set; }
    public byte PType { get; set; }
    public HsmsMessageType SType { get; set; }
    public uint SystemBytes { get; set; }

    // For Data messages
    public byte Stream
    {
        get => (byte)((HeaderByte2 >> 1) & 0x3F);
        set => HeaderByte2 = (byte)((HeaderByte2 & 0x81) | ((value & 0x3F) << 1));
    }

    public bool WBit
    {
        get => (HeaderByte2 & 0x80) != 0;
        set => HeaderByte2 = (byte)((HeaderByte2 & 0x7F) | (value ? 0x80 : 0));
    }

    public byte Function { get; set; }

    public bool IsDataMessage => SType == HsmsMessageType.Data;

    public byte[] Encode()
    {
        var bytes = new byte[HeaderLength];
        // Length (4 bytes)
        bytes[0] = (byte)(Length >> 24);
        bytes[1] = (byte)(Length >> 16);
        bytes[2] = (byte)(Length >> 8);
        bytes[3] = (byte)Length;
        // Session ID (2 bytes)
        bytes[4] = (byte)(SessionId >> 8);
        bytes[5] = (byte)SessionId;

        if (IsDataMessage)
        {
            // Data message: HeaderByte2, Function, PType=0, SType=0
            bytes[6] = HeaderByte2;
            bytes[7] = Function;
            bytes[8] = 0x00; // PType
            bytes[9] = 0x00; // SType = Data
        }
        else
        {
            // Control message: bytes 6-7 are 0, PType, SType
            bytes[6] = 0x00;
            bytes[7] = 0x00;
            bytes[8] = PType;
            bytes[9] = (byte)SType;
        }

        // SystemBytes (4 bytes)
        bytes[10] = (byte)(SystemBytes >> 24);
        bytes[11] = (byte)(SystemBytes >> 16);
        bytes[12] = (byte)(SystemBytes >> 8);
        bytes[13] = (byte)SystemBytes;
        return bytes;
    }

    public static HsmsHeader Decode(byte[] data, int offset = 0)
    {
        if (data.Length - offset < HeaderLength)
            throw new InvalidOperationException("HSMS header too short");

        var header = new HsmsHeader
        {
            Length = (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]),
            SessionId = (ushort)((data[offset + 4] << 8) | data[offset + 5]),
            HeaderByte2 = data[offset + 6],
            PType = data[offset + 8],
            SType = (HsmsMessageType)data[offset + 9],
            SystemBytes = (uint)((data[offset + 10] << 24) | (data[offset + 11] << 16) | (data[offset + 12] << 8) | data[offset + 13]),
        };

        if (header.IsDataMessage)
        {
            // Data: byte 6 = HeaderByte2 (stream + W), byte 7 = Function
            header.HeaderByte2 = data[offset + 6];
            header.Function = data[offset + 7];
        }

        return header;
    }

    public override string ToString()
    {
        if (IsDataMessage)
            return $"S{Stream}F{Function}{(WBit ? "W" : "")} Dev={SessionId} Sys={SystemBytes}";
        return $"[{SType}] Dev={SessionId} Sys={SystemBytes}";
    }
}
