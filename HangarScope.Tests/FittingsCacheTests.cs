using HangarScope.Services;

namespace HangarScope.Tests;

/// <summary>ESI caches /characters/{id}/fittings/ for ~5 minutes: a list generated before our own save/delete is stale.</summary>
public class FittingsCacheTests
{
    private static readonly DateTimeOffset Wrote = new(2026, 10, 17, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ListGeneratedBeforeTheWrite_IsStale() =>
        Assert.True(SyncService.IsStaleRead(Wrote, Wrote.AddMinutes(-3), Wrote.AddMinutes(1)));

    [Fact]
    public void ListGeneratedInTheSameSecond_IsStale() =>
        Assert.True(SyncService.IsStaleRead(Wrote, Wrote, Wrote.AddSeconds(30)));

    [Fact]
    public void ListWithoutTimestamp_IsStaleRightAfterTheWrite() =>
        Assert.True(SyncService.IsStaleRead(Wrote, null, Wrote.AddMinutes(2)));

    [Fact]
    public void ListGeneratedAfterTheWrite_IsFresh() =>
        Assert.False(SyncService.IsStaleRead(Wrote, Wrote.AddMinutes(5), Wrote.AddMinutes(6)));

    [Fact]
    public void AfterFifteenMinutes_AnyListIsAccepted() =>
        Assert.False(SyncService.IsStaleRead(Wrote, Wrote.AddMinutes(-1), Wrote.AddMinutes(16)));
}
