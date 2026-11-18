using SpireParty.Core;

namespace SpireParty.Tests;

/// <summary>Backups and roster commits on a temp data dir, with a fixed clock.</summary>
public sealed class BackupTests : IDisposable
{
    const string Folder = "crew";
    const int Keep = 20;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "spireparty-tests-" + Guid.NewGuid().ToString("N"));
    readonly PartyStore _store;
    DateTime _now = new(2026, 11, 17, 12, 0, 0);
    readonly PartyBackups _backups;

    public BackupTests()
    {
        _store = new PartyStore(_dir);
        _backups = new PartyBackups(_store, () => _now);
        Directory.CreateDirectory(_store.PartyDir(Folder));
        File.Copy(Fixture, _store.RunSavePath(Folder));
        File.WriteAllText(_store.RunSavePath(Folder) + ".backup", "older run");
        _store.Save(Folder, ThreeParty());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "three-player.save");

    static PartyFile ThreeParty()
    {
        var p = new PartyFile { Name = "crew", NextPlayerId = 7_000_004 };
        p.Seats.Add(new Seat { SeatId = "host", PlayerId = 1, DisplayName = "Host" });
        p.Seats.Add(new Seat { SeatId = "tester", PlayerId = 7_000_002, DisplayName = "tester" });
        p.Seats.Add(new Seat { SeatId = "newbie", PlayerId = 7_000_003, DisplayName = "newbie" });
        return p;
    }

    byte[] Bytes(string path) => File.ReadAllBytes(path);
    string BackupDir(string name) => Path.Combine(_store.BackupsDir(Folder), name);

    // ---- take, list, prune

    [Fact]
    public void Take_CopiesEverythingAndRecordsReasonAndRoster()
    {
        Directory.CreateDirectory(_store.BenchedDir(Folder));
        File.WriteAllText(_store.BenchedPath(Folder, "old"), "{}");

        string name = _backups.Take(Folder, "bench tester", Keep);

        Assert.Equal("20261117-120000", name);
        Assert.Equal(Bytes(_store.RunSavePath(Folder)), Bytes(Path.Combine(BackupDir(name), "run.save")));
        Assert.Equal(Bytes(_store.PartyFilePath(Folder)), Bytes(Path.Combine(BackupDir(name), "party.json")));
        Assert.True(File.Exists(Path.Combine(BackupDir(name), "benched", "old.json")));
        Assert.False(File.Exists(Path.Combine(BackupDir(name), "run.save.backup")));

        var info = Assert.Single(_backups.List(Folder));
        Assert.Equal("bench tester", info.Reason);
        Assert.Equal("host, tester, newbie", info.Roster);
        Assert.Equal(_now, info.Time);
    }

    [Fact]
    public void Take_WithoutRunSave_StillWorks()
    {
        File.Delete(_store.RunSavePath(Folder));
        string name = _backups.Take(Folder, "rename", Keep);
        Assert.False(File.Exists(Path.Combine(BackupDir(name), "run.save")));
        Assert.True(File.Exists(Path.Combine(BackupDir(name), "party.json")));
    }

    [Fact]
    public void Take_TwiceInOneSecond_GetsDistinctNamesNewestFirst()
    {
        string a = _backups.Take(Folder, "a", Keep);
        string b = _backups.Take(Folder, "b", Keep);
        Assert.Equal("20261117-120000-2", b);
        Assert.Equal(new[] { b, a }, _backups.List(Folder).Select(x => x.Name));
    }

    [Fact]
    public void Take_KeepsOnlyTheNewest()
    {
        for (int i = 0; i < 5; i++)
        {
            _backups.Take(Folder, $"edit {i}", keep: 3);
            _now = _now.AddMinutes(1);
        }
        Assert.Equal(new[] { "edit 4", "edit 3", "edit 2" }, _backups.List(Folder).Select(b => b.Reason));
    }

    // ---- restore

    [Fact]
    public void Restore_PutsFilesBackAndCanBeUndone()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        byte[] partyBefore = Bytes(_store.PartyFilePath(Folder));
        string first = _backups.Take(Folder, "first", Keep);

        // Change everything after the backup.
        File.WriteAllText(_store.RunSavePath(Folder), "changed");
        var party = ThreeParty();
        party.Seats[2].State = SeatState.Benched;
        _store.Save(Folder, party);
        Directory.CreateDirectory(_store.BenchedDir(Folder));
        File.WriteAllText(_store.BenchedPath(Folder, "newbie"), "benched newbie");
        byte[] partyChanged = Bytes(_store.PartyFilePath(Folder));
        _now = _now.AddMinutes(1);

        string undo = _backups.Restore(Folder, first, Keep);

        Assert.Equal(saveBefore, Bytes(_store.RunSavePath(Folder)));
        Assert.Equal(partyBefore, Bytes(_store.PartyFilePath(Folder)));
        Assert.False(Directory.Exists(_store.BenchedDir(Folder)));
        Assert.Equal("before restoring 20261117-120000", _backups.List(Folder).First().Reason);

        _now = _now.AddMinutes(1);
        _backups.Restore(Folder, undo, Keep);
        Assert.Equal("changed", File.ReadAllText(_store.RunSavePath(Folder)));
        Assert.Equal(partyChanged, Bytes(_store.PartyFilePath(Folder)));
        Assert.Equal("benched newbie", File.ReadAllText(_store.BenchedPath(Folder, "newbie")));
    }

    [Fact]
    public void Restore_BackupWithoutRun_RemovesRunSaveAndItsBackup()
    {
        File.Move(_store.RunSavePath(Folder), Path.Combine(_dir, "aside.save"));
        string noRun = _backups.Take(Folder, "no run yet", Keep);
        File.Move(Path.Combine(_dir, "aside.save"), _store.RunSavePath(Folder));
        _now = _now.AddMinutes(1);

        _backups.Restore(Folder, noRun, Keep);

        Assert.False(File.Exists(_store.RunSavePath(Folder)));
        Assert.False(File.Exists(_store.RunSavePath(Folder) + ".backup"));
        Assert.False(PartyRunSave.Exists(_store.RunSavePath(Folder)));
    }

    [Fact]
    public void Restore_WorksEvenWhenTheSafetyBackupPrunesTheTarget()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        string only = _backups.Take(Folder, "only", keep: 1);
        File.WriteAllText(_store.RunSavePath(Folder), "changed");
        _now = _now.AddMinutes(1);

        _backups.Restore(Folder, only, keep: 1);

        Assert.Equal(saveBefore, Bytes(_store.RunSavePath(Folder)));
        Assert.Equal("before restoring 20261117-120000", Assert.Single(_backups.List(Folder)).Reason);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("../crew")]
    public void Restore_UnknownOrUnsafeName_Throws(string name)
    {
        Assert.Throws<InvalidOperationException>(() => _backups.Restore(Folder, name, Keep));
    }

    // ---- commit

    [Fact]
    public void Commit_Bench_WritesSavePartyAndBenchedFileAfterABackup()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        var run = PartyRunSave.FromBytes(saveBefore);
        var party = _store.Load(Folder);
        var result = RosterEdit.Bench(run, party, "newbie");
        Assert.True(result.Ok, result.Refusal);

        Assert.Null(RosterCommit.Write(_store, _backups, Folder, Keep, "bench newbie", run, party, "newbie", result));

        var onDisk = PartyRunSave.FromBytes(Bytes(_store.RunSavePath(Folder)));
        Assert.Equal(new[] { 1UL, 7_000_002UL }, onDisk.Players.Select(p => p.NetId));
        Assert.Equal(SeatState.Benched, _store.Load(Folder).Seats.Single(s => s.SeatId == "newbie").State);
        Assert.Equal(PartyRunSave.PlayerToBytes(result.BenchedPlayer!), Bytes(_store.BenchedPath(Folder, "newbie")));
        Assert.False(File.Exists(_store.RunSavePath(Folder) + ".backup"));

        var backup = Assert.Single(_backups.List(Folder));
        Assert.Equal("bench newbie", backup.Reason);
        Assert.Equal(saveBefore, Bytes(Path.Combine(BackupDir(backup.Name), "run.save")));
    }

    [Fact]
    public void Commit_BenchThenRestore_RoundTripsThroughTheBenchedFile()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        var run = PartyRunSave.FromBytes(saveBefore);
        var party = _store.Load(Folder);
        var bench = RosterEdit.Bench(run, party, "newbie");
        Assert.Null(RosterCommit.Write(_store, _backups, Folder, Keep, "bench", run, party, "newbie", bench));

        run = PartyRunSave.FromBytes(Bytes(_store.RunSavePath(Folder)));
        party = _store.Load(Folder);
        var restore = RosterEdit.Restore(run, party, "newbie", RosterCommit.ReadBenched(_store, Folder, "newbie"));
        Assert.True(restore.Ok, restore.Refusal);
        _now = _now.AddMinutes(1);
        Assert.Null(RosterCommit.Write(_store, _backups, Folder, Keep, "restore", run, party, "newbie", restore));

        Assert.Equal(PartyRunSave.ToBytes(PartyRunSave.FromBytes(saveBefore)), Bytes(_store.RunSavePath(Folder)));
        Assert.False(File.Exists(_store.BenchedPath(Folder, "newbie")));
        Assert.Equal(SeatState.Active, _store.Load(Folder).Seats.Single(s => s.SeatId == "newbie").State);
    }

    [Fact]
    public void Commit_PartyOnly_LeavesTheRunAlone()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        var party = _store.Load(Folder);
        party.Seats[1].DisplayName = "Tess";
        Assert.Null(RosterCommit.Write(_store, _backups, Folder, Keep, "rename tester", null, party));
        Assert.Equal(saveBefore, Bytes(_store.RunSavePath(Folder)));
        Assert.Equal("Tess", _store.Load(Folder).Seats[1].DisplayName);
    }

    [Fact]
    public void Commit_WriteFailure_PutsThePartyBack()
    {
        byte[] saveBefore = Bytes(_store.RunSavePath(Folder));
        byte[] partyBefore = Bytes(_store.PartyFilePath(Folder));
        File.WriteAllText(_store.BenchedDir(Folder), "a file where the benched folder should be");

        var run = PartyRunSave.FromBytes(saveBefore);
        var party = _store.Load(Folder);
        var result = RosterEdit.Bench(run, party, "newbie");
        string? error = RosterCommit.Write(_store, _backups, Folder, Keep, "bench newbie", run, party, "newbie", result);

        Assert.NotNull(error);
        Assert.Contains("put back as it was", error);
        Assert.Equal(saveBefore, Bytes(_store.RunSavePath(Folder)));
        Assert.Equal(partyBefore, Bytes(_store.PartyFilePath(Folder)));
    }

    [Fact]
    public void ReadBenched_MissingFile_SaysSo()
    {
        var e = Assert.Throws<InvalidOperationException>(() => RosterCommit.ReadBenched(_store, Folder, "ghost"));
        Assert.Contains("ghost", e.Message);
    }
}
