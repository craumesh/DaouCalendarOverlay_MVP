using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 기동 실패 예외를 사용자 표시용 사유 문구로 바꾸는 <see cref="StartupFailureReasons"/>의 분류 규칙을 고정하는 테스트.
/// 문구는 상태 표시줄과 MessageBox에 그대로 나가므로 문자열을 글자 단위로 단언한다.
/// </summary>
public sealed class StartupFailureReasonsTests
{
    /// <summary>권한 거부(정책·ACL)는 쓰기 권한 문구로 분류된다.</summary>
    [Fact]
    public void Describe_UnauthorizedAccess_ReturnsPermissionReason()
    {
        Assert.Equal(
            "쓰기 권한이 없습니다(정책 또는 ACL 제한)",
            StartupFailureReasons.Describe(new UnauthorizedAccessException("denied")));
    }

    /// <summary>보안 예외는 보안 정책 차단 문구로 분류된다.</summary>
    [Fact]
    public void Describe_SecurityException_ReturnsPolicyReason()
    {
        Assert.Equal(
            "보안 정책이 접근을 차단했습니다",
            StartupFailureReasons.Describe(new System.Security.SecurityException("blocked")));
    }

    /// <summary>DirectoryNotFoundException은 IOException 파생이지만 경로 문구로 먼저 분류된다.</summary>
    [Fact]
    public void Describe_DirectoryNotFound_ReturnsPathReason()
    {
        Assert.Equal(
            "필요한 경로를 찾을 수 없습니다",
            StartupFailureReasons.Describe(new DirectoryNotFoundException("nope")));
    }

    /// <summary>일반 IOException은 파일 쓰기 실패 문구로 분류된다.</summary>
    [Fact]
    public void Describe_IOException_ReturnsFileReason()
    {
        Assert.Equal(
            "파일을 쓰지 못했습니다(사용 중이거나 디스크 문제)",
            StartupFailureReasons.Describe(new IOException("locked")));
    }

    /// <summary>확장 리소스 누락(InvalidOperationException)은 리소스 문구로 분류된다.</summary>
    [Fact]
    public void Describe_InvalidOperation_ReturnsResourceReason()
    {
        Assert.Equal(
            "필요한 리소스를 찾지 못했습니다",
            StartupFailureReasons.Describe(new InvalidOperationException("Chrome extension resource not found: x")));
    }

    /// <summary>예외가 없으면 알 수 없는 오류로 표시한다.</summary>
    [Fact]
    public void Describe_Null_ReturnsUnknown()
    {
        Assert.Equal("알 수 없는 오류", StartupFailureReasons.Describe(null));
    }

    /// <summary>내부 예외가 하나뿐인 AggregateException은 내부 예외 기준으로 분류된다.</summary>
    [Fact]
    public void Describe_AggregateWithSingleInner_UnwrapsInner()
    {
        Assert.Equal(
            "쓰기 권한이 없습니다(정책 또는 ACL 제한)",
            StartupFailureReasons.Describe(new AggregateException(new UnauthorizedAccessException())));
    }

    /// <summary>알 수 없는 예외는 메시지의 줄바꿈과 연속 공백을 한 칸으로 줄여 한 줄로 만든다.</summary>
    [Fact]
    public void Describe_UnknownException_CollapsesWhitespace()
    {
        Assert.Equal("첫 줄 둘째 줄", StartupFailureReasons.Describe(new Exception("첫 줄\r\n둘째  줄")));
    }

    /// <summary>긴 메시지는 최대 길이로 잘리고 끝이 말줄임표가 된다.</summary>
    [Fact]
    public void Describe_UnknownException_TruncatesToMaxLength()
    {
        var result = StartupFailureReasons.Describe(new Exception(new string('가', 300)));

        Assert.Equal(StartupFailureReasons.MaxLength, result.Length);
        Assert.EndsWith("…", result);
    }

    /// <summary>메시지가 공백뿐이면 예외 타입 이름을 사유로 쓴다.</summary>
    [Fact]
    public void Describe_BlankMessage_ReturnsTypeName()
    {
        Assert.Equal("Exception", StartupFailureReasons.Describe(new Exception("   ")));
    }
}
