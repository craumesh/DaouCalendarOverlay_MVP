using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// settings.json <c>CalendarNames</c>(캘린더 ID → 마지막으로 확인한 표시 이름) 사전의 병합·정리 순수 로직.
/// 조회 범위에 일정이 없는 캘린더도 컨텍스트 메뉴에서 ID 대신 이름으로 보이게 하는 fallback 저장본이다.
/// 파일·레지스트리에 접근하지 않고 예외를 던지지 않는다.
/// </summary>
public static class CalendarNameStore
{
    public const int MaxEntries = 200;

    /// <summary>이름이 확인된 캘린더만 store에 반영한다. 실제로 바뀌었으면 true.</summary>
    /// <remarks>
    /// 이름이 공백이거나 ID와 같은 문자열이면(= 이름 미확인) 건너뛴다.
    /// 새 키는 <see cref="MaxEntries"/> 미만일 때만 넣고, 기존 키의 이름 갱신은 항상 허용한다.
    /// </remarks>
    public static bool Merge(IDictionary<string, string> store, IEnumerable<CalendarDescriptor> descriptors)
    {
        if (store is null || descriptors is null)
            return false;

        var changed = false;
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null)
                continue;

            var id = descriptor.Id;
            var name = descriptor.Name;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, id, StringComparison.Ordinal))
                continue;

            if (store.TryGetValue(id, out var existing))
            {
                if (string.Equals(existing, name, StringComparison.Ordinal))
                    continue;

                store[id] = name;
                changed = true;
                continue;
            }

            if (store.Count >= MaxEntries)
                continue;

            store[id] = name;
            changed = true;
        }

        return changed;
    }

    /// <summary>저장본에서 유효한 항목만 남긴 사전을 만든다.</summary>
    /// <remarks>키·값이 공백이거나 키와 값이 같은 항목을 버린다. 결과 사전은 <see cref="StringComparer.Ordinal"/>을 쓴다.</remarks>
    public static Dictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? store)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (store is null)
            return result;

        foreach (var pair in store)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            if (string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                continue;

            result[pair.Key] = pair.Value;
        }

        return result;
    }
}
