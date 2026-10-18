using HangarScope.Models;
using HangarScope.Services;

namespace HangarScope.Tests;

public class FitSyncTests
{
    private const long Paladin = 28659, Golem = 28710, Laser = 3057, HeatSink = 2364, Torp = 2420;
    private const long Has = 1, Old = 2, Missing = 3, NoScope = 4, OldNoScope = 5;

    private static CharacterInfo Char(long id, bool write = true) => new()
    {
        Id = id, Name = "Pilot " + id,
        ScopesGranted = write ? EsiScopes.Required.ToList() : EsiScopes.Required.Where(s => s != EsiScopes.FittingsWrite).ToList(),
    };

    private static List<FitItem> Items(params (long type, string flag, long qty)[] items) =>
        items.Select(i => new FitItem { TypeId = i.type, Flag = i.flag, Qty = i.qty }).ToList();

    private static readonly List<FitItem> NewFit = Items((Laser, "HiSlot0", 1), (Laser, "HiSlot1", 1), (HeatSink, "LoSlot0", 1));
    private static readonly List<FitItem> OldFit = Items((Laser, "HiSlot0", 1), (HeatSink, "LoSlot0", 1));

    private static SavedFitting Fit(long id, long charId, string name, List<FitItem> items, long hull = Paladin) =>
        new() { FittingId = id, CharId = charId, Name = name, ShipTypeId = hull, Items = items };

    private static List<FitCopyStatus> Classify(IEnumerable<CharacterInfo> chars, params SavedFitting[] fits) =>
        FitSync.Classify(chars, new FitIndex(fits), Paladin, "Marauders IV No Dread", NewFit);

    [Fact]
    public void Classifies_has_outdated_missing_and_no_scope()
    {
        var chars = new[] { Char(Has), Char(Old), Char(Missing), Char(NoScope, write: false), Char(OldNoScope, write: false) };
        var s = Classify(chars,
            Fit(10, Has, "Some other name", NewFit),              // identical contents under any name count as having it
            Fit(20, Old, "Marauders IV No Dread", OldFit),
            Fit(50, OldNoScope, "Marauders IV No Dread", OldFit));

        Assert.Equal(new[] { Has, Old, Missing, NoScope, OldNoScope }, s.Select(x => x.CharId));
        Assert.Equal(new[] { FitCopyState.Has, FitCopyState.Outdated, FitCopyState.Missing, FitCopyState.NoScope, FitCopyState.NoScope },
            s.Select(x => x.State));
        Assert.Equal(new long[] { 20 }, s[1].OldCopies.Select(f => f.FittingId));
        Assert.Single(s[4].OldCopies);
        Assert.Equal(new[] { false, true, true, false, false }, s.Select(x => x.Selectable));
    }

    [Fact]
    public void Has_wins_over_scope_and_old_copies()
    {
        var s = Classify(new[] { Char(Has, write: false) },
            Fit(10, Has, "Marauders IV No Dread", NewFit), Fit(11, Has, "Marauders IV No Dread", OldFit));
        Assert.Equal(FitCopyState.Has, s[0].State);
        Assert.Empty(s[0].OldCopies);
    }

    [Fact]
    public void Name_match_ignores_case_whitespace_and_esi_truncation_but_not_hull()
    {
        var longName = "Marauders IV No Dread " + new string('x', 60);
        var chars = new[] { Char(1), Char(2), Char(3) };
        var s = FitSync.Classify(chars, new FitIndex(new[]
            {
                Fit(10, 1, "  marauders iv no dread ", OldFit),
                Fit(20, 2, longName[..FitSync.MaxNameLength], OldFit),          // ESI stored the cut-down name
                Fit(30, 3, "Marauders IV No Dread", OldFit, hull: Golem),     // same name on another hull is a different fit
            }), Paladin, "Marauders IV No Dread", NewFit);
        Assert.Equal(FitCopyState.Outdated, s[0].State);
        Assert.Equal(FitCopyState.Missing, s[2].State);

        var t = FitSync.Classify(chars, new FitIndex(new[] { Fit(20, 2, longName[..FitSync.MaxNameLength], OldFit) }), Paladin, longName, NewFit);
        Assert.Equal(FitCopyState.Outdated, t[1].State);
    }

