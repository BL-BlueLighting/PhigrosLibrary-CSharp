namespace PhigrosLibrary.Serialization;

/// <summary>
/// 二进制解码后的字段集合，是 <see cref="ObjectSchema"/> 与强类型模型之间的中间表示。
/// </summary>
/// <remarks>
/// 之所以保留这一层，是因为存档结构带有版本号：解析时会把多个版本的 schema 依次作用在同一个
/// <see cref="FieldMap"/> 上，缺少的字段由模型侧取默认值。序列化时再按同样的顺序写回。
/// </remarks>
public sealed class FieldMap
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>按写入顺序返回所有字段名。</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <summary>字段数量。</summary>
    public int Count => _values.Count;

    /// <summary>写入或覆盖一个字段。</summary>
    public void Set(string name, object? value)
    {
        if (!_values.ContainsKey(name)) _order.Add(name);
        _values[name] = value;
    }

    /// <summary>字段是否存在。</summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>取字段原始值，不存在时返回 <see langword="null"/>。</summary>
    public object? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    /// <summary>按指定类型取字段值，类型不符时抛出 <see cref="PhigrosFormatException"/>。</summary>
    public T Get<T>(string name)
    {
        object? value = Get(name);
        if (value is T typed) return typed;

        throw new PhigrosFormatException(
            value is null
                ? $"字段 '{name}' 不存在。"
                : $"字段 '{name}' 的类型为 {value.GetType().Name}，与期望的 {typeof(T).Name} 不符。");
    }
}
