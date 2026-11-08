using System;
using System.Collections.Generic;
using System.Globalization;
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
    private Control ParamRow(ParamDef pd, TValue v, VType lit)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*"), Margin = new Thickness(0, 2) };
        var label = new TextBlock { Text = Loc.T(pd.label), TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, Loc.T(string.IsNullOrEmpty(pd.hint) ? pd.label : pd.hint));
        grid.Children.Add(label);
        var editor = ValueEditor(pd, v, lit);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static string ModeLabel(ValueMode m, VType lit) => m switch
    {
        ValueMode.Variable => Loc.T("Variable"),
        ValueMode.ModelProp => Loc.T("Model"),
        ValueMode.NodePos => Loc.T("Node"),
        _ => Loc.T(VTypes.SlotLabel(lit)),
    };

    /// <summary>Mode dropdown (literal / variable / model property / node position) plus the editor for that mode.</summary>
    private Control ValueEditor(ParamDef pd, TValue v, VType lit)
    {
        // Name slots are a plain variable name, written without the "_".
        if (pd.varName)
            return TextWithPicker(v.text, VarChoices(pd, lit), s => { v.mode = ValueMode.Literal; v.text = s; Changed(); }, rebuildOnType: true,
                itemLabel: TriggerCatalog.VarLabel, boxText: TriggerCatalog.VarLabel, boxParse: TriggerCatalog.VarFromLabel);

        // The checked value of a comparison decides how it compares: changing it rebuilds the card.
        bool rebuild = ReferenceEquals(pd, CheckDef);

        var modes = new List<ValueMode> { ValueMode.Literal, ValueMode.Variable };
        if (pd.query) { modes.Add(ValueMode.ModelProp); modes.Add(ValueMode.NodePos); }
        if (!modes.Contains(v.mode)) modes.Add(v.mode); // a file may use a query where none is offered: keep it

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ModeColumns) };
        var mode = new ComboBox { ItemsSource = modes.Select(m => ModeLabel(m, lit)).ToList(), SelectedIndex = modes.IndexOf(v.mode), HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0 || modes[mode.SelectedIndex] == v.mode) return;
            v.mode = modes[mode.SelectedIndex];
            if (v.mode == ValueMode.Literal) v.text = VTypes.Coerce(lit, v.text, _vm!.Models);
            v.uiCustom = false;
            Changed();
        };
        grid.Children.Add(mode);

        Control editor;
        switch (v.mode)
        {
            case ValueMode.Variable:
                editor = TextWithPicker(v.varName, VarChoices(pd, lit), s => { v.varName = s; if (rebuild) Changed(); else Touch(); },
                    rebuildOnType: rebuild, rebuildOnPick: rebuild,
                    itemLabel: TriggerCatalog.VarLabel, boxText: TriggerCatalog.VarLabel, boxParse: TriggerCatalog.VarFromLabel);
                break;
            case ValueMode.ModelProp:
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                    row.Children.Add(RefEditor(v, false));
                    var prop = new ComboBox { ItemsSource = TriggerCatalog.ModelProps, SelectedIndex = Math.Max(0, Array.IndexOf(TriggerCatalog.ModelProps, v.prop)), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 0, 0) };
                    prop.SelectionChanged += (_, _) => { if (prop.SelectedItem is string s && s != v.prop) { v.prop = s; if (rebuild) Changed(); else Touch(); } };
                    Grid.SetColumn(prop, 1);
                    row.Children.Add(prop);
                    editor = row;
                    break;
                }
            case ValueMode.NodePos:
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
                    row.Children.Add(RefEditor(v, false));
                    var node = RefEditor(v, true);
                    node.Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(node, 1);
                    row.Children.Add(node);
                    var coord = new ComboBox { ItemsSource = TriggerCatalog.NodeCoords, SelectedIndex = Math.Max(0, Array.IndexOf(TriggerCatalog.NodeCoords, v.coord)), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 0, 0) };
                    coord.SelectionChanged += (_, _) => { if (coord.SelectedItem is string s) { v.coord = s; Touch(); } };
                    Grid.SetColumn(coord, 2);
                    row.Children.Add(coord);
                    editor = row;
                    break;
                }
            default:
                editor = Literal(lit, v.text, pd, () => v.uiCustom, c => v.uiCustom = c, s => { v.text = s; Touch(); });
                break;
        }
        editor.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    /// <summary>
    /// The trigger's settings (Active, AI, Node, Flag1), a line, what the game sets (Model inside Trigger, Key Pressed in
    /// Trigger, Signal's Name), a line, then the user's variables: those whose type fits the slot first, the others
    /// under "Other Types". "-" is a separator.
    /// </summary>
    private List<string> VarChoices(ParamDef? pd, VType lit)
    {
        bool all = pd == null || pd.query || pd.varName;
        var fam = VTypes.FamilyOf(lit);
        var list = new List<string>(TriggerCatalog.FixedVars) { "-" };
        list.AddRange(TriggerCatalog.BuiltInVars);
        var names = Data.VarNames().Where(n => !TriggerCatalog.IsLabelled(n) && Array.IndexOf(TriggerCatalog.FixedVars, n) < 0).ToList();
        if (names.Count > 0)
            list.Add("-");
        list.AddRange(names.Where(n => all || VTypes.FamilyOf(Data.TypeOf(n)) == fam));
        list.AddRange(names.Where(n => !(all || VTypes.FamilyOf(Data.TypeOf(n)) == fam)).Select(n => "Other Types/" + n));
        return list;
    }

    /// <summary>Model or node part of a ?getModel query; a leading "_" means a variable.</summary>
    private Control RefEditor(TValue v, bool nodeSlot)
    {
        string shown = nodeSlot ? v.NodeRef : v.ModelRef;
        IList<string> literals = nodeSlot ? TriggerCatalog.CommonNodes : _vm!.Models.Count > 0 ? _vm.Models : TriggerCatalog.CommonModels;
        var choices = literals.Select(n => "Name/" + n).Concat(Data.VarNames().Select(n => "Variable/" + n)).ToList();
        // _$Model is shown as "Model inside Trigger" (and typed back the same way).
        static string Show(string s) => s.Length > 1 && s[0] == '_' && TriggerCatalog.IsLabelled(s[1..]) ? TriggerCatalog.VarLabel(s[1..]) : s;
        static string Parse(string s) => TriggerCatalog.VarFromLabel(s) is var n && n != s ? "_" + n : s;
        return TextWithPicker(shown, choices, s =>
        {
            bool isVar;
            string name;
            if (s.StartsWith("Variable/", StringComparison.Ordinal)) { isVar = true; name = s["Variable/".Length..]; }
            else if (s.StartsWith("Name/", StringComparison.Ordinal)) { isVar = false; name = s["Name/".Length..]; }
            else { isVar = s.Length > 1 && s[0] == '_'; name = isVar ? s[1..] : s; }
            if (nodeSlot) { v.nodeIsVar = isVar; v.node = name; }
            else { v.modelIsVar = isVar; v.model = name; }
            Touch();
        }, rebuildOnPick: true, itemLabel: TriggerCatalog.VarLabel, boxText: Show, boxParse: Parse);
    }

    /// <summary>Literal editor for a value type (shared by slots and the Variables panel).</summary>
    private Control Literal(VType t, string val, ParamDef? pd, Func<bool> getCustom, Action<bool> setCustom, Action<string> set)
    {
        val ??= string.Empty;
        switch (t)
        {
            case VType.Bool:
                {
                    string on = pd?.onValue ?? "1", off = pd?.offValue ?? "0";
                    bool cur = string.Equals(val, on, StringComparison.OrdinalIgnoreCase) || (on == "1" && string.Equals(val, "true", StringComparison.OrdinalIgnoreCase));
                    return BoolEditor(cur, b => set(b ? on : off), on == "1" ? "True" : on, on == "1" ? "False" : off);
                }
            case VType.Int:
            case VType.UInt:
                {
                    VTypes.TryInt(val, out int n);
                    if (t == VType.UInt) n = Math.Max(0, n);
                    var box = new NumericUpDown { Value = n, Increment = 1, FormatString = "0", Minimum = t == VType.UInt ? 0 : int.MinValue, ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
                    box.ValueChanged += (_, _) => set(((int)(box.Value ?? 0)).ToString(CultureInfo.InvariantCulture));
                    return box;
                }
            case VType.Number:
                {
                    VTypes.TryFloat(val, out float f);
                    var box = new NumericUpDown { Value = (decimal)f, Increment = 1, FormatString = "0.#####", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
                    box.ValueChanged += (_, _) => set(VTypes.Num((float)(box.Value ?? 0)));
                    return box;
                }
            case VType.AI:
                return ChoiceWithCustom(VTypes.AILabels, VTypes.AIValues, val, set, getCustom, setCustom, integer: true);
            case VType.Zoom:
                return ChoiceWithCustom(VTypes.ZoomLabels, VTypes.ZoomValues, val, set, getCustom, setCustom, integer: false);
            case VType.Key:
                return StrictCombo(val, VTypes.Keys, set);
            case VType.Orientation:
                return StrictCombo(val, pd?.choices is { Length: > 0 } c ? c : VTypes.Orientations, set);
            case VType.Model:
                return ModelPicker(val, set, getCustom, setCustom);
            case VType.Spawn:
                return _vm!.Spawns.Count > 0 ? StrictCombo(val, _vm.Spawns, set) : Text(val, set);
            case VType.Color:
                {
                    string hex = VTypes.NormalizeHex(val) ?? VTypes.White;
                    var color = Color.TryParse("#" + hex.Substring(7, 2) + hex.Substring(1, 6), out var parsed) ? parsed : Colors.White;
                    var picker = new ColorPicker { Color = color, IsAlphaEnabled = true, HorizontalAlignment = HorizontalAlignment.Left };
                    picker.ColorChanged += (_, e) => set($"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}{e.NewColor.A:X2}");
                    return picker;
                }
            case VType.Animation:
                return AnimationPicker(val, set, _animationFrame);
            case VType.Sound:
                return SoundPicker(val, set);
            default:
                return pd?.choices is { Length: > 0 } choices ? TextWithPicker(val, choices, set) : Text(val, set);
        }
    }
}
