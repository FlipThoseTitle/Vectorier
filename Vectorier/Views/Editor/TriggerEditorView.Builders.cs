using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;

namespace Vectorier.Views.Editor;

public partial class TriggerEditorView
{
    private static string[] Tr(params string[] items) => items.Select(Loc.T).ToArray();

    private static TextBlock Hint(string text) => new() { Text = Loc.T(text), Classes = { "hint" } };

    private static Control BoolEditor(bool value, Action<bool> set, string onText, string offText)
    {
        onText = Loc.T(onText); offText = Loc.T(offText);
        var box = new CheckBox { IsChecked = value, Content = value ? onText : offText };
        box.IsCheckedChanged += (_, _) =>
        {
            bool on = box.IsChecked == true;
            box.Content = on ? onText : offText;
            set(on);
        };
        return box;
    }

    /// <summary>
    /// Calls <paramref name="edited"/> when the user changes the text. TextChanged also fires when a new box first shows
    /// its text, which must not count as a change (the trigger would read as having unapplied changes).
    /// </summary>
    private static void OnEdited(TextBox box, Action<string> edited)
    {
        string last = box.Text ?? string.Empty;
        box.TextChanged += (_, _) =>
        {
            string text = box.Text ?? string.Empty;
            if (text == last) return;
            last = text;
            edited(text);
        };
    }

    private static TextBox Text(string value, Action<string> set)
    {
        var box = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        // TextChanged also fires when the box first shows its text: only real edits count.
        string last = value ?? string.Empty;
        box.TextChanged += (_, _) =>
        {
            string text = box.Text ?? string.Empty;
            if (text == last) return;
            last = text;
            set(text);
        };
        return box;
    }

