namespace DaouCalendarOverlay.Services;

/// <summary>
/// <see cref="BaseUrlPolicy.Validate"/> 결과. 유효하면 <see cref="NormalizedBaseUrl"/>만,
/// 유효하지 않으면 사용자에게 그대로 보여줄 <see cref="Reason"/>만 채워진다.
/// </summary>
public sealed class BaseUrlValidation
{
    public bool IsValid { get; init; }
    public string? Reason { get; init; }
    public string? NormalizedBaseUrl { get; init; }

    public static BaseUrlValidation Valid(string normalizedBaseUrl) =>
        new() { IsValid = true, NormalizedBaseUrl = normalizedBaseUrl };

    public static BaseUrlValidation Invalid(string reason) =>
        new() { IsValid = false, Reason = reason };
}

/// <summary>
/// DaouOffice BaseUrl 허용 정책의 단일 판정 지점.
/// https 필수, 호스트는 *.daouoffice.com(서브도메인 1개 이상, apex 제외).
/// 설정창 저장 검증, <c>AppSettings.IsConfigured</c>, 브리지 <c>BuildConfig</c>가 모두 이것만 사용한다.
/// </summary>
public static class BaseUrlPolicy
{
    public const string DomainSuffix = ".daouoffice.com";
    public const string ApexDomain = "daouoffice.com";

    private const string EmptyReason = "DaouOffice 주소를 입력해 주세요. 예: https://회사이름.daouoffice.com";
    private const string MalformedReason = "주소 형식이 올바르지 않습니다. https://회사이름.daouoffice.com 형태의 전체 주소를 입력해 주세요.";
    private const string SchemeReason = "보안 연결(https)만 지원합니다. https:// 로 시작하는 주소를 입력해 주세요.";
    private const string UserInfoReason = "주소에 사용자 정보(@)를 포함할 수 없습니다.";
    private const string ApexReason = "daouoffice.com 자체는 사용할 수 없습니다. 회사 주소(예: https://회사이름.daouoffice.com)를 입력해 주세요.";
    private const string DomainReason = "DaouOffice 도메인(*.daouoffice.com)만 사용할 수 있습니다.";

    /// <summary>입력이 무엇이든 예외를 던지지 않는다.</summary>
    public static BaseUrlValidation Validate(string? baseUrl)
    {
        var raw = (baseUrl ?? string.Empty).Trim();
        raw = raw.TrimEnd('/');
        if (raw.Length == 0)
            return BaseUrlValidation.Invalid(EmptyReason);

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return BaseUrlValidation.Invalid(MalformedReason);

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return BaseUrlValidation.Invalid(SchemeReason);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return BaseUrlValidation.Invalid(UserInfoReason);

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();

        if (string.Equals(host, ApexDomain, StringComparison.Ordinal))
            return BaseUrlValidation.Invalid(ApexReason);

        if (!host.EndsWith(DomainSuffix, StringComparison.Ordinal))
            return BaseUrlValidation.Invalid(DomainReason);

        var label = host[..^DomainSuffix.Length];
        if (label.Length == 0 || label.EndsWith('.'))
            return BaseUrlValidation.Invalid(DomainReason);

        // GetLeftPart를 쓰지 않는다: 호스트 끝점(.)이 제거되지 않는다.
        var normalized = uri.IsDefaultPort ? $"https://{host}" : $"https://{host}:{uri.Port}";
        return BaseUrlValidation.Valid(normalized);
    }

    /// <summary>
    /// 시작 시 설정창에 띄울 거부 사유. 미설정(빈 값)은 사유가 없으므로 null.
    /// </summary>
    public static string? GetStartupNotice(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var validation = Validate(baseUrl);
        return validation.IsValid ? null : validation.Reason;
    }
}
