using System.Linq;
using SpireParty.Core;
using Xunit;

namespace SpireParty.Tests;

public class RosterSwitchTests
{
    [Fact]
    public void ParsesSeveralEdits()
    {
        var ops = RosterSwitch.Parse("b5,bench,c2; b5,add,c5,silent ;b5,backup,latest", out var error);
        Assert.Null(error);
        Assert.Equal(new[] { "bench", "add", "backup" }, ops.Select(o => o.Op));
        Assert.Equal("silent", ops[1].Character);
        Assert.Equal("latest", ops[2].Arg);
    }

    [Theory]
    [InlineData("b5,bench")]
    [InlineData("b5,kick,c2")]
    [InlineData("b5,add,c5")]
    [InlineData(";")]
    public void RefusesBadInput(string text)
    {
        Assert.Empty(RosterSwitch.Parse(text, out var error));
        Assert.NotNull(error);
    }
}
