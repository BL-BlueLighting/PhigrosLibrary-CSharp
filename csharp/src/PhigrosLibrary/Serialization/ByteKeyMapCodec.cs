using PhigrosLibrary.Binary;

namespace PhigrosLibrary.Serialization;

/// <summary>
/// <c>gameKey</c> 前半段的键值表读写：值是 5 个 <see cref="byte"/>，磁盘上只保存非零项。
/// </summary>
/// <remarks>
/// 磁盘结构：先一个变长整数表示条目数量，随后每条为
/// <code>
/// +-----------------+------------+---------------------------------------+
/// | 变长长度 + 键名 | 块长度(1B) | exist(1B) + 只写非零项的各 1 字节值  |
/// +-----------------+------------+---------------------------------------+
/// </code>
/// <c>exist</c> 的第 n 位表示第 n 个值是否非零（非零才占一个字节）。
/// </remarks>
public static class ByteKeyMapCodec
{
    /// <summary>每条记录中值的个数。</summary>
    public const int ValueCount = 5;

    /// <summary>读取键值表。</summary>
    public static Dictionary<string, byte[]> Read(ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int count = reader.ReadVarshort();
        var map = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            string key = reader.ReadString();

            int blockLength = reader.ReadByte();
            int next = reader.Position + blockLength;
            if (next > reader.Length)
                throw new PhigrosFormatException($"键 '{key}' 的块长度 {blockLength} 越界，数据可能已损坏。");

            byte exist = reader.ReadByte();
            var values = new byte[ValueCount];
            for (int index = 0; index < ValueCount; index++)
            {
                if (((exist >> index) & 1) != 0) values[index] = reader.ReadByte();
            }

            reader.Position = next;
            map[key] = values;
        }

        return map;
    }

    /// <summary>写出键值表，与 <see cref="Read"/> 互逆。</summary>
    public static void Write(ByteWriter writer, IReadOnlyDictionary<string, byte[]> map)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(map);

        writer.WriteVarshort(map.Count);

        foreach ((string key, byte[] values) in map)
        {
            if (values.Length != ValueCount)
                throw new PhigrosDataException($"键 '{key}' 需要 {ValueCount} 个值，实际为 {values.Length} 个。");

            writer.WriteString(key);

            byte exist = 0;
            int written = 0;
            for (int index = 0; index < ValueCount; index++)
            {
                if (values[index] == 0) continue;
                exist |= (byte)(1 << index);
                written++;
            }

            writer.WriteByte((byte)(written + 1)); // exist 自身占 1 字节
            writer.WriteByte(exist);

            for (int index = 0; index < ValueCount; index++)
            {
                if (values[index] != 0) writer.WriteByte(values[index]);
            }
        }
    }
}
