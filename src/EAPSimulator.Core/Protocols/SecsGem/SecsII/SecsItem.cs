namespace EAPSimulator.Core.Protocols.SecsGem.SecsII;

public enum SecsFormat : byte
{
    List = 0x00,
    Binary = 0x20,
    Boolean = 0x24,
    ASCII = 0x40,
    JIS8 = 0x44,
    I8 = 0x60,
    I1 = 0x64,
    I2 = 0x68,
    I4 = 0x70,
    F8 = 0x80,
    F4 = 0x90,
    U8 = 0xA0,
    U1 = 0xA4,
    U2 = 0xA8,
    U4 = 0xB0,
}

public abstract class SecsItem
{
    public SecsFormat Format { get; }

    protected SecsItem(SecsFormat format)
    {
        Format = format;
    }

    public abstract byte[] Encode();
    public abstract int GetEncodedLength();
    public abstract override string ToString();

    public static SecsItem L(params SecsItem[] items) => new SecsList(items);
    public static SecsItem A(string value) => new SecsAscii(value);
    public static SecsItem B(params byte[] value) => new SecsBinary(value);
    public static SecsItem Boolean(bool value) => new SecsBoolean(value);
    public static SecsItem U1(params byte[] value) => new SecsU1(value);
    public static SecsItem U2(params ushort[] value) => new SecsU2(value);
    public static SecsItem U4(params uint[] value) => new SecsU4(value);
    public static SecsItem U8(params ulong[] value) => new SecsU8(value);
    public static SecsItem I1(params sbyte[] value) => new SecsI1(value);
    public static SecsItem I2(params short[] value) => new SecsI2(value);
    public static SecsItem I4(params int[] value) => new SecsI4(value);
    public static SecsItem I8(params long[] value) => new SecsI8(value);
    public static SecsItem F4(params float[] value) => new SecsF4(value);
    public static SecsItem F8(params double[] value) => new SecsF8(value);

    /// <summary>
    /// Clean up ASCII string - remove trailing NUL characters and other control characters.
    /// </summary>
    internal static string CleanAsciiString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Find first NUL character and truncate there
        int nulIndex = input.IndexOf('\0');
        if (nulIndex >= 0)
            input = input.Substring(0, nulIndex);

