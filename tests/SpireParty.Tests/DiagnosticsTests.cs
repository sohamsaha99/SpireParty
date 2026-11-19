using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpireParty.Core;
using SpireParty.GameAdapters;
using Xunit;

namespace SpireParty.Tests;

public class DiagnosticsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-diag-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    static readonly Dictionary<string, string> NoCopies = new();

    [Fact]
    public void WithoutTokenHashes_RemovesEveryHash_KeepsTheRest()
    {
        var party = new PartyFile { Name = "gate" };
        party.Seats.Add(new Seat { SeatId = "host", PlayerId = 1, TokenHash = "" });
        party.Seats.Add(new Seat { SeatId = "ravi", PlayerId = 7_000_002, TokenHash = SeatTokens.Hash("amber-otter-41") });
        string clean = DiagnosticsBundle.WithoutTokenHashes(party.ToJson())!;

        Assert.DoesNotContain(party.Seats[1].TokenHash, clean);
        Assert.Contains("\"ravi\"", clean);
        var back = PartyFile.Parse(clean, out _);
        Assert.Equal(2, back.Seats.Count);
        Assert.All(back.Seats, s => Assert.Equal(s.TokenHash.Length == 0 ? "" : "(removed)", s.TokenHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void WithoutTokenHashes_LeavesOutAnythingElse(string text) => Assert.Null(DiagnosticsBundle.WithoutTokenHashes(text));

    [Fact]
    public void Write_CreatesTimestampedFolderWithTextsAndCopies()
    {
        string log = Path.Combine(_dir, "game.log.src");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(log, "line 1\n");
        using var writer = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); // still being written

        string bundle = DiagnosticsBundle.Write(Path.Combine(_dir, "diagnostics"), new DateTime(2026, 11, 18, 23, 5, 9),
            new Dictionary<string, string> { ["divergence.txt"] = "ctx" },
            new Dictionary<string, string> { ["game.log"] = log, ["save.run"] = Path.Combine(_dir, "missing") }, keep: 10);

        Assert.Equal("20261118-230509", Path.GetFileName(bundle));
        Assert.Equal("ctx", File.ReadAllText(Path.Combine(bundle, "divergence.txt")));
        Assert.Equal("line 1\n", File.ReadAllText(Path.Combine(bundle, "game.log")));
        Assert.Contains("save.run", File.ReadAllText(Path.Combine(bundle, "missing.txt")));
    }

    [Fact]
    public void Write_SameSecondGetsASuffix()
    {
        var t = new DateTime(2026, 11, 18, 23, 5, 9);
        string a = DiagnosticsBundle.Write(_dir, t, new Dictionary<string, string>(), NoCopies, 10);
        string b = DiagnosticsBundle.Write(_dir, t, new Dictionary<string, string>(), NoCopies, 10);
        Assert.NotEqual(a, b);
        Assert.Equal("20261118-230509-2", Path.GetFileName(b));
    }

    [Fact]
    public void Prune_KeepsNewestBundlesOnly_AndLeavesOtherFoldersAlone()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "notes"));
        for (int i = 0; i < 5; i++)
            DiagnosticsBundle.Write(_dir, new DateTime(2026, 11, 18, 23, 0, i), new Dictionary<string, string>(), NoCopies, keep: 3);

        var left = Directory.GetDirectories(_dir).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "20261118-230002", "20261118-230003", "20261118-230004", "notes" }, left);
    }

    [Fact]
    public void Config_DiagnosticsKeptDefaultsTo10()
    {
        Assert.Equal(10, MachineConfig.Parse(null, out _).DiagnosticsKept);
        Assert.Equal(3, MachineConfig.Parse("{\"diagnosticsKept\":3}", out _).DiagnosticsKept);
        Assert.Equal(10, MachineConfig.Parse("{\"diagnosticsKept\":0}", out _).DiagnosticsKept);
    }
}

[Collection("GameIL")]
public class DivergenceAdapterTests
{
    [Fact]
    public void SelfCheck_FindsTheChecksumHooksInThisGameBuild() => Assert.Null(new DivergenceAdapter().SelfCheck());
}
