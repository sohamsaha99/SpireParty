using System;
using System.Collections.Generic;
using System.Text;

namespace SpireParty.Core;

/// <summary>One chat line as everyone sees it. Kept in memory only; never written to disk.</summary>
public sealed record ChatLine(ulong PlayerId, string Name, string Text);

/// <summary>Rules every chat message passes through, on the sender and again on the host.</summary>
public static class ChatRules
{
    public const int MaxLength = 200;
    public const int MaxNameLength = 32;

    /// <summary>Control characters and line breaks become spaces, runs of spaces collapse, the result is
    /// trimmed and cut to <see cref="MaxLength"/>. Null when nothing is left.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var sb = new StringBuilder(Math.Min(text.Length, MaxLength));
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space)
            {
                if (sb.Length + 1 >= MaxLength) break;
                sb.Append(' ');
                space = false;
            }
            if (sb.Length >= MaxLength) break;
            // Never split a surrogate pair at the length limit.
            if (char.IsHighSurrogate(c) && sb.Length + 2 > MaxLength) break;
            sb.Append(c);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>A display name for a chat line: cleaned, short, never empty.</summary>
    public static string Name(string? name, string fallback)
    {
        string? n = Clean(name) ?? Clean(fallback);
        if (n == null) return "?";
        return n.Length > MaxNameLength ? n.Substring(0, MaxNameLength) : n;
    }
}

/// <summary>The last few lines of this session's chat, oldest first. Memory only.</summary>
public sealed class ChatLog
{
    readonly int _capacity;
    readonly LinkedList<ChatLine> _lines = new();

    public ChatLog(int capacity = 100) => _capacity = Math.Max(1, capacity);

    public int Count => _lines.Count;
    /// <summary>Lines ever added since the last <see cref="Clear"/> (grows past the capacity).</summary>
    public long Added { get; private set; }
    public IEnumerable<ChatLine> Lines => _lines;

    public void Add(ChatLine line)
    {
        _lines.AddLast(line);
        Added++;
        while (_lines.Count > _capacity) _lines.RemoveFirst();
    }

    public void Clear()
    {
        _lines.Clear();
        Added = 0;
    }
}

/// <summary>
/// Host-side flood guard: each sender may send <c>burst</c> messages at once, refilled at one message per
/// <c>refill</c>. Extra messages are dropped.
/// </summary>
public sealed class ChatRateLimiter
{
    readonly int _burst;
    readonly TimeSpan _refill;
    readonly Dictionary<ulong, (double Tokens, DateTime At)> _buckets = new();

    public ChatRateLimiter(int burst = 5, TimeSpan? refill = null)
    {
        _burst = Math.Max(1, burst);
        _refill = refill ?? TimeSpan.FromSeconds(1);
    }

    public bool Allow(ulong sender, DateTime now)
    {
        double tokens = _burst;
        if (_buckets.TryGetValue(sender, out var b))
            tokens = Math.Min(_burst, b.Tokens + Math.Max(0, (now - b.At).TotalSeconds) / _refill.TotalSeconds);
        bool ok = tokens >= 1;
        if (ok) tokens -= 1;
        _buckets[sender] = (tokens, now);
        return ok;
    }

    public void Clear() => _buckets.Clear();
}

public enum ChatPanelMode
{
    /// <summary>Nothing on screen.</summary>
    Hidden,
    /// <summary>The history is shown for a while after a message arrived; no input box.</summary>
    Peek,
    /// <summary>The history and the input box are shown; the player is typing.</summary>
    Open,
}

/// <summary>
/// When the chat panel shows (pure logic; the view in UI/ChatView.cs follows it).
/// Hidden + chat key: open. Peek + chat key: hide at once. A message arriving shows the history for
/// <see cref="PeekTime"/> (or keeps it open). Closing the input (Esc, Tab, Enter on an empty line,
/// clicking away) hides; sending hides, and the message coming back starts a new peek.
/// </summary>
public sealed class ChatPanel
{
    public static readonly TimeSpan PeekTime = TimeSpan.FromSeconds(10);

    DateTime _peekUntil;

    public ChatPanelMode Mode { get; private set; } = ChatPanelMode.Hidden;

    public void OnChatKey() =>
        Mode = Mode == ChatPanelMode.Hidden ? ChatPanelMode.Open : ChatPanelMode.Hidden;

    public void OnMessage(DateTime now)
    {
        if (Mode == ChatPanelMode.Open) return;
        Mode = ChatPanelMode.Peek;
        _peekUntil = now + PeekTime;
    }

    /// <summary>The input was closed without sending, or a message was sent.</summary>
    public void OnClose() => Mode = ChatPanelMode.Hidden;

    public void Tick(DateTime now)
    {
        if (Mode == ChatPanelMode.Peek && now >= _peekUntil) Mode = ChatPanelMode.Hidden;
    }

    public void Reset() => Mode = ChatPanelMode.Hidden;
}
