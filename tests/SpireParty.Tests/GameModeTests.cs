using System;
using System.Text;
using SpireParty.Core;
using SpireParty.Net;
using Xunit;

namespace SpireParty.Tests;

public class GameModeTests
{
    [Fact]
    public void PartyFile_ModeDefaultsToStandard()
    {
        Assert.Equal("standard", PartyFile.Parse(null, out _).Mode);
        Assert.Equal("standard", PartyFile.Parse("{}", out var w).Mode);
        Assert.Empty(w);
    }

    [Theory]
    [InlineData("daily", "daily")]
    [InlineData("Blitz", "blitz")]
    [InlineData("DREAD", "dread")]
    [InlineData("custom", "custom")]
    public void PartyFile_ReadsMode(string written, string expected) =>
        Assert.Equal(expected, PartyFile.Parse($"{{\"mode\":\"{written}\"}}", out _).Mode);

    [Fact]
    public void PartyFile_UnknownModeIsStandardWithAWarning()
    {
        var p = PartyFile.Parse("{\"mode\":\"endless\"}", out var warnings);
        Assert.Equal("standard", p.Mode);
        Assert.Contains(warnings, x => x.Contains("mode"));
    }

    [Fact]
    public void PartyFile_ModeRoundTrips()
    {
        var p = new PartyFile { Name = "x", Mode = PartyModes.Dread };
        Assert.Equal("dread", PartyFile.Parse(p.ToJson(), out _).Mode);
    }

    [Fact]
    public void Store_CreatesPartyWithMode()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sp-modes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PartyStore(dir);
            var (folder, _) = store.Create("Daily Crew", "daily");
            Assert.Equal("daily", store.Load(folder).Mode);
            var (folder2, _) = store.Create("plain");
            Assert.Equal("standard", store.Load(folder2).Mode);
        }
        finally
        {
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Welcome_CarriesMode()
    {
        var w = new Welcome(7000002, "ravi", "gate", null, "everything", 2, Mode: "blitz");
        var frame = ControlCodec.Encode(w);
        var back = (Welcome)ControlCodec.Decode(frame.AsSpan(4));
        Assert.Equal("blitz", back.Mode);
    }

    [Fact]
    public void Welcome_WithoutModeIsStandard()
    {
        var json = Encoding.UTF8.GetBytes("{\"type\":\"welcome\",\"playerId\":7000002,\"seatId\":\"ravi\",\"party\":\"gate\",\"unlocks\":\"everything\"}");
        Assert.Equal("standard", ((Welcome)ControlCodec.Decode(json)).Mode);
    }

    [Fact]
    public void Joiner_NoModifiersIsFine() => Assert.Null(JoinerModifiers.Refusal(Array.Empty<ModifierTraits>()));

    [Fact]
    public void Joiner_ModifiersThatOnlyActLaterAreFine() =>
        Assert.Null(JoinerModifiers.Refusal(new[]
        {
            new ModifierTraits("Flight", false, false), new ModifierTraits("Vintage", false, false),
            new ModifierTraits("DeadlyEvents", false, true),
        }));

    [Theory]
    [InlineData("Draft")]
    [InlineData("SealedDeck")]
    [InlineData("Insanity")]
    public void Joiner_DeckClearingModifierRefuses(string id)
    {
        var reason = JoinerModifiers.Refusal(new[] { new ModifierTraits("Flight", false, false), new ModifierTraits(id, true, false) });
        Assert.Contains(id, reason);
        Assert.Contains("starter deck", reason);
    }

    [Fact]
    public void Joiner_UnknownRunCreationModifierRefuses()
    {
        var reason = JoinerModifiers.Refusal(new[] { new ModifierTraits("SomethingNew", false, true) });
        Assert.Contains("SomethingNew", reason);
        Assert.Contains("does not know", reason);
    }
}
