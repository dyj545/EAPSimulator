using EAPSimulator.Core.Protocols.SecsGem.SecsII;

namespace EAPSimulator.Core.Protocols.SecsGem.SecsII;

public class SecsMessage
{
    public byte Stream { get; set; }
    public byte Function { get; set; }
    public bool WBit { get; set; }
    public uint SessionId { get; set; }
    public uint SystemBytes { get; set; }
    public SecsItem? RootItem { get; set; }

    public string MessageId => $"S{Stream}F{Function}";

    public SecsMessage() { }

    public SecsMessage(byte stream, byte function, bool wBit = false, SecsItem? item = null)
    {
        Stream = stream;
        Function = function;
        WBit = wBit;
        RootItem = item;
    }

    public byte[] EncodeToSecsII()
    {
        var itemData = RootItem?.Encode() ?? Array.Empty<byte>();
        int dataLength = 10 + itemData.Length; // 10 bytes for header before items

        using var ms = new MemoryStream();
        // 10-byte SECS-II message header
        ms.WriteByte((byte)(SessionId >> 8));
        ms.WriteByte((byte)SessionId);
        ms.WriteByte(0); // Header byte 2 (reserved)
        ms.WriteByte((byte)((Stream << 1) | (WBit ? 1 : 0)));
        ms.WriteByte(Function);
        ms.WriteByte(0); // PType
        ms.WriteByte((byte)(SystemBytes >> 24));
        ms.WriteByte((byte)(SystemBytes >> 16));
        ms.WriteByte((byte)(SystemBytes >> 8));
        ms.WriteByte((byte)SystemBytes);

        ms.Write(itemData, 0, itemData.Length);
        return ms.ToArray();
    }

