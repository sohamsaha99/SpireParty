using SpireParty.Core;
using SpireParty.Net;
using Xunit;

namespace SpireParty.Tests;

public class AfkTests
{
    [Fact]
    public void TwoTimeoutsInARow_MarkAfk()
    {
        var a = new AfkState();
        Assert.False(a.OnTimedOut(2));
        Assert.False(a.IsAfk);
        Assert.True(a.OnTimedOut(2));
        Assert.True(a.IsAfk);
        Assert.False(a.OnTimedOut(2));   // already AFK: no change
    }

    [Fact]
    public void EndingATurnYourself_ResetsTheStrikes()
    {
        var a = new AfkState();
        a.OnTimedOut(2);
        a.OnEndedByPlayer();
        Assert.Equal(0, a.Strikes);
        Assert.False(a.OnTimedOut(2));
        Assert.False(a.IsAfk);
    }

    [Fact]
    public void Input_ClearsTheMarkAndStrikes_ButNotStrikesBeforeTheMark()
    {
        var a = new AfkState();
        a.OnTimedOut(3);
        Assert.False(a.OnInput());       // not AFK: input alone does not reset strikes
        Assert.Equal(1, a.Strikes);
        a.OnTimedOut(3);
        a.OnTimedOut(3);
        Assert.True(a.IsAfk);
        Assert.True(a.OnInput());
        Assert.False(a.IsAfk);
        Assert.Equal(0, a.Strikes);
    }

    [Fact]
    public void OneStrikeLimit_MarksAtOnce()
    {
        var a = new AfkState();
        Assert.True(a.OnTimedOut(1));
    }

    [Fact]
    public void Roster_SetReportsChanges()
    {
        AfkRoster.Clear();
        Assert.True(AfkRoster.Set(7000002, true));
        Assert.False(AfkRoster.Set(7000002, true));
        Assert.True(AfkRoster.Set(7000002, false));
        Assert.Empty(AfkRoster.PlayerIds);
        AfkRoster.Clear();
    }

    [Fact]
    public void AfkMessages_RoundTrip()
    {
        Assert.True(Assert.IsType<AfkReport>(ControlCodec.Decode(ControlCodec.Encode(new AfkReport(true)).AsSpan(4))).IsAfk);
        var list = Assert.IsType<AfkList>(ControlCodec.Decode(ControlCodec.Encode(new AfkList(new ulong[] { 1, 7000003 })).AsSpan(4)));
        Assert.Equal(new ulong[] { 1, 7000003 }, list.PlayerIds);
        Assert.Empty(Assert.IsType<AfkList>(ControlCodec.Decode(ControlCodec.Encode(new AfkList(new ulong[0])).AsSpan(4))).PlayerIds);
    }
}
