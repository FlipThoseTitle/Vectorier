using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

public partial class TriggerEditorView
{
    private void BuildMain()
    {
        MainPanel.Children.Clear();
        _loopNameBox = null;
        var vm = _vm!;
        if (vm.SelectedLoop < 0 || vm.SelectedLoop >= Data.loops.Count)
        {
            MainPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Pick a Loop on the left, or press 'New Loop' to start editing the Trigger."),
                Classes = { "hint" },
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30),
            });
            return;
        }

        var L = Data.loops[vm.SelectedLoop];
        MainPanel.Children.Add(LoopHeader(L));

        switch (L.kind)
        {
            case LoopKind.FullTemplate: BuildFullTemplate(L); break;
            case LoopKind.TemplateLoop: BuildTemplateLoop(L); break;
            case LoopKind.Raw:
                MainPanel.Children.Add(Hint("This part of the trigger isn't something the editor recognises, so it's kept exactly as it was written."));
                MainPanel.Children.Add(RawBox(L.raw, s => { L.raw = s; Touch(); }));
                break;
            default:
                // Template loops declare the variables they read in <Using>, above When.
                if (vm.IsTemplateMode)
                    MainPanel.Children.Add(BuildUsing(L));
                MainPanel.Children.Add(BuildWhen(L));
                MainPanel.Children.Add(BuildOnlyIf(L));
                MainPanel.Children.Add(BuildDo(L));
                break;
        }
    }

    private Control LoopHeader(TLoop L)
    {
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = Loc.T("Loop Name"), VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBox { Text = L.name, Width = 200, IsEnabled = L.kind != LoopKind.FullTemplate };
        OnEdited(name, text =>
        {
            L.name = text;
            Touch();
        });
        name.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(L.name)) L.name = TLoop.NoName;
            BuildLoopList();
        };
        _loopNameBox = name;
        row.Children.Add(name);
        if (!L.HasName)
        {
            var unnamed = new TextBlock { Text = Loc.T("(unnamed)"), Classes = { "hint" } };
            ToolTip.SetTip(unnamed, Loc.T("Saved without a name"));
            row.Children.Add(unnamed);
        }
        row.Children.Add(new TextBlock { Text = Loc.T("Build From"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var kinds = new ComboBox
        {
            ItemsSource = Tr("Scratch", "Template Loop", "Whole Template"),
            SelectedIndex = L.kind == LoopKind.TemplateLoop ? 1 : L.kind == LoopKind.FullTemplate ? 2 : 0,
            Width = 140,
            IsEnabled = L.kind != LoopKind.Raw,
        };
        kinds.SelectionChanged += (_, _) =>
        {
            var kind = kinds.SelectedIndex == 1 ? LoopKind.TemplateLoop : kinds.SelectedIndex == 2 ? LoopKind.FullTemplate : LoopKind.Custom;
            if (kind == L.kind) return;
            L.kind = kind;
            Changed();
        };
        row.Children.Add(kinds);
        return row;
    }

    // ---------------- Templates ----------------

    private void BuildFullTemplate(TLoop L)
    {
        var lib = _vm!.Library;
        MainPanel.Children.Add(Hint("The whole template runs as it is. Its loops are listed below."));
        var row = LabeledRow("Template", TextWithPicker(L.template,
            lib.Templates.Where(t => !t.library).Select(t => t.name).ToList(),
            s => { L.template = s; Changed(); }, rebuildOnType: true));
        MainPanel.Children.Add(row);

        var info = lib.FindTemplate(L.template);
        if (info == null) { NotFound(L.template); return; }

        MainPanel.Children.Add(new TextBlock { Text = Loc.T("What this template does"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        MainPanel.Children.Add(Hint("Names in { } are variables."));
        foreach (var l in info.loops) MainPanel.Children.Add(LoopCard(l, true));
        MainPanel.Children.Add(Required(lib.CollectUses(info.loops, "")));
    }

    private void BuildTemplateLoop(TLoop L)
    {
        var lib = _vm!.Library;
        MainPanel.Children.Add(Hint("This loop runs a ready-made loop from TriggerTemplates.xml."));
        var choices = lib.Templates.SelectMany(t => t.loops.Select(l => l.Full)).ToList();
        MainPanel.Children.Add(LabeledRow("Template Loop", TextWithPicker(L.template, choices, s => { L.template = s; Changed(); }, rebuildOnType: true, groupByDot: true)));

        var info = lib.Find(L.template);
        if (info == null) { NotFound(L.template); return; }

        MainPanel.Children.Add(new TextBlock { Text = Loc.T("What this loop does"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        MainPanel.Children.Add(Hint("Names in { } are variables."));
        MainPanel.Children.Add(LoopCard(info, false));
        MainPanel.Children.Add(Required(lib.CollectUses(new[] { info }, "")));
    }

    private Control LoopCard(TriggerTemplateLibrary.LoopInfo l, bool showName)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (showName)
            stack.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(l.name) ? Loc.T("(unnamed loop)") : l.name, Classes = { "card-title" } });
        bool any = false;
        foreach (var (section, head) in new[] { (TriggerTemplateLibrary.Section.When, "When"), (TriggerTemplateLibrary.Section.OnlyIf, "Only If"), (TriggerTemplateLibrary.Section.Do, "Do") })
        {
            var lines = l.Lines(section);
            if (lines.Count == 0) continue;
            if (any) stack.Children.Add(new Separator { Margin = new Thickness(0, 3) });
            stack.Children.Add(Summary(head, lines));
            any = true;
        }
        if (!any) stack.Children.Add(Hint("This loop is empty."));
        return new Border { Classes = { "card" }, Child = stack };
    }

    private static Control Summary(string head, List<string> lines)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (i == 0)
                grid.Children.Add(new TextBlock { Text = Loc.T(head), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top, MinWidth = 60, Margin = new Thickness(0, 0, 8, 0) });
            var line = new TextBlock { Text = TriggerCatalog.FriendlyText(lines[i]), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 11 };
            Grid.SetRow(line, i);
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);
        }
        return grid;
    }

    private void NotFound(string template)
    {
        if (string.IsNullOrEmpty(template)) return;
        MainPanel.Children.Add(Hint(_vm!.Library.LoadFailed ? "TriggerTemplates.xml couldn't be read." : Loc.F("'{0}' isn't in TriggerTemplates.xml.", template)));
    }

    /// <summary>The template's &lt;Using&gt; variables, and a button to add the missing ones.</summary>
    private Control Required(List<TriggerTemplateLibrary.UsingVar> uses)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        if (uses.Count == 0) return stack;
        stack.Children.Add(new TextBlock { Text = Loc.T("Required Variables"), FontWeight = FontWeight.SemiBold });
        stack.Children.Add(Hint("The template reads these from the Variables panel. Add the missing ones, then set their values there."));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,90,110,*") };
        void Cell(string text, int row, int col, bool bold = false, bool missing = false)
        {
            var tb = new TextBlock { Text = bold ? Loc.T(text) : text, FontSize = 11, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
            if (missing) tb.Classes.Add("missing");
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell("Name", 0, 0, true); Cell("Type", 0, 1, true); Cell("Default", 0, 2, true); Cell("Status", 0, 3, true);
        int missingCount = 0, r = 1;
        foreach (var u in uses)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var have = Data.vars.Find(x => x.name == u.name);
            string status = have == null ? Loc.T("Missing") : have.type != u.vtype && !string.IsNullOrEmpty(u.type) ? Loc.F("Added (as {0})", have.type) : Loc.T("Added");
            if (have == null) missingCount++;
            Cell(TriggerCatalog.VarLabel(u.name), r, 0); Cell(u.vtype.ToString(), r, 1); Cell(string.IsNullOrEmpty(u.def) ? Loc.T("(empty)") : u.def, r, 2); Cell(status, r, 3, missing: have == null);
            r++;
        }
        stack.Children.Add(grid);
        if (missingCount > 0)
        {
            var add = new Button { Content = Loc.F("Add Missing Variables ({0})", missingCount), Margin = new Thickness(0, 6, 0, 0) };
            add.Click += (_, _) => _vm!.AddMissingVariables(uses);
            stack.Children.Add(add);
        }
        return stack;
    }

    // ---------------- When ----------------

    private Control BuildWhen(TLoop L)
    {
        var body = Block("When", "When does this loop run?");
        if (L.events.Count == 0)
            body.Children.Add(Hint("None. Without an event this loop never runs."));

        for (int i = 0; i < L.events.Count; i++)
        {
            var E = L.events[i];
            int idx = i;
            string title = E.kind == EvKind.Block ? Loc.T("Template") + "   " + E.template : E.kind == EvKind.Raw ? Loc.T("Kept as written") : Loc.T(TriggerCatalog.FindEvent(E.id)?.title ?? E.id);
            var card = Card(title, CardButtons(L.events, idx));
            if (E.kind == EvKind.Event && TriggerCatalog.FindEvent(E.id) is { } def)
            {
                if (!string.IsNullOrEmpty(def.hint)) card.Children.Add(Hint(def.hint));
                foreach (var pd in def.ps) card.Children.Add(ParamRow(pd, E.Get(pd.attr), pd.type));
            }
            else if (E.kind == EvKind.Block)
            {
                card.Children.Add(BlockRef(E.template, E.prefix, s => E.template = s, s => E.prefix = s, TriggerTemplateLibrary.Section.When));
            }
            else
            {
                card.Children.Add(RawBox(E.raw, s => { E.raw = s; Touch(); }));
            }
            body.Children.Add(WrapCard(card));
        }

        // No templates here: EventBlock is only read from old files.
        var menu = new MenuFlyout();
        foreach (var def in TriggerCatalog.Events)
        {
            bool used = L.events.Exists(x => x.kind == EvKind.Event && x.id == def.id);
            var item = new MenuItem { Header = Loc.T(def.menu), IsEnabled = !used };
            var d = def;
            item.Click += (_, _) => { L.events.Add(TriggerEditorViewModel.NewEvent(d)); Changed(); };
            menu.Items.Add(item);
        }
        body.Children.Add(AddButton("Add Event", menu));
        return body.Parent as Control ?? body;
    }

    // ---------------- Only If ----------------

    private Control BuildOnlyIf(TLoop L)
    {
        var body = Block("Only If", "What must be true?");
        if (L.conds.Count == 0)
            body.Children.Add(Hint("Nothing. The loop runs every time the event happens."));
        if (L.conds.Count > 1)
        {
            var match = new ComboBox { ItemsSource = Tr("All of these", "Any of these"), SelectedIndex = L.rootOp == "Or" ? 1 : 0, Width = 140 };
            match.SelectionChanged += (_, _) => { L.rootOp = match.SelectedIndex == 1 ? "Or" : "And"; Touch(); };
            body.Children.Add(LabeledRow("Match", match));
        }
        BuildCondList(body, L.conds);
        body.Children.Add(AddButton("Add Condition", CondMenu(L.conds)));
        return body.Parent as Control ?? body;
    }

    private void BuildCondList(Panel host, List<TCond> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var C = list[i];
            int idx = i;
            StackPanel card;
            switch (C.kind)
            {
                case CondKind.Compare:
                    {
                        card = Card(Loc.T("Compare"), CardButtons(list, idx));
                        card.Children.Add(ParamRow(CheckDef, C.a, CheckDef.type));
                        BuildCompare(card, C);
                        break;
                    }
                case CondKind.Group:
                    {
                        var op = new ComboBox { ItemsSource = Tr("All of these", "Any of these"), SelectedIndex = C.op == "Or" ? 1 : 0, Width = 140 };
                        op.SelectionChanged += (_, _) => { C.op = op.SelectedIndex == 1 ? "Or" : "And"; Touch(); };
                        card = Card(Loc.T("Group"), CardButtons(list, idx), op);
                        BuildCondList(card, C.children);
                        card.Children.Add(AddButton("Add To Group", CondMenu(C.children)));
                        break;
                    }
                case CondKind.Select:
                    {
                        card = Card(Loc.T("Look through a list"), CardButtons(list, idx));
                        card.Children.Add(ParamRow(SelObjectDef, C.selObject, SelObjectDef.type));
                        card.Children.Add(ParamRow(SelFromDef, C.selFrom, SelFromDef.type));
                        var none = new CheckBox { Content = Loc.T("None of them may match"), IsChecked = C.not };
                        none.IsCheckedChanged += (_, _) => { C.not = none.IsChecked == true; Touch(); };
                        card.Children.Add(none);
                        BuildCondList(card, C.children);
                        card.Children.Add(AddButton("Add To List Test", CondMenu(C.children)));
                        break;
                    }
                case CondKind.Block:
                    card = Card(Loc.T("Template") + "   " + C.template, CardButtons(list, idx));
                    card.Children.Add(BlockRef(C.template, C.prefix, s => C.template = s, s => C.prefix = s, TriggerTemplateLibrary.Section.OnlyIf));
                    break;
                default:
                    card = Card(Loc.T("Kept as written"), CardButtons(list, idx));
                    card.Children.Add(RawBox(C.raw, s => { C.raw = s; Touch(); }));
                    break;
            }
            host.Children.Add(WrapCard(card));
        }
    }

    /// <summary>One way of comparing: the operator, and for booleans a fixed true / false instead of a second value.</summary>
    private sealed record CompareOp(string Label, Cmp Cmp, bool Not, string? BoolValue);

    /// <summary>
    /// The comparison follows the type of what is checked: booleans read "is true" / "is false" (and can still be
    /// compared with another value), only numbers can be greater / less, and the second value gets the editor of
    /// that type (a key list for keys, the results for a model's condition...).
    /// </summary>
    private void BuildCompare(StackPanel card, TCond C)
    {
        var type = CompareType(C.a, out var choices);
        bool isBool = type == VType.Bool;
        var ops = new List<CompareOp>();
        if (isBool)
        {
            ops.Add(new CompareOp("is true", Cmp.Equal, false, "1"));
            ops.Add(new CompareOp("is false", Cmp.Equal, false, "0"));
        }
        ops.Add(new CompareOp(CompareLabels[0], Cmp.Equal, false, null));
        ops.Add(new CompareOp(CompareLabels[1], Cmp.Equal, true, null));
        if (VTypes.FamilyOf(type) == VTypes.Family.Number)
        {
            for (int i = 2; i < CompareLabels.Length; i++)
                ops.Add(new CompareOp(CompareLabels[i], (Cmp)(i / 2), i % 2 == 1, null));
        }

        string? boolLiteral = BoolLiteral(C.b);
        int current = isBool && !C.not && C.cmp == Cmp.Equal && boolLiteral != null
            ? (boolLiteral == "1" ? 0 : 1)
            : ops.FindIndex(o => o.BoolValue == null && o.Cmp == C.cmp && o.Not == C.not);
        if (current < 0)
        {
            // e.g. "is greater than" on a text from a file: kept as written.
            ops.Add(new CompareOp(CompareLabels[(int)C.cmp * 2 + (C.not ? 1 : 0)], C.cmp, C.not, null));
            current = ops.Count - 1;
        }

        var combo = new ComboBox { ItemsSource = ops.Select(o => Loc.T(o.Label)).ToList(), SelectedIndex = current, Width = 170 };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex == current) return;
            var op = ops[combo.SelectedIndex];
            C.cmp = op.Cmp;
            C.not = op.Not;
            if (op.BoolValue != null)
            {
                C.b = TValue.Literal(op.BoolValue);
            }
            else if (isBool && BoolLiteral(C.b) != null)
            {
                // "is equal to" another value: compare with a (boolean) variable instead of a fixed true / false.
                C.b = new TValue { mode = ValueMode.Variable, varName = Data.vars.FirstOrDefault(v => v.type == VType.Bool && v.name != C.a.varName)?.name ?? string.Empty };
            }
            Changed();
        };
        card.Children.Add(LabeledRow(string.Empty, combo));
        if (ops[current].BoolValue == null)
            card.Children.Add(ParamRow(new ParamDef(AgainstDef.attr, AgainstDef.label, type, "", choices).Queries(), C.b, type));
    }

    /// <summary>"1" / "0" when the value is a fixed boolean, otherwise null.</summary>
    private static string? BoolLiteral(TValue v)
    {
        if (v.mode != ValueMode.Literal) return null;
        if (v.text == "1" || string.Equals(v.text, "true", StringComparison.OrdinalIgnoreCase)) return "1";
        if (v.text == "0" || string.Equals(v.text, "false", StringComparison.OrdinalIgnoreCase)) return "0";
        return null;
    }

    /// <summary>Type of the checked value: a variable's type, a model property's, a node position is a number.</summary>
    private VType CompareType(TValue a, out string[]? choices)
    {
        choices = null;
        switch (a.mode)
        {
            case ValueMode.Variable: return Data.TypeOf(a.varName);
            case ValueMode.ModelProp: return TriggerCatalog.ModelPropType(a.prop, out choices);
            case ValueMode.NodePos: return VType.Number;
            default: return VType.String;
        }
    }

    private MenuFlyout CondMenu(List<TCond> list)
    {
        var menu = new MenuFlyout();
        void Add(string header, Func<TCond> make)
        {
            var item = new MenuItem { Header = Loc.T(header) };
            item.Click += (_, _) => { list.Add(make()); Changed(); };
            menu.Items.Add(item);
        }
        Add("Compare two values", () => new TCond { kind = CondKind.Compare });
        Add("Group", () => new TCond { kind = CondKind.Group });
        Add("Look through a list", () => new TCond { kind = CondKind.Select, selFrom = TValue.Literal("Equipped") });
        menu.Items.Add(new Separator());

        var quick = new MenuItem { Header = Loc.T("Quick") };
        void Quick(string header, string a, string b, bool not)
        {
            var item = new MenuItem { Header = Loc.T(header) };
            item.Click += (_, _) => { list.Add(new TCond { kind = CondKind.Compare, not = not, a = TValue.Parse(a), b = TValue.Parse(b) }); Changed(); };
            quick.Items.Add(item);
        }
        Quick("Triggered by the Player", "?getModel[_$Model].AI", "0", false);
        Quick("Triggered by a Bot", "?getModel[_$Model].AI", "0", true);
        Quick("This Trigger is Active", "_$Active", "1", false);
        Quick("Key Pressed was Up", "_$Key", "Up", false);
        Quick("Signal Matches a Variable", "_$ActionID", "_Activator", false);
        Quick("Player is Standing Still", "?getModel[Player].animationName", "Stand", false);
        Quick("Model Faces Right", "?getModel[_$Model].direction", "1", false);
        menu.Items.Add(quick);
        menu.Items.Add(new Separator());
        menu.Items.Add(TemplateItems("From Template", l => l.hasConditions, full => { list.Add(new TCond { kind = CondKind.Block, template = full }); Changed(); }));
        return menu;
    }

    // ---------------- Do ----------------

    private Control BuildDo(TLoop L)
    {
        var body = Block("Do", "What happens next?");
        if (L.acts.Count == 0)
            body.Children.Add(Hint("Nothing yet."));
        BuildActList(body, L.acts);
        body.Children.Add(AddButton("Add Action", ActMenu(L.acts)));
        return body.Parent as Control ?? body;
    }

    private void BuildActList(Panel host, List<TAct> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var A = list[i];
            int idx = i;
            StackPanel card;
            switch (A.kind)
            {
                case ActKind.Choose:
                    {
                        string[] labels = { "One after another", "All at the same time", "Pick at random" };
                        string[] values = { "Straight", "Sync", "Random" };
                        var order = new ComboBox { ItemsSource = Tr(labels), SelectedIndex = Math.Max(0, Array.IndexOf(values, A.order)), Width = 170 };
                        order.SelectionChanged += (_, _) => { A.order = values[Math.Max(0, order.SelectedIndex)]; Changed(); };
                        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { order } };
                        if (A.order == "Random")
                        {
                            header.Children.Add(new TextBlock { Text = Loc.T("How many") });
                            var set = new NumericUpDown { Value = Math.Max(1, A.set), Minimum = 1, Increment = 1, FormatString = "0", Width = 90 };
                            set.ValueChanged += (_, _) => { A.set = Math.Max(1, (int)(set.Value ?? 1)); Touch(); };
                            header.Children.Add(set);
                        }
                        card = Card(Loc.T("Run Order"), CardButtons(list, idx), header);
                        BuildActList(card, A.children);
                        card.Children.Add(AddButton("Add To Group", ActMenu(A.children)));
                        break;
                    }
                case ActKind.Block:
                    card = Card(Loc.T("Template") + "   " + A.template, CardButtons(list, idx));
                    card.Children.Add(BlockRef(A.template, A.prefix, s => A.template = s, s => A.prefix = s, TriggerTemplateLibrary.Section.Do));
                    break;
                case ActKind.Raw:
                    card = Card(Loc.T("Kept as written"), CardButtons(list, idx));
                    card.Children.Add(RawBox(A.raw, s => { A.raw = s; Touch(); }));
                    break;
                default:
                    {
                        var def = TriggerCatalog.FindAction(A.id);
                        card = Card(def?.title ?? A.id, CardButtons(list, idx));
                        if (def != null)
                        {
                            if (!string.IsNullOrEmpty(def.hint)) card.Children.Add(Hint(def.hint));
                            // Set/Add Variable: the value editor follows the picked variable's type.
                            VType follow = VType.String;
                            foreach (var p in def.ps)
                                if (p.varName) { follow = Data.TypeOf(A.Get(p.attr).text); break; }
                            // The animation's start frame follows the preview's trim (ForceAnimation Frame, ModelExecute AnimFrame).
                            var framePd = def.ps.FirstOrDefault(p => p.attr is "Frame" or "AnimFrame" or "StartFrame");
                            _animationFrame = framePd != null ? A.Get(framePd.attr) : null;
                            foreach (var pd in def.ps)
                                card.Children.Add(ParamRow(pd, A.Get(pd.attr), pd.typedByVariable ? follow : pd.type));
                            _animationFrame = null;
                        }
                        break;
                    }
            }
            host.Children.Add(WrapCard(card));
        }
    }

    private MenuFlyout ActMenu(List<TAct> list)
    {
        var menu = new MenuFlyout();
        var groups = new Dictionary<string, MenuItem>();
        foreach (var def in TriggerCatalog.Actions)
        {
            var parts = def.menu.Split('/');
            var item = new MenuItem { Header = Loc.T(parts[^1]) };
            var d = def;
            item.Click += (_, _) => { list.Add(TriggerEditorViewModel.NewAction(d)); Changed(); };
            if (parts.Length > 1)
            {
                if (!groups.TryGetValue(parts[0], out var group))
                {
                    group = new MenuItem { Header = Loc.T(parts[0]) };
                    groups[parts[0]] = group;
                    menu.Items.Add(group);
                }
                group.Items.Add(item);
            }
            else
            {
                menu.Items.Add(item);
            }
        }
        // Not in the catalog since it has children; added under Flow by hand.
        var choose = new MenuItem { Header = Loc.T("Run Order Group") };
        choose.Click += (_, _) => { list.Add(new TAct { kind = ActKind.Choose, order = "Straight" }); Changed(); };
        if (groups.TryGetValue("Flow", out var flow)) flow.Items.Add(choose);
        else menu.Items.Add(choose);

        menu.Items.Add(new Separator());
        menu.Items.Add(TemplateItems("From Template", l => l.hasActions, full => { list.Add(new TAct { kind = ActKind.Block, template = full }); Changed(); }));
        return menu;
    }

    private MenuItem TemplateItems(string header, Func<TriggerTemplateLibrary.LoopInfo, bool> filter, Action<string> pick)
    {
        var root = new MenuItem { Header = Loc.T(header) };
        var lib = _vm!.Library;
        if (!lib.Available)
        {
            root.Items.Add(new MenuItem { Header = Loc.T("TriggerTemplates.xml not found"), IsEnabled = false });
            return root;
        }
        foreach (var t in lib.Templates)
        {
            var loops = t.loops.Where(filter).ToList();
            if (loops.Count == 0) continue;
            var tm = new MenuItem { Header = t.name };
            foreach (var l in loops)
            {
                string full = l.Full;
                var item = new MenuItem { Header = string.IsNullOrEmpty(l.name) ? Loc.T("(unnamed)") : l.name };
                item.Click += (_, _) => pick(full);
                tm.Items.Add(item);
            }
            root.Items.Add(tm);
        }
        return root;
    }

    /// <summary>ConditionBlock / ActionBlock (or an old EventBlock): template + prefix and what it does.</summary>
    private Control BlockRef(string template, string prefix, Action<string> setTemplate, Action<string> setPrefix, TriggerTemplateLibrary.Section section)
    {
        var lib = _vm!.Library;
        var stack = new StackPanel { Spacing = 4 };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*,Auto,60") };
        row.Children.Add(new TextBlock { Text = Loc.T("Template") });
        var tpl = TextWithPicker(template, lib.Templates.SelectMany(t => t.loops.Select(l => l.Full)).ToList(), s => { setTemplate(s); Changed(); }, rebuildOnType: true, groupByDot: true);
        Grid.SetColumn(tpl, 1);
        row.Children.Add(tpl);
        var pl = new TextBlock { Text = Loc.T("Prefix"), Margin = new Thickness(8, 0, 4, 0) };
        ToolTip.SetTip(pl, Loc.T("Added before the template's variable names\ne.g. 1Sound instead of Sound"));
        Grid.SetColumn(pl, 2);
        row.Children.Add(pl);
        var pre = new TextBox { Text = prefix ?? string.Empty };
        pre.LostFocus += (_, _) => { if (pre.Text != prefix) { setPrefix(pre.Text ?? string.Empty); Changed(); } };
        Grid.SetColumn(pre, 3);
        row.Children.Add(pre);
        stack.Children.Add(row);

        var info = lib.Find(template);
        if (info == null)
        {
            if (!string.IsNullOrEmpty(template))
                stack.Children.Add(Hint(lib.LoadFailed ? "TriggerTemplates.xml couldn't be read." : Loc.F("'{0}' isn't in TriggerTemplates.xml.", template)));
            return stack;
        }
        var lines = info.Lines(section);
        if (lines.Count > 0)
            stack.Children.Add(new Border { Classes = { "card" }, Child = Summary(section == TriggerTemplateLibrary.Section.When ? "When" : section == TriggerTemplateLibrary.Section.OnlyIf ? "Only If" : "Do", lines) });
        stack.Children.Add(Required(lib.CollectUses(new[] { info }, prefix)));
        return stack;
    }
}
