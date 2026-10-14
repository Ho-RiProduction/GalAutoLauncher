<#
.SYNOPSIS
    在一个独立的、不可见的 Windows 桌面上运行命令。

.DESCRIPTION
    测试会真的把窗体 Show() 出来（界面冒烟测试、模拟“普通游戏窗口”的辅助进程等）。
    如果直接在默认桌面上跑，这些窗口会弹到用户眼前，打扰用户。

    本脚本先用 CreateDesktop 新建一个隐藏桌面，再用 CreateProcess 把子进程挂到该桌面上。
    子进程（及其再拉起的孙进程）创建的所有窗口都只存在于那个隐藏桌面上，
    当前屏幕不会被遮挡，也不会抢焦点。

    标准输出 / 标准错误会通过句柄继承回传，所以测试结果照常可见。

.EXAMPLE
    & .\tools\run-hidden.ps1 -FilePath dotnet -Arguments @('run','--project','tests\GalAutoLauncher.UiSmoke','-c','Release','--no-build')

.NOTES
    必须以 `& .\tools\run-hidden.ps1 ...` 的方式在 PowerShell 会话内调用。
    不要用 `powershell -File tools\run-hidden.ps1 ...`，那样数组参数会被外层
    命令行拆成独立 token，`--project` 之类会被误当成脚本自身的参数。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [string[]]$Arguments = @(),

    [string]$WorkingDirectory = (Get-Location).Path,

    [string]$DesktopName = 'GalAL-TestDesktop'
)

$ErrorActionPreference = 'Stop'

# CreateProcess 不会搜索 PATH，必须先把可执行文件解析成完整路径。
$resolved = $FilePath
if (-not [System.IO.Path]::IsPathRooted($FilePath)) {
    $command = Get-Command $FilePath -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandType -in @('Application', 'ExternalScript') } |
        Select-Object -First 1

    if ($command) {
        $resolved = $command.Source
    }
}

if (-not (Test-Path -LiteralPath $resolved)) {
    Write-Error "找不到可执行文件：$FilePath"
    exit 1
}

if (-not [System.IO.Path]::IsPathRooted($WorkingDirectory)) {
    $WorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory).Path
}

if (-not ('GalAL.HiddenDesktop' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

namespace GalAL
{
    public static class HiddenDesktop
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const uint GENERIC_ALL = 0x10000000;
        private const uint STARTF_USESTDHANDLES = 0x00000100;
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;
        private const int STD_INPUT_HANDLE = -10;
        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;
        private const uint INFINITE = 0xFFFFFFFF;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string lpszDesktop, string lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        public static int Run(string exe, string[] args, string workingDir, string desktopName, out string error)
        {
            error = null;

            IntPtr desktop = CreateDesktop(desktopName, null, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
            {
                error = "CreateDesktop 失败，Win32 错误码 " + Marshal.GetLastWin32Error();
                return -1;
            }

            try
            {
                StringBuilder commandLine = new StringBuilder();
                commandLine.Append(Quote(exe));
                foreach (string arg in args)
                {
                    commandLine.Append(' ').Append(Quote(arg));
                }

                IntPtr stdIn = GetStdHandle(STD_INPUT_HANDLE);
                IntPtr stdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                IntPtr stdErr = GetStdHandle(STD_ERROR_HANDLE);

                MarkInheritable(stdIn);
                MarkInheritable(stdOut);
                MarkInheritable(stdErr);

                STARTUPINFO si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                si.lpDesktop = desktopName;
                si.dwFlags = (int)STARTF_USESTDHANDLES;
                si.hStdInput = stdIn;
                si.hStdOutput = stdOut;
                si.hStdError = stdErr;

                PROCESS_INFORMATION pi;
                if (!CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, workingDir, ref si, out pi))
                {
                    error = "CreateProcess 失败，Win32 错误码 " + Marshal.GetLastWin32Error();
                    return -1;
                }

                try
                {
                    WaitForSingleObject(pi.hProcess, INFINITE);

                    uint exitCode;
                    GetExitCodeProcess(pi.hProcess, out exitCode);
                    return unchecked((int)exitCode);
                }
                finally
                {
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                }
            }
            finally
            {
                CloseDesktop(desktop);
            }
        }

        private static void MarkInheritable(IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return;
            }

            SetHandleInformation(handle, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
        }

        /// <summary>按 Windows 命令行规则给参数加引号。</summary>
        private static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return argument;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('"');

            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    i++;
                    backslashes++;
                }

                if (i == argument.Length)
                {
                    sb.Append('\\', backslashes * 2);
                }
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(argument[i]);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
'@
}

$failure = $null
$exitCode = [GalAL.HiddenDesktop]::Run($resolved, $Arguments, $WorkingDirectory, $DesktopName, [ref]$failure)

if ($failure) {
    Write-Error $failure
    exit 1
}

exit $exitCode
