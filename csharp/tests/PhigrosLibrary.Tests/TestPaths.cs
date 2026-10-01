namespace PhigrosLibrary.Tests;

/// <summary>定位仓库内的资源文件，避免把测试数据再复制一份。</summary>
internal static class TestPaths
{
    /// <summary>仓库根目录（含 <c>resources</c> 的那一层）。</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>真实定数表，用于解析与计算测试。</summary>
    public static string DifficultyTable => Path.Combine(RepoRoot, "resources", "difficulty.tsv");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "resources")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"从 {AppContext.BaseDirectory} 向上找不到包含 resources 的仓库根目录。");
    }
}
