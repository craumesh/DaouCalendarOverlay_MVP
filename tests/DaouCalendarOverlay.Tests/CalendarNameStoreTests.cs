using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class CalendarNameStoreTests
{
    private static CalendarDescriptor Descriptor(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void Merge_AddsNamedCalendar_ReturnsTrue()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);

        var changed = CalendarNameStore.Merge(store, new[] { Descriptor("123", "팀 캘린더") });

        Assert.True(changed);
        Assert.Equal("팀 캘린더", store["123"]);
    }

    [Fact]
    public void Merge_SkipsDescriptorWhoseNameEqualsId()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);

        var changed = CalendarNameStore.Merge(store, new[] { Descriptor("123", "123") });

        Assert.False(changed);
        Assert.Empty(store);
    }

    [Fact]
    public void Merge_SkipsBlankIdOrName()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);

        var changed = CalendarNameStore.Merge(store, new[]
        {
            Descriptor("", "이름만 있음"),
            Descriptor("   ", "공백 ID"),
            Descriptor("123", ""),
            Descriptor("456", "   ")
        });

        Assert.False(changed);
        Assert.Empty(store);
    }

    [Fact]
    public void Merge_SameNameTwice_ReturnsFalseOnSecondCall()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptors = new[] { Descriptor("123", "팀 캘린더") };

        Assert.True(CalendarNameStore.Merge(store, descriptors));
        Assert.False(CalendarNameStore.Merge(store, descriptors));
        Assert.Single(store);
    }

    [Fact]
    public void Merge_UpdatesChangedName_ReturnsTrue()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal) { ["123"] = "옛 이름" };

        var changed = CalendarNameStore.Merge(store, new[] { Descriptor("123", "새 이름") });

        Assert.True(changed);
        Assert.Equal("새 이름", store["123"]);
    }

    [Fact]
    public void Merge_DoesNotAddBeyondMaxEntries()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < CalendarNameStore.MaxEntries; i++)
            store["id" + i] = "캘린더 " + i;

        var changed = CalendarNameStore.Merge(store, new[] { Descriptor("new-id", "새 캘린더") });

        Assert.False(changed);
        Assert.Equal(CalendarNameStore.MaxEntries, store.Count);
        Assert.False(store.ContainsKey("new-id"));
    }

    [Fact]
    public void Merge_AtMaxEntries_StillUpdatesExistingKey()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < CalendarNameStore.MaxEntries; i++)
            store["id" + i] = "캘린더 " + i;

        var changed = CalendarNameStore.Merge(store, new[] { Descriptor("id0", "바뀐 이름") });

        Assert.True(changed);
        Assert.Equal("바뀐 이름", store["id0"]);
        Assert.Equal(CalendarNameStore.MaxEntries, store.Count);
    }

    [Fact]
    public void Merge_NullStore_ReturnsFalse()
    {
        Assert.False(CalendarNameStore.Merge(null!, new[] { Descriptor("123", "팀 캘린더") }));
    }

    [Fact]
    public void Sanitize_DropsBlankAndIdEqualsNameEntries()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["123"] = "팀 캘린더",
            ["456"] = "456",
            [""] = "빈 키",
            ["   "] = "공백 키",
            ["789"] = "",
            ["790"] = "   "
        };

        var sanitized = CalendarNameStore.Sanitize(source);

        var entry = Assert.Single(sanitized);
        Assert.Equal("123", entry.Key);
        Assert.Equal("팀 캘린더", entry.Value);
        Assert.Same(StringComparer.Ordinal, sanitized.Comparer);
    }

    [Fact]
    public void Sanitize_NullInput_ReturnsEmpty()
    {
        var sanitized = CalendarNameStore.Sanitize(null);

        Assert.NotNull(sanitized);
        Assert.Empty(sanitized);
    }

    [Fact]
    public void Sanitize_ReturnsIndependentCopy()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal) { ["123"] = "팀 캘린더" };

        var sanitized = CalendarNameStore.Sanitize(source);
        source["456"] = "다른 캘린더";

        Assert.Single(sanitized);
    }
}
