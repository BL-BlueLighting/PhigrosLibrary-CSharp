using System.Buffers.Binary;
using System.Text;

namespace PhigrosLibrary.Binary;

/// <summary>
/// 存档二进制数据的顺序读取器。
/// 所有多字节数值均为小端序，与游戏内 Unity 的序列化结果一致。
/// </summary>
public sealed class ByteReader
{
    private readonly byte[] _data;
    private int _position;

    public ByteReader(byte[] data, int position = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (position < 0 || position > data.Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        _data = data;
        _position = position;
    }

    /// <summary>当前读取位置，可写。</summary>
    public int Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _data.Length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    /// <summary>缓冲区总长度。</summary>
    public int Length => _data.Length;

    /// <summary>尚未读取的字节数。</summary>
    public int Remaining => _data.Length - _position;

    /// <summary>底层缓冲区，供需要原样截取的场景（如 overflow）使用。</summary>
    public ReadOnlySpan<byte> Buffer => _data;

    /// <summary>剩余字节的只读切片，不移动读取位置。</summary>
    public ReadOnlySpan<byte> RemainingSpan => _data.AsSpan(_position);

    private void Ensure(int count)
    {
        if (_position + count > _data.Length)
        {
            throw new PhigrosFormatException(
                $"存档数据在偏移 {_position} 处提前结束：还需要 {count} 字节，实际只剩 {Remaining} 字节。");
        }
    }

    /// <summary>读取当前位置的字节但不移动读取位置。</summary>
    public byte PeekByte()
    {
        Ensure(1);
        return _data[_position];
    }

    public byte ReadByte()
    {
        Ensure(1);
        return _data[_position++];
    }

    public ushort ReadUInt16()
    {
        Ensure(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Ensure(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    public float ReadSingle()
    {
        Ensure(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    /// <summary>跳过指定字节数。</summary>
    public void Skip(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Ensure(count);
        _position += count;
    }

    /// <summary>
    /// 读取变长整数：最高位为 0 时占 1 字节，为 1 时占 2 字节（低位在前）。
    /// </summary>
    public int ReadVarshort()
    {
        Ensure(1);
        byte first = _data[_position];
        if (first < 0x80)
        {
            _position++;
            return first;
        }

        Ensure(2);
        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
        _position += 2;
        return (raw & 0x7F) | ((raw >> 8) << 7);
    }

    /// <summary>
    /// 读取变长整数长度的字符串。
    /// </summary>
    /// <param name="suffixLength">
    /// 长度字段中包含的、但不属于字符串内容的尾部字节数。
    /// <c>gameRecord</c> 的键在磁盘上带有 <c>.0</c> 后缀（<paramref name="suffixLength"/> = 2），
    /// 其余字符串为 0。
    /// </param>
    public string ReadString(int suffixLength = 0)
    {
        int length = ReadVarshort();
        if (length < suffixLength)
        {
            throw new PhigrosFormatException(
                $"字符串长度字段 {length} 小于应有的后缀长度 {suffixLength}，数据可能已损坏。");
        }

        Ensure(length);
        string value = Encoding.UTF8.GetString(_data, _position, length - suffixLength);
        _position += length;
        return value;
    }
}
