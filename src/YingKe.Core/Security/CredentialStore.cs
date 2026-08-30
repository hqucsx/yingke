using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace YingKe.Core.Security;

/// <summary>
/// Windows 凭据管理器封装（对应 macOS 版 Keychain，PRD 6.2）。
/// API Key 以泛型凭据存储，绑定当前用户，界面只读不回显明文。
/// </summary>
public static class CredentialStore
{
    private const string TargetPrefix = "YingKe/";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    /// <summary>写入（覆盖同名）；secret 以 UTF-16LE 编码入 blob。</summary>
    public static void Save(string name, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret ?? string.Empty);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new CREDENTIAL
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = TargetPrefix + name,
                Comment = "映刻 for Windows",
                Persist = CredPersistLocalMachine,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobPtr,
                UserName = "Ta",
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 Windows 凭据管理器失败。");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static string? Read(string name)
    {
        if (!CredRead(TargetPrefix + name, CredTypeGeneric, 0, out var credPtr))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static void Delete(string name)
        => CredDelete(TargetPrefix + name, CredTypeGeneric, 0);

    public static bool Exists(string name) => Read(name) != null;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, int type, int flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
