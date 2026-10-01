namespace PhigrosLibrary.Serialization;

/// <summary>存档二进制结构中支持的字段类型。</summary>
public enum FieldKind
{
    /// <summary>布尔值。连续的布尔值按低位在前打包进同一个字节，被其他类型打断时对齐到下一字节。</summary>
    Bool,

    /// <summary>无符号 8 位整数。</summary>
    U8,

    /// <summary>无符号 16 位整数。</summary>
    U16,

    /// <summary>IEEE 754 单精度浮点数。</summary>
    Single,

    /// <summary>变长整数长度前缀的 UTF-8 字符串。</summary>
    String,

    /// <summary>变长整数。</summary>
    Varshort,

    /// <summary>定长的 <see cref="ushort"/> 数组，长度由 <see cref="SchemaField.Count"/> 指定。</summary>
    UInt16Array,

    /// <summary>定长的变长整数数组，长度由 <see cref="SchemaField.Count"/> 指定。</summary>
    VarshortArray,
}
