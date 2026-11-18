using SpireParty.Core;
using Xunit;

namespace SpireParty.Tests;

public class TurnTimerTests
{
    [Fact]
    public void CountsDown_WarnsAtTen_ExpiresAtZero()
    {
        var t = new TurnTimer();
        Assert.False(t.IsRunning);
        t.Start(100, 30);
        Assert.Equal(30, t.Remaining(100));
        Assert.False(t.IsWarning(119.9));
        Assert.True(t.IsWarning(120));
        Assert.False(t.IsExpired(129.9));
        Assert.True(t.IsExpired(130));
        Assert.Equal(0, t.Remaining(140));
    }

    [Fact]
    public void Pause_FreezesTheTime_ResumeContinues()
    {
        var t = new TurnTimer();
        t.Start(0, 30);
        t.Pause(10);
        Assert.True(t.IsPaused);
        Assert.Equal(20, t.Remaining(500));
        Assert.False(t.IsExpired(500));
        t.Resume(500);
        Assert.False(t.IsExpired(519.9));
        Assert.True(t.IsExpired(520));
    }

    [Fact]
    public void StartPaused_HoldsTheFullTime()
    {
        var t = new TurnTimer();
        t.Start(0, 45, paused: true);
        Assert.Equal(45, t.Remaining(1000));
        t.Resume(1000);
        Assert.True(t.IsExpired(1045));
    }

    [Fact]
    public void Stop_ClearsEverything_AndPauseResumeOfAStoppedTimerDoNothing()
    {
        var t = new TurnTimer();
        t.Start(0, 30);
        t.Stop();
        t.Pause(5);
        t.Resume(6);
        Assert.False(t.IsRunning);
        Assert.False(t.IsExpired(100));
        Assert.Equal(0, t.Remaining(100));
    }

    [Theory]
    [InlineData(90, "1:30")]
    [InlineData(29.2, "0:30")]
    [InlineData(0.1, "0:01")]
    [InlineData(0, "0:00")]
    public void Format_RoundsUp(double seconds, string text) => Assert.Equal(text, TurnTimer.Format(seconds));
}
