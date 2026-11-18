using System.Text;
using SpireParty.Core;
using SpireParty.Net;

namespace SpireParty.Tests;

/// <summary>Seat display names (M2 task 2.6): cleaning, the names table, and the control messages.</summary>
public class NamesTests
{
    [Theory]
    [InlineData("  Ravi  ", "Ravi")]
    [InlineData("Ravi   the\tBold", "Ravi the Bold")]
    [InlineData("[color=red]Ravi[/color]", "color=redRavi/color")]
    [InlineData("Ra\u0000vi\n", "Ravi")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("Émilie 🎲", "Émilie 🎲")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnopqrstuvwx")]
    public void Clean(string? input, string expected) => Assert.Equal(expected, SeatNames.Clean(input));

    [Fact]
    public void Clean_NeverSplitsASurrogatePair()
    {
        string name = new string('a', SeatNames.MaxLength - 1) + "🎲";
        Assert.Equal(new string('a', SeatNames.MaxLength - 1), SeatNames.Clean(name));
    }

    [Fact]
    public void Of_UsesDisplayNameOrSeatId_AndSkipsRemovedSeats()
    {
        var party = new PartyFile();
        party.Seats.Add(new Seat { SeatId = "host", PlayerId = 1, DisplayName = "Host" });
        party.Seats.Add(new Seat { SeatId = "ravi", PlayerId = 7_000_002, DisplayName = "Ravi" });
        party.Seats.Add(new Seat { SeatId = "mo", PlayerId = 7_000_003, DisplayName = "  " });
        party.Seats.Add(new Seat { SeatId = "bench", PlayerId = 7_000_004, DisplayName = "B", State = SeatState.Benched });
        party.Seats.Add(new Seat { SeatId = "gone", PlayerId = 7_000_005, DisplayName = "G", State = SeatState.Empty });

        var names = SeatNames.Of(party);

        Assert.Equal(new Dictionary<ulong, string>
        {
            [1] = "Host", [7_000_002] = "Ravi", [7_000_003] = "mo", [7_000_004] = "B",
        }, names);
    }

    [Fact]
    public void Table_SetTryGetClear()
    {
        SeatNames.Set(new Dictionary<ulong, string> { [7_000_002] = "Ravi", [7_000_003] = "" });
        Assert.True(SeatNames.TryGet(7_000_002, out var n));
        Assert.Equal("Ravi", n);
        Assert.False(SeatNames.TryGet(7_000_003, out _)); // an empty name falls through to the game
        Assert.False(SeatNames.TryGet(42, out _));
        SeatNames.Clear();
        Assert.False(SeatNames.TryGet(7_000_002, out _));
    }

    // ---- control messages

    static readonly Dictionary<ulong, string> Names = new() { [1] = "Host", [7_000_002] = "Ravi \"R\" 🎲" };

    static ControlMessage RoundTrip(ControlMessage m) => ControlCodec.Decode(ControlCodec.Encode(m).AsSpan(4));

    [Fact]
    public void Hello_CarriesTheName()
    {
        var back = Assert.IsType<Hello>(RoundTrip(new Hello("0.1", "v", "c", "ravi", "", "Ravi")));
        Assert.Equal("Ravi", back.Name);
    }

    [Fact]
    public void Welcome_CarriesAllNames_OrNone()
    {
        var back = Assert.IsType<Welcome>(RoundTrip(new Welcome(7_000_002, "ravi", "gate", null, "everything", 2, Names)));
        Assert.Equal(Names, back.Names);
        Assert.Null(Assert.IsType<Welcome>(RoundTrip(new Welcome(7_000_002, "ravi", "gate", null, "everything", 2))).Names);
    }

    [Fact]
    public void Roster_RoundTrips()
    {
        Assert.Equal(Names, Assert.IsType<Roster>(RoundTrip(new Roster(Names))).Names);
    }

    [Fact]
    public void Roster_BadEntriesAreSkipped()
    {
        string json = """{"type":"roster","names":{"7000002":"Ravi","x":"bad key","-1":"neg","7000003":5}}""";
        var roster = Assert.IsType<Roster>(ControlCodec.Decode(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(new Dictionary<ulong, string> { [7_000_002] = "Ravi" }, roster.Names);
    }

    [Fact]
    public void TenLongNames_FitInOneFrame()
    {
        var names = Enumerable.Range(0, 10).ToDictionary(i => 7_000_002UL + (ulong)i, _ => string.Concat(Enumerable.Repeat("🎲", SeatNames.MaxLength / 2)));
        Assert.True(ControlCodec.Encode(new Roster(names)).Length < ControlCodec.MaxFrameBytes);
    }

    [Fact]
    public void RecentHost_RemembersTheName()
    {
        var c = MachineConfig.Parse(null, out _);
        c.Remember(new RecentHost("100.64.0.1", 33771, "ravi", "Ravi"));
        Assert.Equal("Ravi", MachineConfig.Parse(c.ToJson(), out _).RecentHosts[0].Name);
        Assert.Equal("", MachineConfig.Parse("""{"recentHosts":[{"address":"a","seatId":"ravi"}]}""", out _).RecentHosts[0].Name);
    }
}