        // Remove other control characters except CR and LF
        var sb = new System.Text.StringBuilder();
        foreach (char c in input)
        {
            if (!char.IsControl(c) || c == '\r' || c == '\n')
                sb.Append(c);
        }
        return sb.ToString();
    }

    protected static byte[] EncodeHeader(SecsFormat format, int length)
    {
        if (length <= 0xFF)
            return new byte[] { (byte)((byte)format | 0x01), (byte)length };
        if (length <= 0xFFFF)
            return new byte[] { (byte)((byte)format | 0x02), (byte)(length >> 8), (byte)length };
        return new byte[] { (byte)((byte)format | 0x03), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
    }

    public static SecsItem Decode(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
            throw new InvalidOperationException("SECS item decode: insufficient data");

        byte headerByte = data[offset++];
        var format = (SecsFormat)(headerByte & 0xFC);
        int lengthBytes = headerByte & 0x03;

        if (lengthBytes == 0)
            throw new InvalidOperationException("SECS item decode: invalid length bytes");

        int length = 0;
        for (int i = 0; i < lengthBytes; i++)
        {
            if (offset >= data.Length)
                throw new InvalidOperationException("SECS item decode: insufficient data for length");
            length = (length << 8) | data[offset++];
        }

        if (format == SecsFormat.List)
        {
            var items = new List<SecsItem>();
            for (int i = 0; i < length; i++)
                items.Add(Decode(data, ref offset));
            return new SecsList(items.ToArray());
        }

        if (offset + length > data.Length)
            throw new InvalidOperationException("SECS item decode: insufficient data for value");

        var valueData = data.Slice(offset, length).ToArray();
        offset += length;

        return format switch
        {
            SecsFormat.ASCII => new SecsAscii(CleanAsciiString(System.Text.Encoding.ASCII.GetString(valueData))),
            SecsFormat.Binary => new SecsBinary(valueData),
            SecsFormat.Boolean => new SecsBoolean(valueData.Length > 0 && valueData[0] != 0),
            SecsFormat.U1 => new SecsU1(valueData),
            SecsFormat.U2 => DecodeU2(valueData),
            SecsFormat.U4 => DecodeU4(valueData),
            SecsFormat.U8 => DecodeU8(valueData),
            SecsFormat.I1 => new SecsI1(valueData.Select(b => (sbyte)b).ToArray()),
            SecsFormat.I2 => DecodeI2(valueData),
            SecsFormat.I4 => DecodeI4(valueData),
            SecsFormat.I8 => DecodeI8(valueData),
            SecsFormat.F4 => DecodeF4(valueData),
            SecsFormat.F8 => DecodeF8(valueData),
            _ => new SecsBinary(valueData),
        };
    }

    private static SecsU2 DecodeU2(byte[] data)
    {
        var values = new ushort[data.Length / 2];
        for (int i = 0; i < values.Length; i++)
            values[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
        return new SecsU2(values);
    }

    private static SecsU4 DecodeU4(byte[] data)
    {
        var values = new uint[data.Length / 4];
        for (int i = 0; i < values.Length; i++)
            values[i] = (uint)((data[i * 4] << 24) | (data[i * 4 + 1] << 16) | (data[i * 4 + 2] << 8) | data[i * 4 + 3]);
        return new SecsU4(values);
    }

    private static SecsU8 DecodeU8(byte[] data)
    {
        var values = new ulong[data.Length / 8];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = ((ulong)data[i * 8] << 56) | ((ulong)data[i * 8 + 1] << 48) |
                        ((ulong)data[i * 8 + 2] << 40) | ((ulong)data[i * 8 + 3] << 32) |
                        ((ulong)data[i * 8 + 4] << 24) | ((ulong)data[i * 8 + 5] << 16) |
                        ((ulong)data[i * 8 + 6] << 8) | data[i * 8 + 7];
        }
        return new SecsU8(values);
    }

    private static SecsI2 DecodeI2(byte[] data)
    {
        var values = new short[data.Length / 2];
        for (int i = 0; i < values.Length; i++)
            values[i] = (short)((data[i * 2] << 8) | data[i * 2 + 1]);
        return new SecsI2(values);
    }

    private static SecsI4 DecodeI4(byte[] data)
    {
        var values = new int[data.Length / 4];
        for (int i = 0; i < values.Length; i++)
            values[i] = (data[i * 4] << 24) | (data[i * 4 + 1] << 16) | (data[i * 4 + 2] << 8) | data[i * 4 + 3];
        return new SecsI4(values);
    }

    private static SecsI8 DecodeI8(byte[] data)
    {
        var values = new long[data.Length / 8];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = ((long)data[i * 8] << 56) | ((long)data[i * 8 + 1] << 48) |
                        ((long)data[i * 8 + 2] << 40) | ((long)data[i * 8 + 3] << 32) |
                        ((long)data[i * 8 + 4] << 24) | ((long)data[i * 8 + 5] << 16) |
                        ((long)data[i * 8 + 6] << 8) | data[i * 8 + 7];
        }
        return new SecsI8(values);
    }

    private static SecsF4 DecodeF4(byte[] data)
    {
        var values = new float[data.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            var bytes = new byte[4];
            bytes[3] = data[i * 4]; bytes[2] = data[i * 4 + 1];
            bytes[1] = data[i * 4 + 2]; bytes[0] = data[i * 4 + 3];
            values[i] = BitConverter.ToSingle(bytes);
        }
        return new SecsF4(values);
    }

    private static SecsF8 DecodeF8(byte[] data)
    {
        var values = new double[data.Length / 8];
        for (int i = 0; i < values.Length; i++)
        {
            var bytes = new byte[8];
            for (int j = 0; j < 8; j++)
                bytes[7 - j] = data[i * 8 + j];
            values[i] = BitConverter.ToDouble(bytes);
        }
        return new SecsF8(values);
    }
}

public class SecsList : SecsItem
{
    public SecsItem[] Items { get; }
    public SecsList(SecsItem[] items) : base(SecsFormat.List) => Items = items;

    public override byte[] Encode()
    {
        var header = EncodeHeader(Format, Items.Length);
        var body = Items.SelectMany(i => i.Encode()).ToArray();
        return header.Concat(body).ToArray();
    }

    public override int GetEncodedLength()
    {
        var header = EncodeHeader(Format, Items.Length);
        return header.Length + Items.Sum(i => i.GetEncodedLength());
    }

    public override string ToString()
    {
        if (Items.Length == 0) return "L[0]";
        var inner = string.Join(", ", Items.Select(i => i.ToString()));
        return $"L[{Items.Length}]: [{inner}]";
    }
}

public class SecsAscii : SecsItem
{
    public string Value { get; }
    public SecsAscii(string value) : base(SecsFormat.ASCII) => Value = CleanAsciiString(value);
    public override byte[] Encode() => EncodeHeader(Format, Value.Length).Concat(System.Text.Encoding.ASCII.GetBytes(Value)).ToArray();
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length).Length + Value.Length;
    public override string ToString() => $"A: \"{Value}\"";
}

