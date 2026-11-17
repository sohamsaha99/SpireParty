using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace SpireParty.UI;

/// <summary>
/// Messages use the game's own error popup (plain text). Forms are a small panel built from
/// stock Godot controls, added to the scene root above the menus. Main thread only.
/// </summary>
public static class Popups
{
    public static void Message(string title, string body)
    {
        try
        {
            var popup = NErrorPopup.Create(title, body, showReportBugButton: false);
            if (popup != null && NModalContainer.Instance != null)
            {
                NModalContainer.Instance.Add(popup);
                return;
            }
        }
        catch (Exception e)
        {
            Log.Warn($"{ModEntry.LogTag} game popup unavailable ({e.Message}); using a plain one");
        }
        var form = new Form(title);
        form.AddText(body);
        form.AddButtons(("OK", form.Close));
        form.Show();
    }
}

/// <summary>A simple modal form: title, text, text fields and buttons, centered over a dim backdrop.</summary>
public sealed class Form
{
    readonly Control _root;
    readonly VBoxContainer _box;
    readonly Label _status;

    public Form(string title)
    {
        _root = new Control { Name = "SpirePartyForm", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.ZIndex = 100;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(620, 0) };
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        panel.AddChild(margin);

        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_box);

        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 30);
        _box.AddChild(heading);

        _status = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.45f));
    }

    public void AddText(string text, int fontSize = 20)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _box.AddChild(label);
    }

    /// <summary>A text the player can select and copy (e.g. a token).</summary>
    public void AddCopyableText(string text)
    {
        var field = new LineEdit { Text = text, Editable = false, SelectAllOnFocus = true };
        field.AddThemeFontSizeOverride("font_size", 26);
        _box.AddChild(field);
    }

    public LineEdit AddField(string label, string value = "", string placeholder = "", bool secret = false)
    {
        AddText(label, 18);
        var field = new LineEdit { Text = value, PlaceholderText = placeholder, Secret = secret, CustomMinimumSize = new Vector2(0, 40) };
        field.AddThemeFontSizeOverride("font_size", 22);
        _box.AddChild(field);
        return field;
    }

    public void AddButtons(params (string Text, Action OnPressed)[] buttons)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var (text, onPressed) in buttons)
        {
            var button = new Button { Text = text, CustomMinimumSize = new Vector2(140, 44) };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.Pressed += onPressed;
            row.AddChild(button);
        }
        _box.AddChild(row);
    }

    /// <summary>A vertical list of buttons (e.g. existing parties, recent hosts).</summary>
    public void AddChoiceList(IEnumerable<(string Text, Action OnPressed)> choices)
    {
        foreach (var (text, onPressed) in choices)
        {
            var button = new Button { Text = text, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40) };
            button.AddThemeFontSizeOverride("font_size", 20);
            button.Pressed += onPressed;
            _box.AddChild(button);
        }
    }

    public void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.Visible = !string.IsNullOrEmpty(text);
    }

    public void Show()
    {
        _box.AddChild(_status);
        if (Engine.GetMainLoop() is SceneTree tree) tree.Root.AddChild(_root);
    }

    public void Close()
    {
        if (GodotObject.IsInstanceValid(_root)) _root.QueueFree();
    }
}
