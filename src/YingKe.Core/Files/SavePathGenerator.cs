namespace YingKe.Core.Files;

/// <summary>
/// 截图默认保存路径（PRD F-34：默认目录与命名规则；目录可在设置中自定义，默认 图片\YingKe）。
/// </summary>
public static class SavePathGenerator
{
    public static string DefaultFileName(DateTime now) => $"YingKe_{now:yyyyMMdd_HHmmss}";

    public static string DefaultDirectory()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures))
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(pictures ?? ".", "YingKe");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
