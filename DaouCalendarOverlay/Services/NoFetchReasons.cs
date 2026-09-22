namespace DaouCalendarOverlay.Services;

/// <summary>
/// 브리지 config 응답의 <c>noFetchReason</c> 코드(소문자 스네이크)와 사용자 표시 문구 변환.
/// 코드 문자열은 이 클래스에만 둔다(다른 파일에서 리터럴을 중복 작성하지 않는다).
/// </summary>
public static class NoFetchReasons
{
    public const string NotConfigured = "not_configured";
    public const string InvalidBaseUrl = "invalid_base_url";
    public const string RangeNotReady = "range_not_ready";
    public const string LeaseActive = "lease_active";
    public const string Backoff = "backoff";
    public const string NotDue = "not_due";

    /// <summary>사용자가 설정을 고쳐야 풀리는 사유인지(일시적 스케줄링 사유와 구분).</summary>
    public static bool IsConfigurationProblem(string? reason) =>
        reason is NotConfigured or InvalidBaseUrl;

    /// <summary>상태 표시에 쓸 한국어 문구. 알 수 없는 코드는 그대로 돌려준다.</summary>
    public static string Describe(string? reason) => reason switch
    {
        NotConfigured => "DaouOffice 주소와 캘린더 ID를 설정해 주세요.",
        InvalidBaseUrl => "DaouOffice 주소는 https://회사이름.daouoffice.com 형식이어야 합니다.",
        RangeNotReady => "표시할 날짜 범위가 아직 준비되지 않았습니다.",
        LeaseActive => "이전 동기화 요청을 처리하는 중입니다.",
        Backoff => "재시도 대기 중입니다.",
        NotDue => "다음 동기화 시각까지 대기 중입니다.",
        _ => string.IsNullOrWhiteSpace(reason) ? "알 수 없는 사유" : reason!
    };
}