    private Control RawBox(string value, Action<string> set)
    {
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 60,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 11,
        };
        string last = value ?? string.Empty;
        box.TextChanged += (_, _) =>
        {
            string text = box.Text ?? string.Empty;
            if (text == last) return;
            last = text;
            set(text);
        };
        return box;
    }

    /// <summary>Dropdown limited to the options; an unknown value from a file is shown as "(not found)" and kept.</summary>
    private static Control StrictCombo(string value, IList<string> options, Action<string> set)
    {
        var shown = options.ToList();
        int index = shown.IndexOf(value);
        if (index < 0 && !string.IsNullOrEmpty(value))
        {
            shown.Add(value + "  " + Loc.T("(not found)"));
            index = shown.Count - 1;
        }
        var combo = new ComboBox { ItemsSource = shown, SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Count)
                set(options[combo.SelectedIndex]);
        };
        return combo;
    }

    /// <summary>The level's models (Player, Hunter...) plus "Custom" with a name field for any other model.</summary>
    private Control ModelPicker(string value, Action<string> set, Func<bool> getCustom, Action<bool> setCustom)
    {
        var models = _vm!.ModelChoices;
        int matched = models.IndexOf(value);
        bool custom = getCustom() || (matched < 0 && !string.IsNullOrEmpty(value) && value != VTypes.NoModel);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(custom ? "Auto,*" : "*") };
        var labels = models.Concat(new[] { Loc.T("Custom") }).ToList();
        var combo = new ComboBox { ItemsSource = labels, SelectedIndex = custom ? labels.Count - 1 : Math.Max(0, matched), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 100 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            if (combo.SelectedIndex == labels.Count - 1) setCustom(true);
            else { setCustom(false); set(models[combo.SelectedIndex]); }
            Changed();
        };
        grid.Children.Add(combo);
        if (custom)
        {
            var box = Text(matched >= 0 ? string.Empty : value, set);
            box.Margin = new Thickness(4, 0, 0, 0);
            box.PlaceholderText = Loc.T("Model name");
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        return grid;
    }

    /// <summary>Presets plus "Custom" with a number field (AI, Zoom).</summary>
    private Control ChoiceWithCustom(string[] labels, string[] values, string value, Action<string> set, Func<bool> getCustom, Action<bool> setCustom, bool integer)
    {
        int matched = Array.FindIndex(values, x => integer ? x == value : VTypes.TryFloat(x, out var a) && VTypes.TryFloat(value, out var b) && Math.Abs(a - b) < 0.0001f);
        bool custom = getCustom() || matched < 0;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(custom ? "Auto,*" : "*") };
        var combo = new ComboBox { ItemsSource = Tr(labels), SelectedIndex = custom ? labels.Length - 1 : matched, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 100 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            if (combo.SelectedIndex == labels.Length - 1) setCustom(true);
            else { setCustom(false); set(values[combo.SelectedIndex]); }
            Changed();
        };
        grid.Children.Add(combo);
        if (custom)
        {
            NumericUpDown box;
            if (integer)
            {
                VTypes.TryInt(value, out int n);
                box = new NumericUpDown { Value = Math.Max(-1, n), Minimum = -1, Increment = 1, FormatString = "0", ShowButtonSpinner = false, Margin = new Thickness(4, 0, 0, 0) };
                box.ValueChanged += (_, _) => set(Math.Max(-1, (int)(box.Value ?? -1)).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                if (!VTypes.TryFloat(value, out float f) || f <= 0) f = 1;
                box = new NumericUpDown { Value = (decimal)f, Minimum = 0.01m, Increment = 0.05m, FormatString = "0.###", ShowButtonSpinner = false, Margin = new Thickness(4, 0, 0, 0) };
                box.ValueChanged += (_, _) => set(VTypes.Num(Math.Max(0.01f, (float)(box.Value ?? 1))));
            }
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        return grid;
    }

    /// <summary>
    /// Free text with a drop-down picker. Choices containing "/" become submenus; long lists can be grouped by
    /// first letter. <paramref name="rebuildOnType"/> refreshes the form after typing (template names, var names).
    /// </summary>
    /// <param name="itemLabel">Text shown for a choice in the menu (the choice itself is what is set).</param>
    /// <param name="boxText">Text shown in the box for a value; <paramref name="boxParse"/> turns typed text back into a value.</param>
    private Control TextWithPicker(string value, IList<string> choices, Action<string> set, bool rebuildOnType = false,
        bool groupByDot = false, string? emptyText = null, bool groupAlphabetically = false, bool rebuildOnPick = false,
        Func<string, string>? itemLabel = null, Func<string, string>? boxText = null, Func<string, string>? boxParse = null)
    {
        boxText ??= s => s;
        boxParse ??= s => s;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        string shownValue = boxText(value ?? string.Empty);
        var box = new TextBox { Text = shownValue };
        if (rebuildOnType)
            box.LostFocus += (_, _) => { if (box.Text != shownValue) set(boxParse(box.Text ?? string.Empty)); };
        else
        {
            string last = shownValue;
            box.TextChanged += (_, _) =>
            {
                string text = box.Text ?? string.Empty;
                if (text == last) return;
                last = text;
                set(boxParse(text));
            };
        }
        grid.Children.Add(box);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        var menu = new MenuFlyout();
        if (choices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = emptyText ?? Loc.T("Nothing to pick"), IsEnabled = false });
        }
        else
        {
            var groups = new Dictionary<string, MenuItem>();
            bool alphabet = groupAlphabetically && choices.Count > 30;
            foreach (var choice in choices)
            {
                if (choice == "-")
                {
                    // Separator, never first or doubled.
                    if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
                        menu.Items.Add(new Separator());
                    continue;
                }
                string label = choice;
                string? group = null;
                if (groupByDot && choice.IndexOf('.') is int dot && dot > 0) { group = choice[..dot]; label = choice[(dot + 1)..]; }
                else if (choice.IndexOf('/') is int slash && slash > 0) { group = choice[..slash]; label = choice[(slash + 1)..]; }
                else if (alphabet) group = char.IsLetter(choice[0]) ? char.ToUpperInvariant(choice[0]).ToString() : "#";

                var item = new MenuItem { Header = string.IsNullOrEmpty(label) ? Loc.T("(unnamed)") : itemLabel?.Invoke(label) ?? label };
                string picked = choice.StartsWith("Other Types/", StringComparison.Ordinal) ? choice["Other Types/".Length..] : choice;
                item.Click += (_, _) =>
                {
                    if (!rebuildOnPick && !groupByDot)
                        box.Text = boxText(picked);
                    set(picked);
                    if (rebuildOnType || rebuildOnPick || groupByDot) Changed();
                };
                if (group == null)
                {
                    menu.Items.Add(item);
                    continue;
                }
                if (!groups.TryGetValue(group, out var g))
                {
                    g = new MenuItem { Header = group };
                    groups[group] = g;
                    menu.Items.Add(g);
                }
                g.Items.Add(item);
            }
        }
        button.Flyout = menu;
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>The start-frame value of the action whose card is being built (null outside an animation action).</summary>
    private TValue? _animationFrame;

    /// <summary>
    /// Move name with the moves dropdown: picking a move (or the play button) opens the character preview; Apply
    /// writes the move and the trimmed start frame.
    /// </summary>
    private Control AnimationPicker(string value, Action<string> set, TValue? frame)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var box = new TextBox { Text = value ?? string.Empty };
        OnEdited(box, set);
        grid.Children.Add(box);

        async void Preview(string move)
        {
            if (string.IsNullOrWhiteSpace(move))
                return;
            int start = 0;
            if (frame is { mode: ValueMode.Literal } && VTypes.TryInt(frame.text, out int f))
                start = Math.Max(0, f);
            var result = await _vm!.PreviewMoveAsync(move, start);
            if (result == null)
                return;
            box.Text = move;
            set(move);
            if (frame != null)
            {
                frame.mode = ValueMode.Literal;
                frame.text = result.StartFrame.ToString(CultureInfo.InvariantCulture);
            }
            Changed();
        }

        var play = IconButton("Icon.Play", Loc.T("Preview on the character"), () => Preview(box.Text ?? string.Empty));
        play.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(play, 1);
        grid.Children.Add(play);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        ToolTip.SetTip(button, Loc.T("Pick a move"));
        var menu = new MenuFlyout();
        var choices = _vm!.Animations;
        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("No moves found in the project's moves.xml"), IsEnabled = false });
        var groups = new Dictionary<string, MenuItem>();
        foreach (var choice in choices)
        {
            var item = new MenuItem { Header = choice };
            string picked = choice;
            item.Click += (_, _) => Preview(picked);
            if (choices.Count <= 30)
            {
                menu.Items.Add(item);
                continue;
            }
            string group = char.IsLetter(choice[0]) ? char.ToUpperInvariant(choice[0]).ToString() : "#";
            if (!groups.TryGetValue(group, out var g))
            {
                g = new MenuItem { Header = group };
                groups[group] = g;
                menu.Items.Add(g);
            }
            g.Items.Add(item);
        }
        button.Flyout = menu;
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>Sound name with the sounds dropdown; each entry has a play button on its left.</summary>
    private Control SoundPicker(string value, Action<string> set)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox { Text = value ?? string.Empty };
        OnEdited(box, set);
        grid.Children.Add(box);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        var menu = new MenuFlyout();
        var choices = _vm!.Sounds;
        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.T("No sounds found in the project's sounds folder"), IsEnabled = false });
        var groups = new Dictionary<string, MenuItem>();
        foreach (var choice in choices)
        {
            string picked = choice;
            var playIcon = new Border
            {
                Background = Brushes.Transparent,
                Width = 18,
                Height = 18,
                Child = new PathIcon { Data = IconGeometry("Icon.Play"), Width = 11, Height = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)) },
            };
            ToolTip.SetTip(playIcon, Loc.T("Play"));
            // The play button plays without picking (the menu stays open).
            playIcon.AddHandler(InputElement.PointerPressedEvent, async (_, e) =>
            {
                e.Handled = true;
                if (await _vm.FindSoundAsync(picked) is { } path)
                {
                    AppServices.Platform.StopSound();
                    await AppServices.Platform.PlaySoundAsync(AppServices.FileSystem, path, 0.8);
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            playIcon.AddHandler(InputElement.PointerReleasedEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            var item = new MenuItem { Header = choice, Icon = playIcon };
            item.Click += (_, _) =>
            {
                AppServices.Platform.StopSound();
                box.Text = picked;
                set(picked);
            };
            if (choices.Count <= 30)
            {
                menu.Items.Add(item);
                continue;
            }
            string group = char.IsLetter(choice[0]) ? char.ToUpperInvariant(choice[0]).ToString() : "#";
            if (!groups.TryGetValue(group, out var g))
            {
                g = new MenuItem { Header = group };
                groups[group] = g;
                menu.Items.Add(g);
            }
            g.Items.Add(item);
        }
        menu.Closed += (_, _) => AppServices.Platform.StopSound();
        button.Flyout = menu;
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static StreamGeometry? IconGeometry(string key) =>
        Application.Current != null && Application.Current.TryGetResource(key, null, out var res) ? res as StreamGeometry : null;

    private static Button IconButton(string icon, string tip, Action click)
    {
        var b = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Content = new PathIcon { Data = IconGeometry(icon), Width = 12, Height = 12 } };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Grid LabeledRow(string label, Control editor)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*"), Margin = new Thickness(0, 2) };
        grid.Children.Add(new TextBlock { Text = Loc.T(label) });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    /// <summary>A section (When / Only If / Do). Returns the body panel; its parent Border is the section.</summary>
    private static StackPanel Block(string title, string description)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = Loc.T(title), Classes = { "block-title" } });
        body.Children.Add(new TextBlock { Text = Loc.T(description), Classes = { "hint" } });
        _ = new Border { Classes = { "block" }, Child = body };
        return body;
    }

    private static StackPanel Card(string title, Control buttons, Control? extra = null)
    {
        var card = new StackPanel { Spacing = 3 };
        var header = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = Loc.T(title), Classes = { "card-title" } });
        if (extra != null) titleRow.Children.Add(extra);
        header.Children.Add(titleRow);
        card.Children.Add(header);
        return card;
    }

    private static Control WrapCard(StackPanel card) => new Border { Classes = { "card" }, Child = card };

    /// <summary>Up / down / remove buttons for an entry in a list.</summary>
    private Control CardButtons<T>(List<T> list, int index)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        var up = IconButton("Icon.ArrowUp", Loc.T("Move up"), () => { var t = list[index]; list.RemoveAt(index); list.Insert(index - 1, t); Changed(); });
        up.IsEnabled = index > 0;
        var down = IconButton("Icon.ArrowDown", Loc.T("Move down"), () => { var t = list[index]; list.RemoveAt(index); list.Insert(index + 1, t); Changed(); });
        down.IsEnabled = index < list.Count - 1;
        var remove = IconButton("Icon.Close", Loc.T("Remove"), () => { list.RemoveAt(index); Changed(); });
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);
        return row;
    }

    private static Control AddButton(string text, MenuFlyout menu)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Flyout = menu,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new PathIcon { Data = IconGeometry("Icon.Plus"), Width = 12, Height = 12 }, new TextBlock { Text = Loc.T(text) } },
            },
        };
        return button;
    }
}
