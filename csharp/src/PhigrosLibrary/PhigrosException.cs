namespace PhigrosLibrary;

/// <summary>本库所有异常的基类。</summary>
public class PhigrosException : Exception
{
    public PhigrosException(string message) : base(message) { }

    public PhigrosException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>存档字节流不符合预期格式时抛出。</summary>
public sealed class PhigrosFormatException : PhigrosException
{
    public PhigrosFormatException(string message) : base(message) { }

    public PhigrosFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>定数表缺失对应曲目，或数据本身不合法时抛出。</summary>
public sealed class PhigrosDataException : PhigrosException
{
    public PhigrosDataException(string message) : base(message) { }
}

/// <summary>LeanCloud 接口返回错误或响应无法解析时抛出。</summary>
public sealed class PhigrosApiException : PhigrosException
{
    public PhigrosApiException(string message) : base(message) { }

    public PhigrosApiException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>扫码登录流程中出现错误时抛出。</summary>
public sealed class PhigrosLoginException : PhigrosException
{
    public PhigrosLoginException(string message) : base(message) { }

    public PhigrosLoginException(string message, Exception innerException) : base(message, innerException) { }
}
