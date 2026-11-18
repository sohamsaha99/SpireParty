using SpireParty.Core;
using SpireParty.GameAdapters;
using Xunit;

namespace SpireParty.Tests;

[Collection("GameIL")]
public class WireFormatTests
{
    [Fact]
    public void PartyWidths_CarryEightPlayers_VanillaCarriesFour()
    {
        Assert.Equal(8, WireWidths.MaxPlayers);
        Assert.True(WireWidths.MaxPlayers - 1 < 1 << WireWidths.PartySlotBits);   // slots 0..7
        Assert.True(WireWidths.MaxPlayers < 1 << WireWidths.PartyListBits);        // a list of 8
        Assert.True(4 - 1 < 1 << WireWidths.VanillaSlotBits);
        Assert.True(4 >= 1 << WireWidths.VanillaSlotBits);                          // vanilla really cannot hold slot 4
        Assert.True(8 >= 1 << WireWidths.VanillaListBits);                          // nor a list of 8
        Assert.Equal(RosterEdit.VanillaCap, 1 << WireWidths.VanillaSlotBits);
    }

    [Fact]
    public void OutsideASession_WidthsAreVanilla()
    {
        Assert.False(SpirePartySession.IsActive);
        Assert.Equal(WireWidths.VanillaSlotBits, WireWidths.SlotBits());
        Assert.Equal(WireWidths.VanillaListBits, WireWidths.ListBits());
    }

    /// <summary>Runs the adapter's matcher on the real game IL (sts2.dll of the installed build).</summary>
    [Fact]
    public void SelfCheck_FindsExactlyOneWidthPerMethodInThisGameBuild()
    {
        Assert.Null(new WireFormatAdapter().SelfCheck());
    }
}
