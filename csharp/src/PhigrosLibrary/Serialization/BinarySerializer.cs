using PhigrosLibrary.Binary;

namespace PhigrosLibrary.Serialization;

/// <summary>
/// 按 <see cref="ObjectSchema"/> 描述的结构，在字节流与 <see cref="FieldMap"/> 之间转换。
/// </summary>
public static class BinarySerializer
{
    /// <summary>按 schema 顺序读取一段字段。</summary>
    public static FieldMap Deserialize(ByteReader reader, ObjectSchema schema)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);

        var map = new FieldMap();
        byte packed = 0;
        int bit = 0;

        foreach (var field in schema.Fields)
        {
            if (field.Kind == FieldKind.Bool)
            {
                // 连续布尔共用一个字节：第一个布尔只窥视不消费，直到遇到非布尔字段才跳过该字节。
                if (bit == 0) packed = reader.PeekByte();
                if (bit >= 8)
                    throw new PhigrosFormatException($"结构 {schema.Name} 中 '{field.Name}' 之前连续出现了超过 8 个布尔字段。");

                map.Set(field.Name, ((packed >> bit) & 1) != 0);
                bit++;
                continue;
            }

            if (bit != 0)
            {
                bit = 0;
                reader.Skip(1);
            }

            map.Set(field.Name, ReadValue(reader, field));
        }

        if (bit != 0) reader.Skip(1);
        return map;
    }

    private static object ReadValue(ByteReader reader, SchemaField field) => field.Kind switch
    {
        FieldKind.U8 => reader.ReadByte(),
        FieldKind.U16 => reader.ReadUInt16(),
        FieldKind.Single => reader.ReadSingle(),
        FieldKind.String => reader.ReadString(),
        FieldKind.Varshort => reader.ReadVarshort(),
        FieldKind.UInt16Array => ReadUInt16Array(reader, field.Count),
        FieldKind.VarshortArray => ReadVarshortArray(reader, field.Count),
        _ => throw new PhigrosFormatException($"未知字段类型 {field.Kind}。"),
    };

    private static ushort[] ReadUInt16Array(ByteReader reader, int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < count; i++) values[i] = reader.ReadUInt16();
        return values;
    }

    private static int[] ReadVarshortArray(ByteReader reader, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = reader.ReadVarshort();
        return values;
    }

    /// <summary>按 schema 顺序写出一段字段。</summary>
    public static void Serialize(ByteWriter writer, FieldMap map, ObjectSchema schema)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(schema);

        byte packed = 0;
        int bit = 0;

        foreach (var field in schema.Fields)
        {
            if (field.Kind == FieldKind.Bool)
            {
                if (bit >= 8)
                    throw new PhigrosFormatException($"结构 {schema.Name} 中 '{field.Name}' 之前连续出现了超过 8 个布尔字段。");

                if (GetBool(map, field.Name)) packed |= (byte)(1 << bit);
                bit++;
                continue;
            }

            if (bit != 0)
            {
                writer.WriteByte(packed);
                bit = 0;
                packed = 0;
            }

            WriteValue(writer, field, map);
        }

        if (bit != 0) writer.WriteByte(packed);
    }

    private static void WriteValue(ByteWriter writer, SchemaField field, FieldMap map)
    {
        switch (field.Kind)
        {
            case FieldKind.U8:
                writer.WriteByte(Convert.ToByte(map.Get(field.Name) ?? (byte)0));
                break;
            case FieldKind.U16:
                writer.WriteUInt16(Convert.ToUInt16(map.Get(field.Name) ?? (ushort)0));
                break;
            case FieldKind.Single:
                writer.WriteSingle(Convert.ToSingle(map.Get(field.Name) ?? 0f));
                break;
            case FieldKind.String:
                writer.WriteString(map.Get(field.Name) as string ?? string.Empty);
                break;
            case FieldKind.Varshort:
                writer.WriteVarshort(GetVarshort(map, field, map.Get(field.Name)));
                break;
            case FieldKind.UInt16Array:
                foreach (ushort value in GetArray<ushort>(map, field))
                    writer.WriteUInt16(value);
                break;
            case FieldKind.VarshortArray:
                foreach (int value in GetArray<int>(map, field))
                    writer.WriteVarshort(GetVarshort(map, field, value));
                break;
            default:
                throw new PhigrosFormatException($"未知字段类型 {field.Kind}。");
        }
    }

    private static bool GetBool(FieldMap map, string name) => map.Get(name) as bool? ?? false;

    /// <summary>
    /// 变长整数在磁盘上最多占两字节（首字节 7 位 + 次字节 8 位）。
    /// 这里把越界报错补上字段名，避免调用方只看到裸的数值错误。
    /// </summary>
    private static int GetVarshort(FieldMap map, SchemaField field, object? value)
    {
        int number = Convert.ToInt32(value ?? 0);
        if (number < 0 || number > ByteWriter.VarshortMax)
        {
            throw new PhigrosDataException(
                $"字段 '{field.Name}' 的值 {number} 超出变长整数能表示的范围 0..{ByteWriter.VarshortMax}。");
        }

        return number;
    }

    private static T[] GetArray<T>(FieldMap map, SchemaField field)
    {
        if (map.Get(field.Name) is not T[] values)
            return new T[field.Count];

        if (values.Length != field.Count && field.Count != 0)
        {
            throw new PhigrosFormatException(
                $"字段 '{field.Name}' 需要 {field.Count} 个元素，实际为 {values.Length} 个。");
        }

        return values;
    }

    /// <summary>
    /// 依次应用前 <paramref name="version"/> 个 schema 读取字段，用于带版本的存档结构。
    /// </summary>
    public static FieldMap DeserializeVersioned(
        ByteReader reader, IReadOnlyList<ObjectSchema> schemas, int version)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        var map = new FieldMap();
        int applicable = Math.Min(version, schemas.Count);
        for (int i = 0; i < applicable; i++)
        {
            var section = Deserialize(reader, schemas[i]);
            foreach (string key in section.Keys) map.Set(key, section.Get(key));
        }

        return map;
    }

    /// <summary>
    /// 依次应用前 <paramref name="version"/> 个 schema 写出字段，与
    /// <see cref="DeserializeVersioned"/> 互逆。
    /// </summary>
    public static void SerializeVersioned(
        ByteWriter writer, FieldMap map, IReadOnlyList<ObjectSchema> schemas, int version)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        int applicable = Math.Min(version, schemas.Count);
        for (int i = 0; i < applicable; i++)
        {
            Serialize(writer, map, schemas[i]);
        }
    }
}
