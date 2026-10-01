namespace PhigrosLibrary.Serialization;

/// <summary>描述存档二进制结构中的一个字段。</summary>
/// <param name="Name">字段名，与模型属性一一对应。</param>
/// <param name="Kind">字段类型。</param>
/// <param name="Count">数组类型的元素个数，非数组类型忽略。</param>
public sealed record SchemaField(string Name, FieldKind Kind, int Count = 0);

/// <summary>
/// 描述存档二进制结构中的一段字段序列。
/// </summary>
/// <remarks>
/// 游戏每次大版本更新可能向存档追加字段，因此同一逻辑结构会按版本拆成多个 <see cref="ObjectSchema"/>，
/// 解析时依次应用 <c>version</c> 个 schema（见 <see cref="BinarySerializer.DeserializeVersioned"/>）。
/// 这种"表驱动"的写法沿用自上游 C 实现，便于在游戏更新后对照增删字段。
/// </remarks>
public sealed class ObjectSchema
{
    public ObjectSchema(string name, params SchemaField[] fields)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fields);

        Name = name;
        Fields = fields;
    }

    /// <summary>结构名，仅用于异常信息与调试。</summary>
    public string Name { get; }

    /// <summary>按磁盘顺序排列的字段。</summary>
    public IReadOnlyList<SchemaField> Fields { get; }
}
