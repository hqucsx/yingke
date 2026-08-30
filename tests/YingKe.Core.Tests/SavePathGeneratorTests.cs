using System.Text.RegularExpressions;
using YingKe.Core.Files;
using Xunit;

namespace YingKe.Core.Tests;

public class SavePathGeneratorTests
{
    [Fact]
    public void DefaultFileName_FollowsYingKeTimestampPattern()
    {
        var name = SavePathGenerator.DefaultFileName(new DateTime(2026, 8, 28, 9, 30, 5));
        Assert.Equal("YingKe_20260828_093005", name);
    }

    [Fact]
    public void DefaultDirectory_IsPicturesYingKeAndExists()
    {
        var dir = SavePathGenerator.DefaultDirectory();
        Assert.EndsWith("YingKe", dir);
        Assert.True(Directory.Exists(dir));
    }
}
