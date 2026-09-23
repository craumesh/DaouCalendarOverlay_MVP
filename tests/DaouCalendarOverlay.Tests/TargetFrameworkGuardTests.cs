using System.Reflection;
using System.Runtime.Versioning;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// T2.7 .NET 10 시험(tools/net10-trial.ps1)은 두 csproj의 TargetFramework를 임시로 바꿨다가 되돌린다.
/// 바뀐 TFM이 저장소에 남으면 이 테스트가 실패한다. 측정 스크립트의 dotnet test만 --filter로 이 클래스를 제외하고,
/// 필터 없는 일반 dotnet test에서는 항상 실행된다.
/// </summary>
public sealed class TargetFrameworkGuardTests
{
    [Fact]
    public void AppAssembly_TargetsNet8Windows()
    {
        // T2.7 .NET 10 시험 후 TFM 원복 가드
        var attr = typeof(DaouCalendarOverlay.Services.NativeBridgeProtocol).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attr);
        Assert.StartsWith(".NETCoreApp,Version=v8.0", attr.FrameworkName, StringComparison.Ordinal);
    }

    [Fact]
    public void TestAssembly_TargetsNet8Windows()
    {
        // T2.7 .NET 10 시험 후 TFM 원복 가드
        var attr = typeof(TargetFrameworkGuardTests).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attr);
        Assert.StartsWith(".NETCoreApp,Version=v8.0", attr.FrameworkName, StringComparison.Ordinal);
    }
}
