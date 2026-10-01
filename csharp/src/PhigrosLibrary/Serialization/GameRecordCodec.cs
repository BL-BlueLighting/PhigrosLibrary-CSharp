using System.Text;
using PhigrosLibrary.Binary;
using PhigrosLibrary.Models;

namespace PhigrosLibrary.Serialization;

/// <summary>
/// <c>gameRecord</c> 条目的读写。
/// </summary>
/// <remarks>
/// 磁盘结构：先一个变长整数表示曲目数量，随后每首曲目为
/// <code>
/// +----------------------+------------+------------------------------------------+
/// | 变长长度 + 曲目id.0  | 块长度(1B) | exist(1B) + fc(1B) + [分数(4B) + acc(4B)]* |
/// +----------------------+------------+------------------------------------------+
/// </code>
/// <c>exist</c> 的第 n 位表示第 n 个难度有成绩，<c>fc</c> 的第 n 位表示该难度取得 Full Combo；
/// 只有被 <c>exist</c> 标记的难度才会写入 8 字节的成绩。块长度用于跳过本实现不认识的尾部字段。
/// </remarks>
public static class GameRecordCodec
{
    /// <summary>曲目 id 在磁盘上携带的后缀，读取时剥离、写回时补上。</summary>
    private static readonly byte[] KeySuffix = Encoding.ASCII.GetBytes(".0");

    /// <summary>读取成绩表。</summary>
    public static Dictionary<string, SongLevels> Read(ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int count = reader.ReadVarshort();
        var records = new Dictionary<string, SongLevels>(count, StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            string songId = reader.ReadString(KeySuffix.Length);

            int blockLength = reader.ReadByte();
            int next = reader.Position + blockLength;
            if (next > reader.Length)
                throw new PhigrosFormatException($"曲目 '{songId}' 的块长度 {blockLength} 越界，数据可能已损坏。");

            byte exist = reader.ReadByte();
            byte fullCombo = reader.ReadByte();

            var levels = new SongLevels();
            for (int level = 0; level < SongLevels.Count; level++)
            {
                if (((exist >> level) & 1) == 0) continue;

                uint score = reader.ReadUInt32();
                float accuracy = reader.ReadSingle();
                levels[level] = new LevelRecord(score, accuracy, ((fullCombo >> level) & 1) != 0);
            }

            reader.Position = next;
            records[songId] = levels;
        }

        return records;
    }

    /// <summary>写出成绩表，与 <see cref="Read"/> 互逆。</summary>
    public static void Write(ByteWriter writer, IReadOnlyDictionary<string, SongLevels> records)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(records);

        writer.WriteVarshort(records.Count);

        foreach ((string songId, SongLevels levels) in records)
        {
            writer.WriteString(songId, KeySuffix);

            byte exist = 0;
            byte fullCombo = 0;
            int blockLength = 2; // exist + fc

            for (int level = 0; level < SongLevels.Count; level++)
            {
                if (levels[level] is not { } record) continue;

                exist |= (byte)(1 << level);
                blockLength += 8; // 分数 + acc
                if (record.FullCombo) fullCombo |= (byte)(1 << level);
            }

            writer.WriteByte((byte)blockLength);
            writer.WriteByte(exist);
            writer.WriteByte(fullCombo);

            for (int level = 0; level < SongLevels.Count; level++)
            {
                if (levels[level] is not { } record) continue;

                writer.WriteUInt32(record.Score);
                writer.WriteSingle(record.Accuracy);
            }
        }
    }
}
