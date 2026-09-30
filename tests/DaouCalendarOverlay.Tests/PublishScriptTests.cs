using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// publish.ps1이 네이티브 명령 실패를 삼키지 않고, 버전이 들어간 산출물명·PDB 분리·서명 파라미터를 갖추며,
/// -Version이 csproj 버전과 다르면 중단하는지 고정한다. 실제 dotnet publish는 실행하지 않고 -DryRun과 스크립트 본문만 확인한다.
/// </summary>
public sealed class PublishScriptTests
{
    private const int ScriptTimeoutMilliseconds = 60000;

    private static readonly string ScriptPath = RepoLayout.Path("publish.ps1");

    private static readonly string MainProjectPath =
        RepoLayout.Path("DaouCalendarOverlay", "DaouCalendarOverlay.csproj");

    [Fact]
    public void PublishScript_DryRun_PrintsVersionedArtifactName()
    {
        var version = ReadProjectVersion();
        var publishDir = RepoLayout.Path("publish");
        var publishDirExistedBefore = Directory.Exists(publishDir);

        var (exitCode, stdOut, stdErr) = RunScript("-DryRun");

        Assert.True(exitCode == 0, $"종료 코드 {exitCode}. stderr: {stdErr}");
        Assert.Contains($"VERSION={version}", stdOut, StringComparison.Ordinal);
        Assert.Contains($"ARTIFACT=DaouCalendarOverlay-{version}.exe", stdOut, StringComparison.Ordinal);
        Assert.Contains("PROFILE=win-x64", stdOut, StringComparison.Ordinal);
        Assert.Contains("SIGN=none", stdOut, StringComparison.Ordinal);
        Assert.Equal(publishDirExistedBefore, Directory.Exists(publishDir));
    }

    [Fact]
    public void PublishScript_DryRun_ExplicitVersionMustMatchProjectVersion()
    {
        var version = ReadProjectVersion();

        var (matchExitCode, matchStdOut, matchStdErr) = RunScript("-DryRun", "-Version", version);

        Assert.True(matchExitCode == 0, $"종료 코드 {matchExitCode}. stderr: {matchStdErr}");
        Assert.Contains($"ARTIFACT=DaouCalendarOverlay-{version}.exe", matchStdOut, StringComparison.Ordinal);

        Assert.NotEqual("9.9.9", version);

        var (mismatchExitCode, mismatchStdOut, mismatchStdErr) = RunScript("-DryRun", "-Version", "9.9.9");

        Assert.NotEqual(0, mismatchExitCode);
        Assert.DoesNotContain("ARTIFACT=", mismatchStdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Publish completed", mismatchStdOut, StringComparison.Ordinal);
        Assert.Contains("mismatch", mismatchStdOut + mismatchStdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishScript_DryRun_InvalidVersionExitsNonZero()
    {
        var (exitCode, stdOut, _) = RunScript("-DryRun", "-Version", "1.2.x");

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("Publish completed", stdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishScript_GuardsNativeExitCodeAndSeparatesSymbols()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("$LASTEXITCODE", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-Checked", script, StringComparison.Ordinal);
        Assert.Contains("PublishProfile=win-x64", script, StringComparison.Ordinal);
        Assert.Contains("symbols", script, StringComparison.Ordinal);
        Assert.Contains("CertificateThumbprint", script, StringComparison.Ordinal);
        Assert.Contains("TimestampUrl", script, StringComparison.Ordinal);
        Assert.Contains("Version mismatch", script, StringComparison.Ordinal);

        Assert.DoesNotContain("DebugType=None", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:PublishSingleFile=true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:FileVersion", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:AssemblyVersion", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Windows PowerShell로 publish.ps1을 실행하고 (종료 코드, 표준 출력, 표준 오류)를 돌려준다.
    /// 출력은 이벤트로 비동기 수집해 파이프 버퍼가 차서 멈추는 일을 막는다.
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunScript(params string[] args)
    {
        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShellPath))
        {
            powerShellPath = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo(powerShellPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoLayout.Root,
        };

        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stdOut)
                {
                    stdOut.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stdErr)
                {
                    stdErr.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(ScriptTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 이미 종료된 경우
            }

            throw new TimeoutException($"publish.ps1이 {ScriptTimeoutMilliseconds}ms 안에 끝나지 않았습니다. 인자: {string.Join(' ', args)}");
        }

        // 인자 없는 WaitForExit는 비동기 출력 수집이 끝날 때까지 기다린다.
        process.WaitForExit();

        string outText;
        string errText;
        lock (stdOut)
        {
            outText = stdOut.ToString();
        }

        lock (stdErr)
        {
            errText = stdErr.ToString();
        }

        return (process.ExitCode, outText, errText);
    }

    /// <summary>
    /// publish.ps1의 Get-ProjectVersion과 같은 규칙: Version → FileVersion → "7.2.0".
    /// MSBuild SDK 스타일 프로젝트는 XML 네임스페이스가 없으므로 LocalName으로 찾는다.
    /// </summary>
    private static string ReadProjectVersion()
    {
        var document = XDocument.Load(MainProjectPath);

        foreach (var name in new[] { "Version", "FileVersion" })
        {
            var value = document.Descendants()
                .Where(element => element.Name.LocalName == name
                    && element.Parent?.Name.LocalName == "PropertyGroup")
                .Select(element => element.Value.Trim())
                .FirstOrDefault(text => text.Length > 0);

            if (value is not null)
            {
                return value;
            }
        }

        return "7.2.0";
    }
}
