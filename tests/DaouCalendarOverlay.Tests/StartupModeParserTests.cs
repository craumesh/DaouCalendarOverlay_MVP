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

    [Fact]
    public void IsUninstallInvocation_WithUninstallFlag_ReturnsTrue()
    {
        Assert.True(StartupModeParser.IsUninstallInvocation(new[] { "--uninstall" }));
        Assert.True(StartupModeParser.IsUninstallInvocation(new[] { "  --uninstall  " }));
        Assert.True(StartupModeParser.IsUninstallInvocation(new string[] { null!, "--uninstall" }));
    }

    [Fact]
    public void IsUninstallInvocation_AcceptsSlashAndUpperCase_ReturnsTrue()
    {
        Assert.True(StartupModeParser.IsUninstallInvocation(new[] { "/UNINSTALL" }));
        Assert.True(StartupModeParser.IsUninstallInvocation(new[] { "-Uninstall" }));
    }

    [Fact]
    public void IsUninstallInvocation_EmptyArgs_ReturnsFalse()
    {
        Assert.False(StartupModeParser.IsUninstallInvocation(null));
        Assert.False(StartupModeParser.IsUninstallInvocation(Array.Empty<string>()));
        Assert.False(StartupModeParser.IsUninstallInvocation(new string[] { null!, "" }));
        Assert.False(StartupModeParser.IsUninstallInvocation(new[] { "--uninstall-now", "uninstall" }));
    }

    [Fact]
    public void IsUninstallInvocation_NativeInvocationArgs_ReturnsFalse()
    {
        Assert.False(StartupModeParser.IsUninstallInvocation(new[] { "chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/" }));
        Assert.False(StartupModeParser.IsUninstallInvocation(new[] { "chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/", "--parent-window=0" }));
    }
}
