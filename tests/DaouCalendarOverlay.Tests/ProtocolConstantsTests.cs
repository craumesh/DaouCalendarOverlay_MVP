using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void FrozenIdentifiers_MustNotChange()
    {
        Assert.Equal("DaouCalendarOverlay.NativeBridge.v8", NativeBridgeProtocol.PipeName);
        Assert.Equal("com.daou.calendar_overlay", NativeBridgeProtocol.HostName);
        Assert.Equal("gkchgbpcbljkgabjcgjelacfkphcmhmi", NativeBridgeProtocol.ExtensionId);
        Assert.Equal("chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/", NativeBridgeProtocol.ExtensionOrigin);
    }

    [Fact]
    public void ProtocolVersion_IsTwo()
    {
        Assert.Equal(2, NativeBridgeProtocol.ProtocolVersion);
    }
}
