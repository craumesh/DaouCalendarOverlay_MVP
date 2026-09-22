using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// Named Pipe 서버 프로세스의 신원을 확인하기 위한 순수 판정 로직과 Win32 조회 헬퍼.
/// native host(클라이언트)가 접속한 파이프 서버가 "자기 자신과 같은 실행 파일"인지 판정하는 데 쓴다.
/// 모든 메서드는 실패 시 예외를 밖으로 던지지 않고 false 또는 null을 돌려준다(fail closed).
/// 로깅은 호출부(<c>NativeHostRelay</c>)에서 하며 이 클래스는 로그를 남기지 않는다.
/// </summary>
public static class PipePeerVerifier
{
    /// <summary>
    /// PROCESS_QUERY_LIMITED_INFORMATION. 대상 프로세스가 더 높은 권한으로 실행 중이어도
    /// 이미지 경로 조회가 가능하도록 PROCESS_QUERY_INFORMATION 대신 이 권한을 먼저 요청한다.
    /// </summary>
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// 두 실행 파일 경로가 같은 파일을 가리키는지 비교한다(순수 함수).
    /// 둘 중 하나라도 비어 있거나 정규화할 수 없으면 false(fail closed).
    /// </summary>
    public static bool IsSameExecutable(string? selfExecutablePath, string? serverExecutablePath)
    {
        var self = NormalizePath(selfExecutablePath);
        if (self is null)
            return false;

        var server = NormalizePath(serverExecutablePath);
        if (server is null)
            return false;

        return string.Equals(self, server, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 경로를 절대 경로로 정규화하고 끝의 디렉터리 구분자를 제거한다.
    /// 비어 있거나 정규화 중 예외가 나면 null.
    /// </summary>
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            // ArgumentException(널 문자 포함 등), PathTooLongException, NotSupportedException, SecurityException.
            return null;
        }
    }

    /// <summary>
    /// 연결된 파이프의 서버 프로세스 ID를 조회한다. 확인할 수 없으면 null.
    /// </summary>
    public static uint? TryGetServerProcessId(SafePipeHandle pipeHandle)
    {
        try
        {
            if (pipeHandle is null || pipeHandle.IsInvalid || pipeHandle.IsClosed)
                return null;

            if (!GetNamedPipeServerProcessId(pipeHandle, out var serverProcessId))
                return null;

            return serverProcessId == 0 ? null : serverProcessId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 프로세스의 실행 파일 경로를 조회한다. 확인할 수 없으면 null.
    /// </summary>
    public static string? TryGetProcessPath(int processId)
    {
        if (processId <= 0)
            return null;

        var queried = QueryFullProcessImageName(processId);
        if (!string.IsNullOrEmpty(queried))
            return queried;

        try
        {
            using var p = Process.GetProcessById(processId);
            var fileName = p.MainModule?.FileName;
            return string.IsNullOrEmpty(fileName) ? null : fileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>실패하지 않는 오류 문구 생성기. 쿠키·토큰·요청 본문을 절대 담지 않는다.</summary>
    public static string BuildUntrustedPeerError(uint serverProcessId) =>
        $"Named Pipe 서버(PID {serverProcessId})가 Daou Calendar Overlay 실행 파일이 아니어서 요청을 전송하지 않았습니다.";

    /// <summary>실패하지 않는 오류 문구 생성기. 쿠키·토큰·요청 본문을 절대 담지 않는다.</summary>
    public static string BuildUnverifiablePeerError() =>
        "Named Pipe 서버 프로세스를 확인할 수 없어 요청을 전송하지 않았습니다.";

    private static string? QueryFullProcessImageName(int processId)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
            if (handle == IntPtr.Zero)
                return null;

            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                return null;

            var path = buffer.ToString();
            return path.Length == 0 ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle hNamedPipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
