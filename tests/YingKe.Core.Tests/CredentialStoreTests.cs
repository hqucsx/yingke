using YingKe.Core.Security;
using Xunit;

namespace YingKe.Core.Tests;

/// <summary>
/// 读写真实 Windows 凭据管理器；使用一次性名称，测试后清理。
/// </summary>
public class CredentialStoreTests
{
    private const string Name = "test-roundtrip";

    [Fact]
    public void SaveReadDelete_RoundTrip()
    {
        try
        {
            CredentialStore.Delete(Name);
        }
        catch
        {
            // 不存在时删除失败是正常的
        }

        Assert.False(CredentialStore.Exists(Name));

        CredentialStore.Save(Name, "sk-test-12345");
        Assert.True(CredentialStore.Exists(Name));
        Assert.Equal("sk-test-12345", CredentialStore.Read(Name));

        // 覆盖写
        CredentialStore.Save(Name, "sk-updated");
        Assert.Equal("sk-updated", CredentialStore.Read(Name));

        CredentialStore.Delete(Name);
        Assert.False(CredentialStore.Exists(Name));
    }
}
