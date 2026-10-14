using System.Runtime.InteropServices;
using System.Text;

namespace GalAutoLauncher;

/// <summary>
/// 枚举某个进程的顶层窗口，找出真正的“主窗口”。
///
/// 不能直接依赖 <see cref="System.Diagnostics.Process.MainWindowHandle"/>：
/// 它只是按窗口枚举顺序返回第一个顶层窗口，很可能命中 UAC 输入指示器、
/// 输入法候选框、提示浮层等“可见但没有标题”的系统窗口，
/// 从而让真正的游戏窗口被漏掉。
///
/// 这里改为遍历该进程的所有顶层窗口，只保留“可见 + 标题非空”的窗口，
/// 再从中挑选面积最大的一个作为主窗口。
/// </summary>
internal static class WindowInspector
{
    /// <summary>
    /// 查找进程的主窗口。找到时返回 true，并给出窗口句柄与标题。
    /// </summary>
    public static bool TryFindMainWindow(int pid, out IntPtr handle, out string title)
    {
        handle = IntPtr.Zero;
        title = string.Empty;

        if (pid <= 0)
        {
            return false;
        }

        var found = false;
        var bestArea = 0L;
        var bestHandle = IntPtr.Zero;
        var bestTitle = string.Empty;

        EnumWindows((hWnd, _) =>
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid != (uint)pid)
                {
                    return true;
                }

                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return true;
                }

                var buffer = new StringBuilder(length + 1);
                if (GetWindowText(hWnd, buffer, buffer.Capacity) <= 0)
                {
                    return true;
                }

                var text = buffer.ToString().Trim();
                if (text.Length == 0)
                {
                    return true;
                }

                var area = 0L;
                if (GetWindowRect(hWnd, out var rect))
                {
                    area = Math.Max(0, rect.Right - rect.Left) *
                           (long)Math.Max(0, rect.Bottom - rect.Top);
                }

                // 第一个带标题的可见窗口先兜底，之后按面积择优。
                if (!found || area > bestArea)
                {
                    found = true;
                    bestArea = area;
                    bestHandle = hWnd;
                    bestTitle = text;
                }
            }
            catch
            {
                // 忽略单个窗口的读取失败。
            }

            return true;
        }, IntPtr.Zero);

        if (!found)
        {
            return false;
        }

        handle = bestHandle;
        title = bestTitle;
        return true;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
