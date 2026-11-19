using SpireParty.Core;

namespace SpireParty.Tests;

public class ChatRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \t\n ", null)]
    [InlineData("hi", "hi")]
    [InlineData("  hi  there  ", "hi there")]
    [InlineData("line one\nline two\r\n", "line one line two")]
    [InlineData("a\u0000b\u0007c", "a b c")]
    [InlineData("[b]bold[/b]", "[b]bold[/b]")]
    public void Clean(string? input, string? expected) => Assert.Equal(expected, ChatRules.Clean(input));

    [Fact]
    public void Clean_CutsAtMaxLength()
    {
        string clean = ChatRules.Clean(new string('x', 500))!;
        Assert.Equal(ChatRules.MaxLength, clean.Length);
    }

    [Fact]
    public void Clean_NeverSplitsASurrogatePair()
    {
        string text = new string('x', ChatRules.MaxLength - 1) + "😀";
        string clean = ChatRules.Clean(text)!;
        Assert.Equal(ChatRules.MaxLength - 1, clean.Length);
        Assert.False(char.IsHighSurrogate(clean[^1]));
    }

    [Fact]
    public void Clean_NoTrailingSpaceAtTheLimit()
    {
        string text = new string('x', ChatRules.MaxLength - 1) + " yy";
        string clean = ChatRules.Clean(text)!;
        Assert.False(clean.EndsWith(' '));
        Assert.True(clean.Length <= ChatRules.MaxLength);
    }

    [Theory]
    [InlineData("Ravi", "ravi", "Ravi")]
    [InlineData("", "ravi", "ravi")]
    [InlineData(null, "ravi", "ravi")]
    [InlineData(null, "", "?")]
    public void Name(string? name, string fallback, string expected) => Assert.Equal(expected, ChatRules.Name(name, fallback));

    [Fact]
    public void Name_IsShort() => Assert.Equal(ChatRules.MaxNameLength, ChatRules.Name(new string('n', 80), "x").Length);
}

public class ChatLogTests
{
    static ChatLine L(int i) => new(1, "a", $"m{i}");

    [Fact]
    public void KeepsTheNewestLines_CountsEveryLine()
    {
        var log = new ChatLog(capacity: 3);
        for (int i = 1; i <= 5; i++) log.Add(L(i));
        Assert.Equal(new[] { "m3", "m4", "m5" }, log.Lines.Select(l => l.Text));
        Assert.Equal(3, log.Count);
        Assert.Equal(5, log.Added);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var log = new ChatLog();
        log.Add(L(1));
        log.Clear();
        Assert.Empty(log.Lines);
        Assert.Equal(0, log.Added);
    }
}

public class ChatRateLimiterTests
{
    static readonly DateTime T0 = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BurstThenDrop_ThenRefills()
    {
        var limiter = new ChatRateLimiter(burst: 3, refill: TimeSpan.FromSeconds(1));
        Assert.True(limiter.Allow(1, T0));
        Assert.True(limiter.Allow(1, T0));
        Assert.True(limiter.Allow(1, T0));
        Assert.False(limiter.Allow(1, T0));
        Assert.True(limiter.Allow(2, T0));                       // senders are separate
        Assert.True(limiter.Allow(1, T0.AddSeconds(1)));         // one refilled
        Assert.False(limiter.Allow(1, T0.AddSeconds(1)));
        for (int i = 0; i < 3; i++) Assert.True(limiter.Allow(1, T0.AddSeconds(60)));   // capped at the burst
        Assert.False(limiter.Allow(1, T0.AddSeconds(60)));
    }
}

public class ChatPanelTests
{
    static readonly DateTime T0 = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HiddenByDefault() => Assert.Equal(ChatPanelMode.Hidden, new ChatPanel().Mode);

    [Fact]
    public void Message_ShowsForTenSeconds()
    {
        var p = new ChatPanel();
        p.OnMessage(T0);
        Assert.Equal(ChatPanelMode.Peek, p.Mode);
        p.Tick(T0.AddSeconds(9.9));
        Assert.Equal(ChatPanelMode.Peek, p.Mode);
        p.Tick(T0.AddSeconds(10));
        Assert.Equal(ChatPanelMode.Hidden, p.Mode);
    }

    [Fact]
    public void NewMessage_RestartsTheTenSeconds()
    {
        var p = new ChatPanel();
        p.OnMessage(T0);
        p.OnMessage(T0.AddSeconds(8));
        p.Tick(T0.AddSeconds(12));
        Assert.Equal(ChatPanelMode.Peek, p.Mode);
        p.Tick(T0.AddSeconds(18));
        Assert.Equal(ChatPanelMode.Hidden, p.Mode);
    }

    [Fact]
    public void ChatKey_OpensWhenHidden_HidesAPeekAtOnce()
    {
        var p = new ChatPanel();
        p.OnChatKey();
        Assert.Equal(ChatPanelMode.Open, p.Mode);

        p = new ChatPanel();
        p.OnMessage(T0);
        p.OnChatKey();
        Assert.Equal(ChatPanelMode.Hidden, p.Mode);
    }

    [Fact]
    public void Open_StaysOpenWhileMessagesArriveAndTimePasses()
    {
        var p = new ChatPanel();
        p.OnChatKey();
        p.OnMessage(T0);
        p.Tick(T0.AddMinutes(5));
        Assert.Equal(ChatPanelMode.Open, p.Mode);
    }

    [Fact]
    public void Send_Closes_ThenTheReturningLinePeeks()
    {
        var p = new ChatPanel();
        p.OnChatKey();
        p.OnClose();                 // Enter: the view closes before sending
        Assert.Equal(ChatPanelMode.Hidden, p.Mode);
        p.OnMessage(T0);             // the host relays the line back
        Assert.Equal(ChatPanelMode.Peek, p.Mode);
    }
}
