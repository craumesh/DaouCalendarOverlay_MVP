using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class ProtocolConstantsTests
{
    [Fact]
    public void FrozenIdentifiers_MustNotChange()
    {
        Assert.Equal("DaouCalendarOverlay.NativeBridge.v7", NativeBridgeProtocol.PipeName);
        Assert.Equal("com.daou.calendar_overlay", NativeBridgeProtocol.HostName);
        Assert.Equal("gkchgbpcbljkgabjcgjelacfkphcmhmi", NativeBridgeProtocol.ExtensionId);
        Assert.Equal("chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/", NativeBridgeProtocol.ExtensionOrigin);
    }

    [Fact]
    public void ProtocolVersion_IsOne()
    {
        Assert.Equal(1, NativeBridgeProtocol.ProtocolVersion);
    }
}
