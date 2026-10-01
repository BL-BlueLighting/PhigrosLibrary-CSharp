namespace PhigrosLibrary.Api;

/// <summary>
/// LeanCloud <c>_GameSave</c> 表中的一条记录。
/// </summary>
/// <param name="ObjectId">记录自身的 objectId。</param>
/// <param name="Summary">base64 编码的玩家概要。</param>
/// <param name="FileId">存档文件在 <c>_File</c> 表中的 objectId。</param>
/// <param name="Url">存档文件下载地址。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
/// <param name="UserId">所属用户的 objectId。</param>
public sealed record CloudSaveRecord(
    string ObjectId,
    string Summary,
    string? FileId,
    string Url,
    DateTimeOffset? UpdatedAt,
    string? UserId);
