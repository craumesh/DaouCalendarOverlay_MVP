using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class StartupModeParserTests
{
    [Fact]
    public void IsNativeInvocation_ReturnsFalse_ForNullOrEmptyArgs()
    {
        Assert.False(StartupModeParser.IsNativeInvocation(null));
        Assert.False(StartupModeParser.IsNativeInvocation(Array.Empty<string>()));
    }

    [Fact]
    public void IsNativeInvocation_ReturnsTrue_ForExtensionOrigin()
    {
        Assert.True(StartupModeParser.IsNativeInvocation(new[] { NativeBridgeProtocol.ExtensionOrigin }));
    }

    [Fact]
    public void IsNativeInvocation_ReturnsTrue_WhenOriginIsNotFirstArg()
    {
        Assert.True(StartupModeParser.IsNativeInvocation(new[] { "--parent-window=0", NativeBridgeProtocol.ExtensionOrigin }));
    }

    [Fact]
    public void IsNativeInvocation_IsCaseInsensitive()
    {
        Assert.True(StartupModeParser.IsNativeInvocation(new[] { NativeBridgeProtocol.ExtensionOrigin.ToUpperInvariant() }));
    }

    [Fact]
    public void IsNativeInvocation_ReturnsFalse_ForOtherExtensionId()
    {
        Assert.False(StartupModeParser.IsNativeInvocation(new[] { "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/" }));
    }

    [Fact]
    public void IsNativeInvocation_ReturnsFalse_ForUninstallSwitch()
    {
        Assert.False(StartupModeParser.IsNativeInvocation(new[] { "--uninstall" }));
    }

    [Fact]
    public void IsNativeInvocation_IgnoresNullElements()
    {
        Assert.False(StartupModeParser.IsNativeInvocation(new string[] { null! }));
        Assert.True(StartupModeParser.IsNativeInvocation(new string[] { null!, NativeBridgeProtocol.ExtensionOrigin }));
    }

    [Fact]
    public void NativeMessagingHost_IsNativeInvocation_DelegatesToParser()
    {
        var origin = new[] { NativeBridgeProtocol.ExtensionOrigin };
        var uninstall = new[] { "--uninstall" };

        Assert.Equal(StartupModeParser.IsNativeInvocation(origin), NativeMessagingHost.IsNativeInvocation(origin));
        Assert.Equal(StartupModeParser.IsNativeInvocation(uninstall), NativeMessagingHost.IsNativeInvocation(uninstall));
    }
}
