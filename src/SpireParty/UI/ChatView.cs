using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireParty.Core;
using SpireParty.Net;

namespace SpireParty.UI;

/// <summary>
/// Party chat panel, top middle under the AFK banners (user request 2026-11-19). Follows
/// <see cref="ChatPanel"/>: hidden by default; a message shows the history for 10 seconds; T opens the
/// history with an input box (GameAdapters/ChatInputAdapter catches T before the game's hotkeys), Enter
/// sends, Esc, Tab or Enter on an empty line closes, T while the history is only showing hides it.
/// Built from plain Godot controls on its own CanvasLayer, like <see cref="AfkView"/>; nothing is stored.
/// </summary>
public static class ChatView
{
    const float Width = 760f;
    const float MaxHistoryHeight = 230f;
    const int FontSize = 20;
    static readonly Color NameColor = new(1f, 0.85f, 0.3f);

    static readonly ChatPanel Panel = new();
    static CanvasLayer? _layer;
    static PanelContainer? _box;
    static ScrollContainer? _scroll;
    static VBoxContainer? _lines;
    static LineEdit? _input;
    static long _shownLines;
    static int _scrollFrames;
    static ChatPanelMode _applied = ChatPanelMode.Hidden;
    static bool _started;

    /// <summary>True while the player is typing in the chat box (the input adapter lets keys through).</summary>
    public static bool IsTyping => Panel.Mode == ChatPanelMode.Open;

    public static ChatPanelMode Mode => Panel.Mode;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        PartyChat.Received += OnReceived;
        MainThread.EveryFrame += Tick;
    }

    /// <summary>The chat key (T) reached the game unhandled. Returns true when the chat used it.</summary>
    public static bool OnChatKey()
    {
        if (!SpirePartySession.IsActive || IsTyping) return false;
        Panel.OnChatKey();
        Apply();
        return true;
    }

    static void OnReceived(ChatLine line)
    {
        Panel.OnMessage(DateTime.UtcNow);
        Apply();
    }

    static void Tick()
    {
        try
        {
            if (!SpirePartySession.IsActive)
            {
                if (Panel.Mode != ChatPanelMode.Hidden || _shownLines > 0)
                {
                    Panel.Reset();
                    ClearLines();
                    Apply();
                }
                return;
            }
            Panel.Tick(DateTime.UtcNow);
            if (Panel.Mode != _applied) Apply();
            if (_scrollFrames > 0 && _scroll != null && GodotObject.IsInstanceValid(_scroll))
            {
                _scrollFrames--;
                FitHeight();
                _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
            }
        }
        catch (Exception e)
        {
            Log.Error($"{ModEntry.LogTag} chat view stopped: {e}");
            MainThread.EveryFrame -= Tick;
        }
    }

    /// <summary>Shows the panel as <see cref="Panel"/> says.</summary>
    static void Apply()
    {
        var mode = Panel.Mode;
        _applied = mode;
        if (mode == ChatPanelMode.Hidden)
        {
            if (_layer != null && GodotObject.IsInstanceValid(_layer))
            {
                if (_input!.HasFocus()) _input.ReleaseFocus();
                _layer.Visible = false;
            }
            return;
        }
        if (!Build()) return;
        SyncLines();
        _layer!.Visible = true;
        bool open = mode == ChatPanelMode.Open;
        _scroll!.Visible = _shownLines > 0;
        _input!.Visible = open;
        // While only showing, the panel never takes the mouse; while typing it does (scroll, click the box).
        _box!.MouseFilter = open ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _scroll.MouseFilter = open ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        if (open && !_input.HasFocus()) _input.GrabFocus();
        _scrollFrames = 3;
    }

    static bool Build()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return true;
        if (Engine.GetMainLoop() is not SceneTree tree) return false;

        _layer = new CanvasLayer { Name = "SpirePartyChat", Layer = 91 };
        var anchor = new Control
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -Width / 2, OffsetRight = Width / 2, OffsetTop = 170,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.62f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        style.SetCornerRadiusAll(8);
        _box.AddThemeStyleboxOverride("panel", style);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        _lines.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_lines);

        _input = new LineEdit
        {
            PlaceholderText = "Message the party: Enter to send, Esc to close",
            MaxLength = ChatRules.MaxLength,
            ContextMenuEnabled = false,
            CustomMinimumSize = new Vector2(0, 40),
        };
        _input.AddThemeFontSizeOverride("font_size", FontSize);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputKey;
        _input.FocusExited += OnFocusLost;

        column.AddChild(_scroll);
        column.AddChild(_input);
        _box.AddChild(column);
        anchor.AddChild(_box);
        _layer.AddChild(anchor);
        tree.Root.AddChild(_layer);
        _shownLines = 0;
        return true;
    }

    /// <summary>Adds the lines that arrived since the last sync and drops labels the log no longer holds.</summary>
    static void SyncLines()
    {
        if (_lines == null) return;
        var history = PartyChat.History;
        if (history.Added < _shownLines) ClearLines();   // a new session started
        long fresh = Math.Min(history.Added - _shownLines, history.Count);
        long skip = history.Count - fresh;
        long i = 0;
        foreach (var line in history.Lines)
            if (i++ >= skip) _lines.AddChild(MakeLine(line));
        _shownLines = history.Added;
        // QueueFree only takes effect at the end of the frame, so count the labels still alive.
        int alive = 0;
        foreach (var child in _lines.GetChildren()) if (!child.IsQueuedForDeletion()) alive++;
        foreach (var child in _lines.GetChildren())
        {
            if (alive <= history.Count) break;
            if (child.IsQueuedForDeletion()) continue;
            child.QueueFree();
            alive--;
        }
    }

    static RichTextLabel MakeLine(ChatLine line)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = $"[color=#{NameColor.ToHtml(false)}]{Escape(line.Name)}:[/color] {Escape(line.Text)}",
        };
        label.AddThemeFontSizeOverride("normal_font_size", FontSize);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeColorOverride("default_color", Colors.White);
        return label;
    }

    /// <summary>Player text is shown as typed: "[" starts BBCode, so it is written as the [lb] tag.</summary>
    static string Escape(string text) => text.Replace("[", "[lb]");

    static void FitHeight()
    {
        if (_scroll == null || _lines == null) return;
        float content = _lines.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(content, MaxHistoryHeight));
    }

    static void ClearLines()
    {
        if (_lines != null && GodotObject.IsInstanceValid(_lines))
            foreach (var child in _lines.GetChildren()) child.QueueFree();
        _shownLines = 0;
    }

    static void OnSubmitted(string text)
    {
        if (_input == null) return;
        _input.Clear();
        // Close first: the line coming back (from the host, or at once on the host) starts the 10-second peek.
        Panel.OnClose();
        Apply();
        if (ChatRules.Clean(text) != null && !PartyChat.Send(text))
            Log.Info($"{ModEntry.LogTag} chat: not sent (no party connection)");
    }

    static void OnInputKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || _input == null) return;
        if (key.Keycode is Key.Escape or Key.Tab)
        {
            _input.AcceptEvent();
            Close();
        }
    }

    static void OnFocusLost()
    {
        if (Panel.Mode == ChatPanelMode.Open) Close();
    }

    static void Close()
    {
        _input?.Clear();
        Panel.OnClose();
        Apply();
    }
}
