using YingKe.Core.Ocr;
using Xunit;

namespace YingKe.Core.Tests;

public class OcrTextCleanerTests
{
    [Fact]
    public void Clean_TrimsLinesAndDropsEmpty()
    {
        var text = OcrTextCleaner.Clean(new[] { "  hello  ", "", "world\t", "   " });
        Assert.Equal("hello\nworld", text);
    }

    [Fact]
    public void Clean_RemovesSpacesBetweenCjkCharacters()
    {
        Assert.Equal("你好，世界", OcrTextCleaner.Clean("你 好，世 界"));
        Assert.Equal("截图工具", OcrTextCleaner.Clean("截 图 工 具"));
    }

    [Fact]
    public void Clean_KeepsSpacesBetweenLatinWords()
    {
        Assert.Equal("Ta OCR 2026", OcrTextCleaner.Clean("Ta OCR 2026"));
    }

    [Fact]
    public void Clean_KeepsSpaceBetweenCjkAndLatin()
    {
        // 中英混排的空格是有效分隔，不应删除
        Assert.Equal("映刻 App 工具", OcrTextCleaner.Clean("映刻 App 工具"));
    }

    [Fact]
    public void Clean_SingleStringOverloadSplitsByNewline()
    {
        Assert.Equal("第一行\n第二行", OcrTextCleaner.Clean(" 第一行 \n\n 第二行 "));
    }
}
