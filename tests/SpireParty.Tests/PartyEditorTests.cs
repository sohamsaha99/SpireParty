using MegaCrit.Sts2.Core.Saves.Runs;
using SpireParty.Core;

namespace SpireParty.Tests;

/// <summary>The roster editor's actions on a temp party folder holding the three-player fixture.</summary>
public sealed class PartyEditorTests : IDisposable
{
    const string Folder = "crew";
    readonly string _dir = Path.Combine(Path.GetTempPath(), "spireparty-tests-" + Guid.NewGuid().ToString("N"));
    readonly PartyStore _store;
    readonly PartyEditor _editor;
    DateTime _now = new(2026, 11, 17, 12, 0, 0);

    public PartyEditorTests()
    {
        _store = new PartyStore(_dir);
        Directory.CreateDirectory(_store.PartyDir(Folder));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "three-player.save"), _store.RunSavePath(Folder));
        var p = new PartyFile { Name = "Crew", NextPlayerId = 7_000_004 };
        p.Seats.Add(new Seat { SeatId = "host", PlayerId = 1, DisplayName = "Host" });
        p.Seats.Add(new Seat { SeatId = "tester", PlayerId = 7_000_002, DisplayName = "Tess", TokenHash = "sha256$a$b" });
        p.Seats.Add(new Seat { SeatId = "newbie", PlayerId = 7_000_003, DisplayName = "newbie", TokenHash = "sha256$a$b" });
        _store.Save(Folder, p);
        _editor = new PartyEditor(_store, new PartyBackups(_store, () => _now = _now.AddSeconds(1)), Folder, 20);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Stand-in for NewPlayer.ForEditor: a copy of the host's player with the new ID and starter-ish stats.</summary>
    static SerializablePlayer FakeBuild(string saveJson, string character, PartyFile party, ulong netId, int banes)
    {
        var run = PartyRunSave.FromBytes(System.Text.Encoding.UTF8.GetBytes(saveJson));
        var p = PartyRunSave.PlayerFromBytes(PartyRunSave.PlayerToBytes(run.Players[0]));
        p.NetId = netId;
        p.Gold = 99;
        p.MaxHp = 70;
        p.CurrentHp = 70;
        return p;
    }

    SeatView Seat(string id) => _editor.Seats(out _).Single(s => s.SeatId == id);

    // ---- settings (M3 task 3.6)

    [Fact]
    public void SaveSettings_WritesPartyJson_WithABackupFirst()
    {
        var s = new PartySettings(6, 1.5, 45, 3);
        Assert.Null(_editor.SaveSettings(s, out var changed));
        Assert.Equal("max players 6, enemy health ×1.5, turn timer 45 s, AFK after 3", changed);
        var party = _editor.LoadParty();
        Assert.Equal(s, PartySettings.Of(party));
        var backup = Assert.Single(_editor.Backups());
        Assert.Contains("settings: max players 6", backup.Reason);
    }

    [Fact]
    public void SaveSettings_NothingChanged_TakesNoBackup()
    {
        Assert.Null(_editor.SaveSettings(PartySettings.Of(_editor.LoadParty()), out var changed));
        Assert.Equal("", changed);
        Assert.Empty(_editor.Backups());
    }

    [Fact]
    public void SaveSettings_RefusesFewerMaxPlayersThanTheRunHas_AndChangesNothing()
    {
        string before = File.ReadAllText(Path.Combine(_store.PartyDir(Folder), "party.json"));
        string? err = _editor.SaveSettings(new PartySettings(2, 1.0, 0, 2), out _);
        Assert.Contains("already has 3 players", err);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_store.PartyDir(Folder), "party.json")));
        Assert.Empty(_editor.Backups());
    }

    [Fact]
    public void Seats_ShowStatusNameAndStats()
    {
        var seats = _editor.Seats(out var problem);
        Assert.Null(problem);
        Assert.Equal(new[] { "host", "active", "active" }, seats.Select(s => s.Status));
        Assert.Equal("Tess", seats[1].Name);
        Assert.False(seats[1].Open);
        Assert.True(seats[0].Hp > 0 && seats[0].Gold >= 0);
        Assert.StartsWith("CHARACTER.", seats[2].Character);
    }

    [Fact]
    public void BenchThenRestore_ThroughTheEditor()
    {
        var before = Seat("newbie");
        Assert.Null(_editor.Bench("newbie"));
        var benched = Seat("newbie");
        Assert.Equal("benched", benched.Status);
        Assert.Equal((before.Hp, before.Gold), (benched.Hp, benched.Gold)); // read from the benched file

        Assert.Null(_editor.Restore("newbie"));
        Assert.Equal("active", Seat("newbie").Status);
        Assert.Equal(2, _editor.Backups().Count);
    }

    [Fact]
    public void Bench_RefusalIsReturned_AndNoBackupIsTaken()
    {
        Assert.Contains("host seat", _editor.Bench("host"));
        Assert.Empty(_editor.Backups());
    }

    [Fact]
    public void Add_AppliesCatchUpAndLeavesTheSeatOpen()
    {
        int highestGold = _editor.Seats(out _).Max(s => s.Gold!.Value);
        Assert.Null(_editor.Add("Charlie", "Charlie C", "CHARACTER.SILENT", FakeBuild));

        var added = Seat("charlie");
        Assert.Equal("active", added.Status);
        Assert.True(added.Open);
        Assert.Equal("Charlie C", added.Name);
        Assert.Equal(Math.Max(99, highestGold), added.Gold); // default catch-up: highest
        Assert.Equal(added.MaxHp, added.Hp);
        Assert.Equal("add charlie", _editor.Backups()[0].Reason);
    }

    [Fact]
    public void Add_BuilderFailure_IsReported()
    {
        string? err = _editor.Add("charlie", "", "CHARACTER.NOPE", (_, _, _, _, _) => throw new ArgumentException("Unknown character"));
        Assert.Contains("Unknown character", err);
        Assert.Empty(_editor.Backups());
    }

    [Fact]
    public void RenameAndResetToken_ChangeOnlyPartyJson()
    {
        byte[] save = File.ReadAllBytes(_store.RunSavePath(Folder));
        Assert.Null(_editor.Rename("tester", "  Tess   the Brave "));
        Assert.Null(_editor.ResetToken("tester"));
        Assert.Equal("Tess the Brave", Seat("tester").Name);
        Assert.True(Seat("tester").Open);
        Assert.Equal(save, File.ReadAllBytes(_store.RunSavePath(Folder)));
        Assert.Equal("Enter a name.", _editor.Rename("tester", "   "));
    }

    [Fact]
    public void RestoreBackup_UndoesAnEdit()
    {
        Assert.Null(_editor.Bench("newbie"));
        string name = _editor.Backups()[0].Name;
        Assert.Null(_editor.RestoreBackup(name, out var undo));
        Assert.Equal("active", Seat("newbie").Status);
        Assert.NotNull(undo);
    }

    [Fact]
    public void NoRun_BenchAndAddRefused_RemoveWorks()
    {
        File.Delete(_store.RunSavePath(Folder));
        Assert.Contains("no run yet", _editor.Bench("newbie"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no run yet", _editor.Add("charlie", "", "CHARACTER.SILENT", FakeBuild), StringComparison.OrdinalIgnoreCase);
        Assert.Null(_editor.Remove("newbie"));
        Assert.Equal("removed", Seat("newbie").Status);
        Assert.Equal("The host seat cannot be removed.", _editor.Remove("host"));
    }

    [Fact]
    public void UnreadableRun_IsReportedAndEditsRefused()
    {
        File.WriteAllText(_store.RunSavePath(Folder), "{ broken");
        _editor.Seats(out var problem);
        Assert.Contains("cannot be read", problem);
        Assert.Contains("cannot be read", _editor.Bench("newbie"));
    }
}
