using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;

namespace YingKe.App;

/// <summary>
/// 会话结束后的内存收缩：压缩 LOH 碎片 + 把物理页交还系统。
/// 截图会话会产生多张整屏位图（LOH 大对象），不整理的话提交量只涨不降。
/// </summary>
internal static class MemoryTrim
{
    /// <summary>可在任意线程调用；失败不影响功能。</summary>
    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            // min/max = -1：允许工作集立刻被换出（提交量不变，再次访问自动换回）
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));

            // 诊断埋点：观察每次会话结束后托管堆是否回落（定位提交量增长来源）
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "yingke-mem.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] managed={GC.GetTotalMemory(false) / 1024 / 1024}MB " +
                $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)} " +
                $"threads={Process.GetCurrentProcess().Threads.Count}\r\n");
        }
        catch
        {
            // 内存整理失败不影响功能
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);
}