public class SecsBinary : SecsItem
{
    public byte[] Value { get; }
    public SecsBinary(byte[] value) : base(SecsFormat.Binary) => Value = value;
    public override byte[] Encode() => EncodeHeader(Format, Value.Length).Concat(Value).ToArray();
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length).Length + Value.Length;
    public override string ToString() => $"B: [{BitConverter.ToString(Value)}]";
}

public class SecsBoolean : SecsItem
{
    public bool Value { get; }
    public SecsBoolean(bool value) : base(SecsFormat.Boolean) => Value = value;
    public override byte[] Encode() => EncodeHeader(Format, 1).Concat(new byte[] { (byte)(Value ? 1 : 0) }).ToArray();
    public override int GetEncodedLength() => EncodeHeader(Format, 1).Length + 1;
    public override string ToString() => $"Boolean: {Value}";
}

public class SecsU1 : SecsItem
{
    public byte[] Value { get; }
    public SecsU1(byte[] value) : base(SecsFormat.U1) => Value = value;
    public override byte[] Encode() => EncodeHeader(Format, Value.Length).Concat(Value).ToArray();
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length).Length + Value.Length;
    public override string ToString() => $"U1: [{string.Join(", ", Value)}]";
}

public class SecsU2 : SecsItem
{
    public ushort[] Value { get; }
    public SecsU2(ushort[] value) : base(SecsFormat.U2) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] { (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 2).Length + Value.Length * 2;
    public override string ToString() => $"U2: [{string.Join(", ", Value)}]";
}

public class SecsU4 : SecsItem
{
    public uint[] Value { get; }
    public SecsU4(uint[] value) : base(SecsFormat.U4) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 4).Length + Value.Length * 4;
    public override string ToString() => $"U4: [{string.Join(", ", Value)}]";
}

public class SecsU8 : SecsItem
{
    public ulong[] Value { get; }
    public SecsU8(ulong[] value) : base(SecsFormat.U8) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] {
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 8).Length + Value.Length * 8;
    public override string ToString() => $"U8: [{string.Join(", ", Value)}]";
}

public class SecsI1 : SecsItem
{
    public sbyte[] Value { get; }
    public SecsI1(sbyte[] value) : base(SecsFormat.I1) => Value = value;
    public override byte[] Encode() => EncodeHeader(Format, Value.Length).Concat(Value.Select(v => (byte)v)).ToArray();
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length).Length + Value.Length;
    public override string ToString() => $"I1: [{string.Join(", ", Value)}]";
}

public class SecsI2 : SecsItem
{
    public short[] Value { get; }
    public SecsI2(short[] value) : base(SecsFormat.I2) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] { (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 2).Length + Value.Length * 2;
    public override string ToString() => $"I2: [{string.Join(", ", Value)}]";
}

public class SecsI4 : SecsItem
{
    public int[] Value { get; }
    public SecsI4(int[] value) : base(SecsFormat.I4) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 4).Length + Value.Length * 4;
    public override string ToString() => $"I4: [{string.Join(", ", Value)}]";
}

public class SecsI8 : SecsItem
{
    public long[] Value { get; }
    public SecsI8(long[] value) : base(SecsFormat.I8) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => new byte[] {
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 8).Length + Value.Length * 8;
    public override string ToString() => $"I8: [{string.Join(", ", Value)}]";
}

public class SecsF4 : SecsItem
{
    public float[] Value { get; }
    public SecsF4(float[] value) : base(SecsFormat.F4) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => { var b = BitConverter.GetBytes(v); return new byte[] { b[3], b[2], b[1], b[0] }; }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 4).Length + Value.Length * 4;
    public override string ToString() => $"F4: [{string.Join(", ", Value)}]";
}

public class SecsF8 : SecsItem
{
    public double[] Value { get; }
    public SecsF8(double[] value) : base(SecsFormat.F8) => Value = value;
    public override byte[] Encode()
    {
        var bytes = Value.SelectMany(v => { var b = BitConverter.GetBytes(v); return new byte[] { b[7], b[6], b[5], b[4], b[3], b[2], b[1], b[0] }; }).ToArray();
        return EncodeHeader(Format, bytes.Length).Concat(bytes).ToArray();
    }
    public override int GetEncodedLength() => EncodeHeader(Format, Value.Length * 8).Length + Value.Length * 8;
    public override string ToString() => $"F8: [{string.Join(", ", Value)}]";
}
