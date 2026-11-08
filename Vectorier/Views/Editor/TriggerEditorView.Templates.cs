using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;

namespace Vectorier.Views.Editor;

public partial class TriggerEditorView
{
    /// <summary>
    /// Every template of TriggerTemplates.xml: open one to edit it, reorder them, and mark which are a Library
    /// (building blocks without &lt;Init&gt;).
    /// </summary>
    private void BuildTemplateList()
    {
        var panel = TemplateListPanel;
        panel.Children.Clear();
        var file = _vm!.TemplateFile;
        if (file == null) return;
        panel.Children.Add(new TextBlock { Text = Loc.T("Trigger Templates"), Classes = { "block-title" } });
        panel.Children.Add(Hint("Click a template to edit it. Tick Library for a template that only holds loops for other templates to use."));
        var templates = file.Templates;
        if (templates.Count == 0)
            panel.Children.Add(Hint("TriggerTemplates.xml has no templates."));

        // The header and the rows share their column widths (the buttons' columns too), so each title sits over
        // its column.
        Grid.SetIsSharedSizeScope(panel, true);
        static Grid Columns() => new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "TplUp" },
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "TplDown" },
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "TplRemove" },
            },
        };
        var header = Columns();
        header.Children.Add(new TextBlock { Text = Loc.T("Template"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 0, 0, 0) });
        var libHead = new TextBlock { Text = Loc.T("Library"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(libHead, 1);
        header.Children.Add(libHead);
        if (templates.Count > 0)
            panel.Children.Add(new Border
            {
                Classes = { "card" },
                Padding = new Thickness(2),
                Margin = new Thickness(0, 8, 0, 0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Child = header,
            });

        for (int i = 0; i < templates.Count; i++)
        {
            var t = templates[i];
            string name = TriggerCatalog.FriendlyText(TriggerTemplateFile.NameOf(t));
            int loops = t.Elements("Loop").Count() + t.Elements("Template").Count();
            var row = Columns();

            var open = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Classes = { "tool" } };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(new PathIcon { Data = IconGeometry("Icon.Trigger"), Width = 12, Height = 12 });
            label.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? Loc.T("(unnamed)") : name, FontWeight = FontWeight.SemiBold });
            label.Children.Add(new TextBlock { Text = Loc.F("{0} loop(s)", loops), Classes = { "hint" }, VerticalAlignment = VerticalAlignment.Center });
            open.Content = label;
            ToolTip.SetTip(open, Loc.T("Edit this template"));
            open.Click += (_, _) => _vm!.OpenTemplate(t);
            row.Children.Add(open);

            var library = new CheckBox { IsChecked = TriggerTemplateFile.IsLibrary(t), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(library, Loc.T("Library: only holds loops for other templates to use"));
            library.IsCheckedChanged += (_, _) => _vm!.SetTemplateLibrary(t, library.IsChecked == true);
            Grid.SetColumn(library, 1);
            row.Children.Add(library);

            var up = IconButton("Icon.ArrowUp", Loc.T("Move up"), () => _vm!.MoveTemplate(t, -1));
            up.IsEnabled = i > 0;
            Grid.SetColumn(up, 2);
            row.Children.Add(up);
            var down = IconButton("Icon.ArrowDown", Loc.T("Move down"), () => _vm!.MoveTemplate(t, 1));
            down.IsEnabled = i < templates.Count - 1;
            Grid.SetColumn(down, 3);
            row.Children.Add(down);
            var remove = IconButton("Icon.Delete", Loc.T("Remove template"), () => _ = _vm!.RemoveTemplateAsync(t));
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);

            panel.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(2), Child = row });
        }
        panel.Children.Add(AddButtonSimple("Add Template", () => _ = _vm!.AddTemplateAsync()));
    }

    // ---------------- <Using> (template loops) ----------------

    /// <summary>
    /// The loop's &lt;Using&gt;: the variables it reads, with their type and default value (what the Variables panel's
    /// New Variable does for a trigger). ComplexName marks a variable whose name gets the block's Prefix.
    /// </summary>
    private Control BuildUsing(TLoop L)
    {
        var body = Block("<Using>", "Variables used in this Loop");
        if (L.usings.Count == 0)
            body.Children.Add(Hint("None."));
        else
        {
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,100,Auto,Auto"), Margin = new Thickness(0, 2) };
            void H(string text, int col)
            {
                var tb = new TextBlock { Text = Loc.T(text), Classes = { "hint" } };
                Grid.SetColumn(tb, col);
                head.Children.Add(tb);
            }
            H("Name", 0); H("Default value", 1); H("Type", 2); H("Complex Name", 3);
            body.Children.Add(head);
        }
        foreach (var u in L.usings)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,100,Auto,Auto"), Margin = new Thickness(0, 1) };
            var nameBox = new TextBox { Text = u.name };
            nameBox.LostFocus += (_, _) =>
            {
                if (nameBox.Text != u.name) _vm!.RenameUsing(L, u, nameBox.Text ?? string.Empty);
            };
            row.Children.Add(nameBox);

            var value = Literal(u.VType, u.def, null, () => false, _ => { }, s => { u.def = s; Touch(); });
            value.Margin = new Thickness(4, 0);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            // The editor's types; a type the editor doesn't know is listed as written (and kept).
            var labels = VTypes.VariableTypeLabels.ToList();
            bool known = Enum.TryParse<VType>(u.type, true, out _) ||
                         u.type.ToLowerInvariant() is "integer" or "boolean" or "float" or "text";
            int index = known ? Array.IndexOf(VTypes.VariableTypes, u.VType) : -1;
            if (index < 0)
            {
                labels.Add(u.type);
                index = labels.Count - 1;
            }
            var type = new ComboBox { ItemsSource = labels, SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
            type.SelectionChanged += (_, _) =>
            {
                if (type.SelectedIndex < 0 || type.SelectedIndex >= VTypes.VariableTypes.Length) return;
                var t = VTypes.VariableTypes[type.SelectedIndex];
                if (string.Equals(u.type, t.ToString(), StringComparison.OrdinalIgnoreCase)) return;
                u.type = t.ToString();
                if (!u.def.StartsWith("?", StringComparison.Ordinal) && !u.def.StartsWith("_", StringComparison.Ordinal))
                    u.def = VTypes.Coerce(t, u.def, _vm!.Models);
                Changed();
            };
            Grid.SetColumn(type, 2);
            row.Children.Add(type);

            var complex = new CheckBox { IsChecked = u.complexName, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(complex, Loc.T("ComplexName\nAdds the Prefix of the block that uses it"));
            complex.IsCheckedChanged += (_, _) => { u.complexName = complex.IsChecked == true; Touch(); };
            Grid.SetColumn(complex, 3);
            row.Children.Add(complex);

            var remove = IconButton("Icon.Minus", Loc.T("Remove variable"), () => _vm!.RemoveUsing(L, u));
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);
            body.Children.Add(row);
        }
        body.Children.Add(AddButtonSimple("Add Variable", () => _vm!.AddUsing(L)));
        return body.Parent as Control ?? body;
    }

    private static Control AddButtonSimple(string text, Action click)
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(new PathIcon { Data = IconGeometry("Icon.Plus") });
        content.Children.Add(new TextBlock { Text = Loc.T(text) });
        button.Content = content;
        button.Click += (_, _) => click();
        return button;
    }
}