    [Fact]
    public void Only_missing_is_preselected()
    {
        var s = Classify(new[] { Char(Has), Char(Old), Char(Missing), Char(NoScope, write: false) },
            Fit(10, Has, "x", NewFit), Fit(20, Old, "Marauders IV No Dread", OldFit));
        Assert.Equal(new[] { Missing }, FitSync.DefaultSelection(s));
    }

    [Fact]
    public void Plan_maps_selection_to_actions()
    {
        var s = Classify(new[] { Char(Has), Char(Old), Char(Missing), Char(NoScope, write: false) },
            Fit(10, Has, "x", NewFit), Fit(20, Old, "Marauders IV No Dread", OldFit));
        var all = new HashSet<long> { Has, Old, Missing, NoScope };

        var replace = FitSync.Plan(s, all, replaceOutdated: true);
        Assert.Equal(new[] { FitSyncAction.Nothing, FitSyncAction.Replace, FitSyncAction.Save, FitSyncAction.Nothing }, replace.Select(x => x.Action));
        Assert.Equal(new long[] { 20 }, replace[1].OldCopies.Select(f => f.FittingId));

        var keep = FitSync.Plan(s, all, replaceOutdated: false);
        Assert.Equal(FitSyncAction.SaveKeepOld, keep[1].Action);
        Assert.Empty(keep[1].OldCopies);

        var none = FitSync.Plan(s, new HashSet<long>(), replaceOutdated: true);
        Assert.All(none, x => Assert.Equal(FitSyncAction.Nothing, x.Action));
    }

    private sealed class FakeEsi
    {
        public readonly List<string> Calls = new();
        public readonly HashSet<long> FailSave = new();
        public readonly HashSet<long> FailDelete = new();

        public Task<(bool, string)> Save(long charId)
        {
            Calls.Add($"save {charId}");
            return Task.FromResult(FailSave.Contains(charId) ? (false, "ESI 502") : (true, "saved"));
        }

        public Task<(bool, string)> Delete(long charId, long fittingId)
        {
            Calls.Add($"delete {charId}/{fittingId}");
            return Task.FromResult(FailDelete.Contains(fittingId) ? (false, "ESI 404") : (true, "deleted"));
        }
    }

    private static FitSyncStep Step(long charId, FitSyncAction a, params long[] oldIds) =>
        new(charId, a, oldIds.Select(id => Fit(id, charId, "Marauders IV No Dread", OldFit)).ToList());

    [Fact]
    public async Task Replace_saves_before_deleting_every_old_copy()
    {
        var esi = new FakeEsi();
        var r = await FitSync.Execute(new[] { Step(Old, FitSyncAction.Replace, 20, 21) }, esi.Save, esi.Delete);
        Assert.Equal(new[] { "save 2", "delete 2/20", "delete 2/21" }, esi.Calls);
        Assert.True(r[0].Ok);
        Assert.Equal(2, r[0].Deleted);
    }

    [Fact]
    public async Task Failed_save_deletes_nothing_and_other_pilots_continue()
    {
        var esi = new FakeEsi { FailSave = { Old } };
        var r = await FitSync.Execute(new[]
        {
            Step(Old, FitSyncAction.Replace, 20),
            Step(Missing, FitSyncAction.Save),
            Step(Has, FitSyncAction.Nothing),
        }, esi.Save, esi.Delete);

        Assert.Equal(new[] { "save 2", "save 3" }, esi.Calls);
        Assert.Equal(2, r.Count);
        Assert.False(r[0].Ok);
        Assert.False(r[0].Saved);
        Assert.Equal(0, r[0].Deleted);
        Assert.Contains("old copy kept", r[0].Message);
        Assert.True(r[1].Ok);
    }

    [Fact]
    public async Task Failed_delete_is_reported_after_a_good_save()
    {
        var esi = new FakeEsi { FailDelete = { 21 } };
        var r = await FitSync.Execute(new[] { Step(Old, FitSyncAction.Replace, 20, 21) }, esi.Save, esi.Delete);
        Assert.True(r[0].Saved);
        Assert.False(r[0].Ok);
        Assert.Equal(1, r[0].Deleted);
        Assert.Contains("ESI 404", r[0].Message);
    }

    [Fact]
    public async Task Save_keep_old_never_deletes()
    {
        var esi = new FakeEsi();
        await FitSync.Execute(new[] { Step(Old, FitSyncAction.SaveKeepOld, 20) }, esi.Save, esi.Delete);
        Assert.Equal(new[] { "save 2" }, esi.Calls);
    }
}