    public static SecsMessage DecodeFromSecsII(byte[] data)
    {
        if (data.Length < 10)
            throw new InvalidOperationException("SECS-II message too short");

        var msg = new SecsMessage
        {
            SessionId = (uint)((data[0] << 8) | data[1]),
            Stream = (byte)(data[3] >> 1),
            WBit = (data[3] & 0x01) != 0,
            Function = data[4],
            SystemBytes = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9]),
        };

        if (data.Length > 10)
        {
            int offset = 10;
            msg.RootItem = SecsItem.Decode(data, ref offset);
        }

        return msg;
    }

    public string ToFormattedString()
    {
        var dir = WBit ? "W" : "";
        var itemStr = RootItem?.ToString() ?? "(no data)";
        return $"S{Stream}F{Function}{dir} [{SystemBytes}] {itemStr}";
    }

    public string ToFormattedTree(string? description = null, string? direction = null)
    {
        var sb = new System.Text.StringBuilder();

        // System bytes line
        if (direction != null)
        {
            var sysBytesStr = $"{SystemBytes:X8}";
            var formatted = string.Join(" ", Enumerable.Range(0, 4).Select(i => sysBytesStr.Substring(i * 2, 2)));
            sb.AppendLine($"* {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {direction}: [System Bytes: {formatted}]");
        }

        // Message line
        var wbitStr = WBit ? " W" : "";
        var descPart = description != null ? $" * {description}" : "";
        sb.AppendLine($"S{Stream}F{Function}{wbitStr}{descPart}");

        // Tree
        if (RootItem != null)
        {
            AppendItem(sb, RootItem, 0);
            sb.AppendLine(">.");
        }
        else
        {
            sb.AppendLine("(no data)");
            sb.AppendLine(">.");
        }

        return sb.ToString();
    }

    private static void AppendItem(System.Text.StringBuilder sb, SecsItem item, int indent)
    {
        var indentStr = new string('\t', indent);

        if (item is SecsList list)
        {
            // Compute length bytes count for the list count
            var lengthBytes = list.Items.Length <= 0xFF ? 1 : (list.Items.Length <= 0xFFFF ? 2 : 3);
            sb.AppendLine($"{indentStr}<L [{list.Items.Length}/{lengthBytes}]>");

            foreach (var child in list.Items)
            {
                AppendItem(sb, child, indent + 1);
            }

            // Closing bracket for this list
            sb.AppendLine($"{indentStr}>");
        }
        else
        {
            var valueStr = GetValueString(item);
            var formatAbbrev = GetFormatAbbrev(item.Format);

            // Compute byte length and length bytes count
            int byteLength = GetByteLength(item);
            var lengthBytes = byteLength <= 0xFF ? 1 : (byteLength <= 0xFFFF ? 2 : 3);

            sb.AppendLine($"{indentStr}<{formatAbbrev} [{byteLength}/{lengthBytes}] {valueStr}>");
        }
    }

    private static string GetFormatAbbrev(SecsFormat format) => format switch
    {
        SecsFormat.List => "L",
        SecsFormat.ASCII => "A",
        SecsFormat.Binary => "B",
        SecsFormat.Boolean => "Boolean",
        SecsFormat.U1 => "U1",
        SecsFormat.U2 => "U2",
        SecsFormat.U4 => "U4",
        SecsFormat.U8 => "U8",
        SecsFormat.I1 => "I1",
        SecsFormat.I2 => "I2",
        SecsFormat.I4 => "I4",
        SecsFormat.I8 => "I8",
        SecsFormat.F4 => "F4",
        SecsFormat.F8 => "F8",
        _ => "?"
    };

    private static int GetByteLength(SecsItem item)
    {
        return item switch
        {
            SecsAscii a => a.Value.Length,
            SecsBinary b => b.Value.Length,
            SecsBoolean => 1,
            SecsU1 u1 => u1.Value.Length,
            SecsU2 u2 => u2.Value.Length * 2,
            SecsU4 u4 => u4.Value.Length * 4,
            SecsU8 u8 => u8.Value.Length * 8,
            SecsI1 i1 => i1.Value.Length,
            SecsI2 i2 => i2.Value.Length * 2,
            SecsI4 i4 => i4.Value.Length * 4,
            SecsI8 i8 => i8.Value.Length * 8,
            SecsF4 f4 => f4.Value.Length * 4,
            SecsF8 f8 => f8.Value.Length * 8,
            _ => 0
        };
    }

    private static string GetValueString(SecsItem item)
    {
        switch (item)
        {
            case SecsAscii a:
                return $"'{a.Value}'";
            case SecsBinary b:
                return string.Join(" ", b.Value.Select(bt => $"{bt:X2}"));
            case SecsBoolean bo:
                return bo.Value ? "True" : "False";
            case SecsU1 u1:
                return u1.Value.Length == 1 ? u1.Value[0].ToString() : $"[{string.Join(", ", u1.Value)}]";
            case SecsU2 u2:
                return u2.Value.Length == 1 ? u2.Value[0].ToString() : $"[{string.Join(", ", u2.Value)}]";
            case SecsU4 u4:
                return u4.Value.Length == 1 ? u4.Value[0].ToString() : $"[{string.Join(", ", u4.Value)}]";
            case SecsU8 u8:
                return u8.Value.Length == 1 ? u8.Value[0].ToString() : $"[{string.Join(", ", u8.Value)}]";
            case SecsI1 i1:
                return i1.Value.Length == 1 ? i1.Value[0].ToString() : $"[{string.Join(", ", i1.Value)}]";
            case SecsI2 i2:
                return i2.Value.Length == 1 ? i2.Value[0].ToString() : $"[{string.Join(", ", i2.Value)}]";
            case SecsI4 i4:
                return i4.Value.Length == 1 ? i4.Value[0].ToString() : $"[{string.Join(", ", i4.Value)}]";
            case SecsI8 i8:
                return i8.Value.Length == 1 ? i8.Value[0].ToString() : $"[{string.Join(", ", i8.Value)}]";
            case SecsF4 f4:
                return f4.Value.Length == 1 ? f4.Value[0].ToString() : $"[{string.Join(", ", f4.Value)}]";
            case SecsF8 f8:
                return f8.Value.Length == 1 ? f8.Value[0].ToString() : $"[{string.Join(", ", f8.Value)}]";
            default:
                return item.ToString();
        }
    }

    public override string ToString() => $"S{Stream}F{Function}";
}
