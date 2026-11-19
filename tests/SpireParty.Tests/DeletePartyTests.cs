using System;
using System.IO;
using System.Linq;
using SpireParty.Core;
using Xunit;

/// <summary>Deleting a party (moved to deleted/, newest kept) and forgetting a recent host.</summary>
public sealed class DeletePartyTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "spireparty-delete-" + Guid.NewGuid().ToString("N"));
    readonly PartyStore _store;

    public DeletePartyTests()
    {
        _store = new PartyStore(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    static DateTime At(int minute) => new(2026, 11, 18, 12, minute, 0);

    [Fact]
    public void Delete_moves_the_whole_folder_and_hides_the_party()
    {
        var (folder, _) = _store.Create("Thursday Crew");
        File.WriteAllText(_store.RunSavePath(folder), "run");
        Directory.CreateDirectory(_store.BackupsDir(folder));

        string moved = _store.Delete(folder, At(5), keep: 10);

        Assert.Empty(_store.List());
        Assert.False(Directory.Exists(_store.PartyDir(folder)));
        Assert.Equal(Path.Combine(_store.DeletedDir, $"{folder}-20261118-120500"), moved);
        Assert.True(File.Exists(Path.Combine(moved, PartyStore.PartyFileName)));
        Assert.Equal("run", File.ReadAllText(Path.Combine(moved, PartyStore.RunSaveFileName)));
        Assert.True(Directory.Exists(Path.Combine(moved, "backups")));
    }

    [Fact]
    public void Same_name_deleted_twice_in_one_second_keeps_both()
    {
        _store.Create("a");
        string first = _store.Delete("a", At(1), keep: 10);
        _store.Create("a");
        string second = _store.Delete("a", At(1), keep: 10);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2", second);
        Assert.Equal(2, _store.ListDeleted().Count);
        Assert.Equal(second, _store.ListDeleted()[0]);
    }

    [Fact]
    public void Only_the_newest_are_kept()
    {
        for (int i = 0; i < 5; i++)
        {
            _store.Create($"p{i}");
            _store.Delete($"p{i}", At(i), keep: 3);
        }
        var names = _store.ListDeleted().Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "p4-20261118-120400", "p3-20261118-120300", "p2-20261118-120200" }, names);
    }

    [Fact]
    public void Prune_leaves_other_files_and_folders_alone()
    {
        Directory.CreateDirectory(Path.Combine(_store.DeletedDir, "not-a-deleted-party"));
        File.WriteAllText(Path.Combine(_store.DeletedDir, "note.txt"), "x");
        for (int i = 0; i < 3; i++) { _store.Create($"p{i}"); _store.Delete($"p{i}", At(i), keep: 1); }

        Assert.Single(_store.ListDeleted());
        Assert.True(Directory.Exists(Path.Combine(_store.DeletedDir, "not-a-deleted-party")));
        Assert.True(File.Exists(Path.Combine(_store.DeletedDir, "note.txt")));
    }

    [Fact]
    public void Deleting_a_missing_party_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => _store.Delete("nope", At(0), keep: 10));
    }

    [Fact]
    public void DeletedPartiesKept_setting()
    {
        Assert.Equal(10, MachineConfig.Parse(null, out _).DeletedPartiesKept);
        Assert.Equal(3, MachineConfig.Parse("{\"deletedPartiesKept\":3}", out _).DeletedPartiesKept);
        Assert.Equal(10, MachineConfig.Parse("{\"deletedPartiesKept\":0}", out _).DeletedPartiesKept);
        var c = new MachineConfig { DeletedPartiesKept = 7 };
        Assert.Equal(7, MachineConfig.Parse(c.ToJson(), out _).DeletedPartiesKept);
    }

    [Fact]
    public void Forget_removes_only_that_recent_host()
    {
        var c = new MachineConfig();
        var a = new RecentHost("100.1.2.3", 33771, "ravi");
        var b = new RecentHost("100.1.2.3", 33771, "mia");
        c.Remember(a);
        c.Remember(b);

        Assert.True(c.Forget(new RecentHost("100.1.2.3", 33771, "ravi", "Ravi")));   // the name does not matter
        Assert.Equal(new[] { b }, c.RecentHosts);
        Assert.False(c.Forget(a));
        Assert.Single(MachineConfig.Parse(c.ToJson(), out _).RecentHosts);
    }
}
