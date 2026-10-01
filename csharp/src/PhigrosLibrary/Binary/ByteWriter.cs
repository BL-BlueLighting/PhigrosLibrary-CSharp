using System.Buffers.Binary;
using System.Text;

namespace PhigrosLibrary.Binary;

/// <summary>
/// 存档二进制数据的顺序写入器，与 <see cref="ByteReader"/> 互逆。
/// </summary>
public sealed class ByteWriter
{
    private byte[] _buffer;
    private int _length;

    public ByteWriter(int capacity = 256)
    {
        _buffer = new byte[Math.Max(capacity, 16)];
    }

    /// <summary>已写入的字节数。</summary>
    public int Length => _length;

    private void EnsureCapacity(int extra)
    {
        int required = _length + extra;
        if (required <= _buffer.Length) return;

        int capacity = _buffer.Length;
        while (capacity < required) capacity *= 2;
        Array.Resize(ref _buffer, capacity);
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    public void WriteSingle(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>
    /// 变长整数能表示的最大值：首字节给低 7 位，次字节给高 8 位。
    /// </summary>
    public const int VarshortMax = 0x7F7F;

    /// <summary>
    /// 写入变长整数。小于 128 的值占 1 字节，其余占 2 字节（低位在前，首位补 1）。
    /// </summary>
    public void WriteVarshort(int value)
    {
        if (value < 0 || value > VarshortMax)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"变长整数只能在 0..{VarshortMax} 之间。");

        if (value < 0x80)
        {
            WriteByte((byte)value);
            return;
        }

        WriteByte((byte)((value & 0x7F) | 0x80));
        WriteByte((byte)(value >> 7));
    }

    /// <summary>
    /// 写入变长整数长度的字符串。
    /// </summary>
    /// <param name="value">字符串内容。</param>
    /// <param name="suffix">
    /// 计入长度字段但不属于字符串内容的尾部字节。
    /// <c>gameRecord</c> 的键需要传 <c>".0"</c>，其余传空。
    /// </param>
    public void WriteString(string value, ReadOnlySpan<byte> suffix = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarshort(byteCount + suffix.Length);
        WriteBytes(Encoding.UTF8.GetBytes(value));
        WriteBytes(suffix);
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}
