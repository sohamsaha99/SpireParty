using SpireParty.Core;

namespace SpireParty.Tests;

public class SeatTests
{
    [Theory]
    [InlineData("ravi", true)]
    [InlineData("ravi-2", true)]
    [InlineData("r", true)]
    [InlineData("abcdefghijklmnop", true)]
    [InlineData("abcdefghijklmnopq", false)]
    [InlineData("", false)]
    [InlineData("2ravi", false)]
    [InlineData("-ravi", false)]
    [InlineData("ra vi", false)]
    [InlineData("Ravi", false)]
    public void SeatIdRules(string id, bool valid) => Assert.Equal(valid, SeatRules.IsValidSeatId(id));

    [Fact]
    public void Normalize_LowercasesAndTrims() => Assert.Equal("ravi", SeatRules.Normalize("  Ravi "));

    [Fact]
    public void PlayerIds_StartAtFirstGuest_AreNeverReused()
    {
        var p = new PartyFile();
        ulong a = SeatRules.AllocatePlayerId(p);
        p.Seats.Add(new Seat { SeatId = "a", PlayerId = a });
        ulong b = SeatRules.AllocatePlayerId(p);
        p.Seats.RemoveAll(s => s.PlayerId == a);   // seat removed for good
        ulong c = SeatRules.AllocatePlayerId(p);

        Assert.Equal(SeatRules.FirstGuestPlayerId, a);
        Assert.Equal(a + 1, b);
        Assert.Equal(b + 1, c);
    }

    [Theory]
    [InlineData("Thursday Crew!", "thursday-crew")]
    [InlineData("  --a__b--  ", "a-b")]
    [InlineData("!!!", "")]
    [InlineData("C:\\..\\etc", "c-etc")]
    public void PartyFolderName_IsSafe(string name, string folder) => Assert.Equal(folder, SeatRules.PartyFolderName(name));

    [Fact]
    public void Token_GeneratedFormat()
    {
        for (int i = 0; i < 50; i++)
            Assert.Matches("^[a-z]+-[a-z]+-[1-9][0-9]$", SeatTokens.Generate());
    }

    [Fact]
    public void Token_HashVerify_ToleratesCaseAndSpacing_RejectsWrong()
    {
        string hash = SeatTokens.Hash("amber-otter-41");
        Assert.True(SeatTokens.Verify("amber-otter-41", hash));
        Assert.True(SeatTokens.Verify(" Amber Otter 41 ", hash));
        Assert.False(SeatTokens.Verify("amber-otter-42", hash));
        Assert.False(SeatTokens.Verify("", hash));
        Assert.False(SeatTokens.Verify("amber-otter-41", "garbage"));
        Assert.False(SeatTokens.Verify("amber-otter-41", "sha256$!!$!!"));
    }

    [Fact]
    public void Token_HashIsSalted() => Assert.NotEqual(SeatTokens.Hash("amber-otter-41"), SeatTokens.Hash("amber-otter-41"));

    [Fact]
    public void Limiter_BlocksAfterMaxFailures_UnblocksAfterWindow_ResetsOnSuccess()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var limiter = new FailedAttemptLimiter(3, TimeSpan.FromMinutes(1), () => now);

        for (int i = 0; i < 3; i++) limiter.RecordFailure("100.64.0.9");
        Assert.True(limiter.IsBlocked("100.64.0.9"));
        Assert.False(limiter.IsBlocked("100.64.0.10"));

        now = now.AddSeconds(61);
        Assert.False(limiter.IsBlocked("100.64.0.9"));

        limiter.RecordFailure("100.64.0.9");
        limiter.RecordFailure("100.64.0.9");
        limiter.RecordSuccess("100.64.0.9");
        limiter.RecordFailure("100.64.0.9");
        Assert.False(limiter.IsBlocked("100.64.0.9"));
    }

    [Fact]
    public void PartyStore_CreateListLoadSave_OnTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "spireparty-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PartyStore(dir);
            Assert.Empty(store.List());

            var (folder, party) = store.Create("Thursday Crew");
            Assert.Equal("thursday-crew", folder);
            Assert.Equal(SeatRules.HostPlayerId, Assert.Single(party.Seats).PlayerId);
            Assert.Throws<InvalidOperationException>(() => store.Create("thursday crew"));
            Assert.Throws<ArgumentException>(() => store.Create("!!!"));

            party.Seats.Add(new Seat { SeatId = "ravi", PlayerId = SeatRules.AllocatePlayerId(party) });
            store.Save(folder, party);

            Assert.Equal(new[] { "thursday-crew" }, store.List());
            var loaded = store.Load(folder);
            Assert.Equal(2, loaded.Seats.Count);
            Assert.Equal("Thursday Crew", loaded.Name);
            Assert.False(File.Exists(Path.Combine(store.PartyDir(folder), "party.json.tmp")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
