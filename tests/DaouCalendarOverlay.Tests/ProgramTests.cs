using System.Reflection;
using System.Resources;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void EntryPoint_IsProgramMain()
    {
        var entry = typeof(App).Assembly.EntryPoint;

        Assert.NotNull(entry);
        Assert.Equal("DaouCalendarOverlay.Program", entry!.DeclaringType!.FullName);
        Assert.Equal("Main", entry.Name);
    }

    [Fact]
    public void EntryPoint_HasStaThreadAttribute()
    {
        var entry = typeof(App).Assembly.EntryPoint;

        Assert.NotNull(entry);
        Assert.Single(entry!.GetCustomAttributes(typeof(STAThreadAttribute), false));
    }

    [Fact]
    public void Main_DoesNotReferenceWpfApplicationDirectly()
    {
        // Main 본문이 App(WPF Application)을 직접 참조하면 host 모드에서도 Main JIT 시 WPF 어셈블리가 로드된다.
        var entry = typeof(App).Assembly.EntryPoint;
        Assert.NotNull(entry);

        var referenced = GetCalledMethods(entry!);

        Assert.Contains(referenced, method => method.DeclaringType == typeof(StartupModeParser)
            && method.Name == nameof(StartupModeParser.IsNativeInvocation));
        Assert.DoesNotContain(referenced, method => typeof(System.Windows.Application).IsAssignableFrom(method.DeclaringType));

        var runGui = entry!.DeclaringType!.GetMethod("RunGui", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(runGui);
        Assert.True(runGui!.MethodImplementationFlags.HasFlag(MethodImplAttributes.NoInlining));
        Assert.Contains(GetCalledMethods(runGui), method => method.DeclaringType == typeof(App));
    }

    [Fact]
    public void AppXaml_IsCompiledIntoBaml()
    {
        var assembly = typeof(App).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(resourceName);

        using var stream = assembly.GetManifestResourceStream(resourceName!);
        Assert.NotNull(stream);

        var keys = new List<string>();
        using (var reader = new ResourceReader(stream!))
        {
            var enumerator = reader.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Key is string key)
                    keys.Add(key);
            }
        }

        Assert.Contains(keys, key => string.Equals(key, "app.baml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HostSpawnMeasurementScript_Exists()
    {
        var root = FindRepositoryRoot();
        Assert.NotNull(root);

        var path = Path.Combine(root!, "tools", "Measure-HostSpawn.ps1");
        Assert.True(File.Exists(path), $"host 스폰 측정 스크립트가 없습니다: {path}");

        var content = File.ReadAllText(path);
        Assert.Contains("chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/", content);
        Assert.Contains("StandardInput", content);
    }

    [Fact]
    public void TechnicalDocumentation_HasPerformanceSection()
    {
        var root = FindRepositoryRoot();
        Assert.NotNull(root);

        var path = Path.Combine(root!, "docs", "DaouCalendarOverlay_Technical_Documentation.html");
        Assert.True(File.Exists(path), $"기술 문서가 없습니다: {path}");

        var html = File.ReadAllText(path);
        Assert.Contains("id=\"perf\"", html);
        Assert.Contains("성능 특성 (native host 스폰 비용)", html);

        // 신규 절이 추가돼도 기존 절 번호는 바뀌지 않아야 한다.
        Assert.Contains("<h2>5. 동일 EXE의 두 실행 모드</h2>", html);
        Assert.Contains("<h2>20. 핵심 상수와 운영 경로</h2>", html);
    }

    /// <summary>
    /// 테스트 출력 폴더에서 상위로 올라가며 DaouCalendarOverlay.sln이 있는 저장소 루트를 찾는다. 못 찾으면 null.
    /// </summary>
    private static string? FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DaouCalendarOverlay.sln")))
                return dir.FullName;
        }

        return null;
    }

    /// <summary>
    /// 메서드 IL에서 call/callvirt/newobj 뒤의 메타데이터 토큰을 풀어 호출 대상 메서드를 모은다.
    /// 피연산자 경계를 해석하지 않는 단순 스캔이므로 유효한 메서드 토큰으로 풀리지 않는 바이트열은 건너뛴다.
    /// </summary>
    private static List<MethodBase> GetCalledMethods(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        Assert.NotNull(il);

        var result = new List<MethodBase>();
        for (var i = 0; i + 4 < il!.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F or 0x73))
                continue;

            try
            {
                var resolved = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                if (resolved is not null)
                    result.Add(resolved);
            }
            catch (ArgumentException)
            {
                // 유효한 메서드 토큰이 아니다.
            }
        }

        return result;
    }
}
