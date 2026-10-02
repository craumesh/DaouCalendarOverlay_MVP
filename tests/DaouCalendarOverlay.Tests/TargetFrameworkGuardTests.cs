using System.Reflection;
using System.Runtime.Versioning;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 7.3.1부터 대상 프레임워크는 net10.0-windows(.NET 10 LTS)다.
/// 앱·테스트 어셈블리의 TargetFrameworkAttribute가 v10.0이 아니면 실패한다.
/// tools/net10-trial.ps1의 dotnet test만 --filter로 이 클래스를 제외하고,
/// 필터 없는 일반 dotnet test에서는 항상 실행된다.
/// </summary>
public sealed class TargetFrameworkGuardTests
{
    [Fact]
    public void AppAssembly_TargetsNet10Windows()
    {
        // 7.3.1 .NET 10 전환 고정 가드
        var attr = typeof(DaouCalendarOverlay.Services.NativeBridgeProtocol).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attr);
        Assert.StartsWith(".NETCoreApp,Version=v10.0", attr.FrameworkName, StringComparison.Ordinal);
    }

    [Fact]
    public void TestAssembly_TargetsNet10Windows()
    {
        // 7.3.1 .NET 10 전환 고정 가드
        var attr = typeof(TargetFrameworkGuardTests).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attr);
        Assert.StartsWith(".NETCoreApp,Version=v10.0", attr.FrameworkName, StringComparison.Ordinal);
    }
}
