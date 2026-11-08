using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;

namespace Vectorier.Views.Editor;

public partial class TriggerEditorView
{
    private void BuildLoopList()
    {
        var vm = _vm!;
        _syncing = true;
        _shownLoops = Enumerable.Range(0, Data.loops.Count)
            .Where(i => string.IsNullOrEmpty(vm.LoopSearch) || Data.loops[i].Display.Contains(vm.LoopSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        LoopList.ItemsSource = _shownLoops.Select(i =>
        {
            var loop = Data.loops[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = loop.kind == LoopKind.Custom ? " " : "T",
                FontWeight = FontWeight.Bold,
                Width = 10,
                Foreground = Brushes.SteelBlue,
            });
            ToolTip.SetTip(row.Children[0], loop.kind == LoopKind.Custom ? null : Loc.T("Built from a template"));
            row.Children.Add(new TextBlock { Text = loop.Display });
            return row;
        }).ToList();
        LoopList.SelectedIndex = _shownLoops.IndexOf(vm.SelectedLoop);
        _syncing = false;
    }

    private void OnLoopSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _vm == null) return;
        int index = LoopList.SelectedIndex;
        _vm.SelectedLoop = index >= 0 && index < _shownLoops.Count ? _shownLoops[index] : -1;
    }

    private void OnLoopUp(object? sender, RoutedEventArgs e)
    {
        if (_vm != null && _vm.SelectedLoop > 0)
            _vm.MoveLoop(_vm.SelectedLoop, _vm.SelectedLoop - 1);
    }

    private void OnLoopDown(object? sender, RoutedEventArgs e)
    {
        if (_vm != null && _vm.SelectedLoop >= 0 && _vm.SelectedLoop < Data.loops.Count - 1)
            _vm.MoveLoop(_vm.SelectedLoop, _vm.SelectedLoop + 2);
    }

    private TextBox? _loopNameBox;

    private void FocusLoopName()
    {
        _loopNameBox?.Focus();
        _loopNameBox?.SelectAll();
    }

    // ================= VARIABLES ================= //

    private static string LockedHint(string n) => n switch
    {
        "$Active" => Loc.T("Is the trigger switched on? Ticked = 1, unticked = 0."),
        "$AI" => Loc.T("Which AI this trigger reacts to. -1 any, 0 player, 1 hunter, 2 helper."),
        "$Node" => Loc.T("The model's node used to test entry, usually COM."),
        "Flag1" => Loc.T("General purpose flag used by several templates."),
        _ => string.Empty,
    };


    private void BuildVariables()
    {
        VarsPanel.Children.Clear();
        if (_vm!.IsTemplateMode)
        {
            VarsPanel.Children.Add(Hint(_vm.TemplateIsLibrary
                ? "A Library template has no <Init>: its loops are building blocks for other templates. Each loop lists its variables in <Using>."
                : "The template's <Init>. Each loop lists the variables it reads in <Using>."));
        }
        foreach (var v in Data.vars)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,Auto,Auto"), Margin = new Thickness(0, 1, 14, 1) };
            if (v.locked)
            {
                // Shown without the "$" (Active, AI, Node); the XML keeps $Active...
                var name = new TextBlock { Text = TriggerCatalog.VarLabel(v.name), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(name, LockedHint(v.name));
                row.Children.Add(name);
                Control editor = v.name switch
                {
                    "$Active" => BoolEditor(v.value != "0", on => { v.value = on ? "1" : "0"; Touch(); }, "Active", "Inactive"),
                    "$AI" => ChoiceWithCustom(VTypes.AILabels, VTypes.AIValues, v.value, s => { v.value = s; Touch(); }, () => v.uiCustom, c => v.uiCustom = c, integer: true),
                    "$Node" => StrictCombo(v.value, TriggerCatalog.TriggerNodes, s => { v.value = s; Touch(); }),
                    _ => Text(v.value, s => { v.value = s; Touch(); }),
                };
                Grid.SetColumn(editor, 1);
                Grid.SetColumnSpan(editor, 3);
                row.Children.Add(editor);
            }
            else
            {
                // Name, type and remove on the first line, the value editor on its own line under them: value
                // editors (combos with a custom field, model / animation pickers...) need more width than is left
                // beside the name and type in this narrow panel, and overlapped the type list.
                row.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
                row.RowDefinitions = new RowDefinitions("Auto,Auto");
                row.Margin = new Thickness(0, 1, 14, 5);
                var nameBox = new TextBox { Text = v.name, MinWidth = 50 };
                ToolTip.SetTip(nameBox, v.name);
                nameBox.LostFocus += (_, _) =>
                {
                    if (nameBox.Text != v.name)
                        _vm!.RenameVariable(v.name, nameBox.Text ?? string.Empty);
                };
                row.Children.Add(nameBox);

                var value = Literal(v.type, v.value, null, () => v.uiCustom, c => v.uiCustom = c, s => { v.value = s; Touch(); });
                value.Margin = new Thickness(0, 3, 0, 0);
                Grid.SetRow(value, 1);
                Grid.SetColumnSpan(value, 3);
                row.Children.Add(value);

                var type = new ComboBox { ItemsSource = VTypes.VariableTypeLabels, SelectedIndex = Array.IndexOf(VTypes.VariableTypes, v.type), Width = 92, Margin = new Thickness(4, 0, 0, 0) };
                type.SelectionChanged += (_, _) =>
                {
                    if (type.SelectedIndex < 0) return;
                    var t = VTypes.VariableTypes[type.SelectedIndex];
                    if (t == v.type) return;
                    v.type = t;
                    v.value = VTypes.Coerce(t, v.value, _vm!.Models);
                    v.uiCustom = false;
                    Changed();
                };
                Grid.SetColumn(type, 1);
                row.Children.Add(type);

                var remove = IconButton("Icon.Minus", Loc.T("Remove variable"), () => _vm!.RemoveVariable(v));
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
            }
            VarsPanel.Children.Add(row);
        }
    }
}
