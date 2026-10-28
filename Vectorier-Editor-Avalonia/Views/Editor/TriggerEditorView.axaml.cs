using Avalonia.Input;
using Vectorier_Editor_Avalonia.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier_Editor_Avalonia.Services.Triggers;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Views.Editor;

/// <summary>
/// The Trigger Editor's forms are built in code (like Unity's IMGUI window): every entry of a loop can have a
/// different set of parameters and value modes, so the panel is rebuilt whenever the structure changes, while
/// simple value edits write straight to the trigger model.
/// </summary>
public partial class TriggerEditorView : UserControl
{
    private const double LabelWidth = 110;
    private static readonly ParamDef CheckDef = new ParamDef("Value1", "Check").Queries();
    private static readonly ParamDef AgainstDef = new ParamDef("Value2", "Against").Queries();
    private static readonly ParamDef SelObjectDef = new("Object", "Put each into");
    private static readonly ParamDef SelFromDef = new("From", "List", VType.String, "Equipped");
    private static readonly string[] CompareLabels = { "is equal to", "is not equal to", "is greater than", "is not greater than", "is less than", "is not less than" };

    private TriggerEditorViewModel? _vm;
    private bool _rebuildScheduled;
    private bool _syncing;
    private List<int> _shownLoops = new();

    public TriggerEditorView()
    {
        InitializeComponent();
        LoopList.SelectionChanged += OnLoopSelected;
        LoopList.DoubleTapped += (_, _) => FocusLoopName();
        PresetBox.SelectionChanged += OnPresetPicked;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.StructureChanged -= ScheduleRebuild;
            _vm.PropertyChanged -= OnVmChanged;
            _vm.Presets.CollectionChanged -= OnPresetsChanged;
        }
        _vm = DataContext as TriggerEditorViewModel;
        if (_vm != null)
        {
            _vm.StructureChanged += ScheduleRebuild;
            _vm.PropertyChanged += OnVmChanged;
            _vm.Presets.CollectionChanged += OnPresetsChanged;
        }
        SyncPresetBox();
        ScheduleRebuild();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TriggerEditorViewModel.PresetIndex))
            SyncPresetBox();
    }

    private void OnPresetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(SyncPresetBox);

    private void SyncPresetBox()
    {
        if (_vm == null) return;
        _syncing = true;
        PresetBox.SelectedIndex = _vm.PresetIndex;
        _syncing = false;
    }

    private async void OnPresetPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _vm == null || PresetBox.SelectedIndex < 0 || PresetBox.SelectedIndex == _vm.PresetIndex) return;
        await _vm.LoadPresetAsync(PresetBox.SelectedIndex);
        SyncPresetBox();
    }

    private void OnCopyXml(object? sender, RoutedEventArgs e)
    {
        if (_vm != null)
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(_vm.XmlPreview);
    }

    private void ScheduleRebuild()
    {
        if (_rebuildScheduled) return;
        _rebuildScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildScheduled = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        if (_vm == null) return;
        var offset = MainScroll.Offset;
        BuildLoopList();
        BuildVariables();
        BuildMain();
        Dispatcher.UIThread.Post(() => MainScroll.Offset = offset, DispatcherPriority.Background);
    }

    private TriggerData Data => _vm!.Data;

    private void Touch() => _vm?.Touch();

    private void Changed() => _vm?.Changed();

    // ================= LOOPS ================= //

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
            ToolTip.SetTip(row.Children[0], loop.kind == LoopKind.Custom ? null : "Built from a template");
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
        "$Active" => "Is the trigger switched on? Ticked = 1, unticked = 0.",
        "$AI" => "Which AI this trigger reacts to. -1 any, 0 player, 1 hunter, 2 helper.",
        "$Node" => "The model's node used to test entry, usually COM.",
        "Flag1" => "General purpose flag used by several templates.",
        _ => string.Empty,
    };

    private static readonly string[] TriggerNodes = { "COM", "DetectorH", "Head", "Foot" };

    private void BuildVariables()
    {
        VarsPanel.Children.Clear();
        foreach (var v in Data.vars)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,Auto,Auto"), Margin = new Thickness(0, 1, 14, 1) };
            if (v.locked)
            {
                var name = new TextBlock { Text = v.name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(name, LockedHint(v.name));
                row.Children.Add(name);
                Control editor = v.name switch
                {
                    "$Active" => BoolEditor(v.value != "0", on => { v.value = on ? "1" : "0"; Touch(); }, "Active", "Inactive"),
                    "$AI" => ChoiceWithCustom(VTypes.AILabels, VTypes.AIValues, v.value, s => { v.value = s; Touch(); }, () => v.uiCustom, c => v.uiCustom = c, integer: true),
                    "$Node" => TextWithPicker(v.value, TriggerNodes, s => { v.value = s; Touch(); }),
                    _ => Text(v.value, s => { v.value = s; Touch(); }),
                };
                Grid.SetColumn(editor, 1);
                Grid.SetColumnSpan(editor, 3);
                row.Children.Add(editor);
            }
            else
            {
                var nameBox = new TextBox { Text = v.name, MinWidth = 50 };
                ToolTip.SetTip(nameBox, v.name);
                nameBox.LostFocus += (_, _) =>
                {
                    if (nameBox.Text != v.name)
                        _vm!.RenameVariable(v.name, nameBox.Text ?? string.Empty);
                };
                row.Children.Add(nameBox);

                var value = Literal(v.type, v.value, null, () => v.uiCustom, c => v.uiCustom = c, s => { v.value = s; Touch(); });
                value.Margin = new Thickness(4, 0);
                Grid.SetColumn(value, 1);
                row.Children.Add(value);

                var type = new ComboBox { ItemsSource = VTypes.VariableTypeLabels, SelectedIndex = Array.IndexOf(VTypes.VariableTypes, v.type), Width = 92 };
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
                Grid.SetColumn(type, 2);
                row.Children.Add(type);

                var remove = IconButton("Icon.Minus", "Remove variable", () => _vm!.RemoveVariable(v));
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);
            }
            VarsPanel.Children.Add(row);
        }
    }

    // ================= MAIN PANEL ================= //

    private void BuildMain()
    {
        MainPanel.Children.Clear();
        _loopNameBox = null;
        var vm = _vm!;
        if (vm.SelectedLoop < 0 || vm.SelectedLoop >= Data.loops.Count)
        {
            MainPanel.Children.Add(new TextBlock
            {
                Text = "Pick a Loop on the left, or press 'New Loop' to start editing the Trigger.",
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
                MainPanel.Children.Add(BuildWhen(L));
                MainPanel.Children.Add(BuildOnlyIf(L));
                MainPanel.Children.Add(BuildDo(L));
                break;
        }
    }

    private Control LoopHeader(TLoop L)
    {
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = "Loop Name", VerticalAlignment = VerticalAlignment.Center });
        var name = new TextBox { Text = L.name, Width = 200, IsEnabled = L.kind != LoopKind.FullTemplate };
        name.TextChanged += (_, _) =>
        {
            L.name = name.Text ?? string.Empty;
            Touch();
        };
        name.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(L.name)) L.name = TLoop.NoName;
            BuildLoopList();
        };
        _loopNameBox = name;
        row.Children.Add(name);
        if (!L.HasName)
        {
            var unnamed = new TextBlock { Text = "(unnamed)", Classes = { "hint" } };
            ToolTip.SetTip(unnamed, "A loop called 'Loop' is written without a Name attribute.");
            row.Children.Add(unnamed);
        }
        row.Children.Add(new TextBlock { Text = "Build From", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var kinds = new ComboBox
        {
            ItemsSource = new[] { "Scratch", "Template Loop", "Whole Template" },
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

        MainPanel.Children.Add(new TextBlock { Text = "What this template does", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
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

        MainPanel.Children.Add(new TextBlock { Text = "What this loop does", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        MainPanel.Children.Add(Hint("Names in { } are variables."));
        MainPanel.Children.Add(LoopCard(info, false));
        MainPanel.Children.Add(Required(lib.CollectUses(new[] { info }, "")));
    }

    private Control LoopCard(TriggerTemplateLibrary.LoopInfo l, bool showName)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (showName)
            stack.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(l.name) ? "(unnamed loop)" : l.name, Classes = { "card-title" } });
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
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*") };
        for (int i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (i == 0)
                grid.Children.Add(new TextBlock { Text = head, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top });
            var line = new TextBlock { Text = lines[i], TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 11 };
            Grid.SetRow(line, i);
            Grid.SetColumn(line, 1);
            grid.Children.Add(line);
        }
        return grid;
    }

    private void NotFound(string template)
    {
        if (string.IsNullOrEmpty(template)) return;
        MainPanel.Children.Add(Hint(_vm!.Library.LoadFailed ? "TriggerTemplates.xml couldn't be read." : $"'{template}' isn't in TriggerTemplates.xml."));
    }

    /// <summary>The template's &lt;Using&gt; variables, and a button to add the missing ones.</summary>
    private Control Required(List<TriggerTemplateLibrary.UsingVar> uses)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        if (uses.Count == 0) return stack;
        stack.Children.Add(new TextBlock { Text = "Required Variables", FontWeight = FontWeight.SemiBold });
        stack.Children.Add(Hint("The template reads these from the Variables panel. Add the missing ones, then set their values there."));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,90,110,*") };
        void Cell(string text, int row, int col, bool bold = false, bool missing = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 11, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
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
            string status = have == null ? "Missing" : have.type != u.vtype && !string.IsNullOrEmpty(u.type) ? $"Added (as {have.type})" : "Added";
            if (have == null) missingCount++;
            Cell(u.name, r, 0); Cell(u.vtype.ToString(), r, 1); Cell(string.IsNullOrEmpty(u.def) ? "(empty)" : u.def, r, 2); Cell(status, r, 3, missing: have == null);
            r++;
        }
        stack.Children.Add(grid);
        if (missingCount > 0)
        {
            var add = new Button { Content = $"Add Missing Variables ({missingCount})", Margin = new Thickness(0, 6, 0, 0) };
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
            string title = E.kind == EvKind.Block ? "Template   " + E.template : E.kind == EvKind.Raw ? "Kept as written" : TriggerCatalog.FindEvent(E.id)?.title ?? E.id;
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
            var item = new MenuItem { Header = def.menu, IsEnabled = !used };
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
            var match = new ComboBox { ItemsSource = new[] { "All of these", "Any of these" }, SelectedIndex = L.rootOp == "Or" ? 1 : 0, Width = 140 };
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
                    card = Card("Compare", CardButtons(list, idx));
                    card.Children.Add(ParamRow(CheckDef, C.a, CheckDef.type));
                    var cmp = new ComboBox { ItemsSource = CompareLabels, SelectedIndex = (int)C.cmp * 2 + (C.not ? 1 : 0), Width = 170 };
                    cmp.SelectionChanged += (_, _) =>
                    {
                        if (cmp.SelectedIndex < 0) return;
                        C.cmp = (Cmp)(cmp.SelectedIndex / 2);
                        C.not = cmp.SelectedIndex % 2 == 1;
                        Touch();
                    };
                    card.Children.Add(LabeledRow(string.Empty, cmp));
                    card.Children.Add(ParamRow(AgainstDef, C.b, AgainstDef.type));
                    break;
                }
                case CondKind.Group:
                {
                    var op = new ComboBox { ItemsSource = new[] { "All of these", "Any of these" }, SelectedIndex = C.op == "Or" ? 1 : 0, Width = 140 };
                    op.SelectionChanged += (_, _) => { C.op = op.SelectedIndex == 1 ? "Or" : "And"; Touch(); };
                    card = Card("Group", CardButtons(list, idx), op);
                    BuildCondList(card, C.children);
                    card.Children.Add(AddButton("Add To Group", CondMenu(C.children)));
                    break;
                }
                case CondKind.Select:
                {
                    card = Card("Look through a list", CardButtons(list, idx));
                    card.Children.Add(ParamRow(SelObjectDef, C.selObject, SelObjectDef.type));
                    card.Children.Add(ParamRow(SelFromDef, C.selFrom, SelFromDef.type));
                    var none = new CheckBox { Content = "None of them may match", IsChecked = C.not };
                    none.IsCheckedChanged += (_, _) => { C.not = none.IsChecked == true; Touch(); };
                    card.Children.Add(none);
                    BuildCondList(card, C.children);
                    card.Children.Add(AddButton("Add To List Test", CondMenu(C.children)));
                    break;
                }
                case CondKind.Block:
                    card = Card("Template   " + C.template, CardButtons(list, idx));
                    card.Children.Add(BlockRef(C.template, C.prefix, s => C.template = s, s => C.prefix = s, TriggerTemplateLibrary.Section.OnlyIf));
                    break;
                default:
                    card = Card("Kept as written", CardButtons(list, idx));
                    card.Children.Add(RawBox(C.raw, s => { C.raw = s; Touch(); }));
                    break;
            }
            host.Children.Add(WrapCard(card));
        }
    }

    private MenuFlyout CondMenu(List<TCond> list)
    {
        var menu = new MenuFlyout();
        void Add(string header, Func<TCond> make)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => { list.Add(make()); Changed(); };
            menu.Items.Add(item);
        }
        Add("Compare two values", () => new TCond { kind = CondKind.Compare });
        Add("Group", () => new TCond { kind = CondKind.Group });
        Add("Look through a list", () => new TCond { kind = CondKind.Select, selFrom = TValue.Literal("Equipped") });
        menu.Items.Add(new Separator());

        var quick = new MenuItem { Header = "Quick" };
        void Quick(string header, string a, string b, bool not)
        {
            var item = new MenuItem { Header = header };
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
                    var order = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, Array.IndexOf(values, A.order)), Width = 170 };
                    order.SelectionChanged += (_, _) => { A.order = values[Math.Max(0, order.SelectedIndex)]; Changed(); };
                    var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { order } };
                    if (A.order == "Random")
                    {
                        header.Children.Add(new TextBlock { Text = "How many" });
                        var set = new NumericUpDown { Value = Math.Max(1, A.set), Minimum = 1, Increment = 1, FormatString = "0", Width = 90 };
                        set.ValueChanged += (_, _) => { A.set = Math.Max(1, (int)(set.Value ?? 1)); Touch(); };
                        header.Children.Add(set);
                    }
                    card = Card("Run Order", CardButtons(list, idx), header);
                    BuildActList(card, A.children);
                    card.Children.Add(AddButton("Add To Group", ActMenu(A.children)));
                    break;
                }
                case ActKind.Block:
                    card = Card("Template   " + A.template, CardButtons(list, idx));
                    card.Children.Add(BlockRef(A.template, A.prefix, s => A.template = s, s => A.prefix = s, TriggerTemplateLibrary.Section.Do));
                    break;
                case ActKind.Raw:
                    card = Card("Kept as written", CardButtons(list, idx));
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
            var item = new MenuItem { Header = parts[^1] };
            var d = def;
            item.Click += (_, _) => { list.Add(TriggerEditorViewModel.NewAction(d)); Changed(); };
            if (parts.Length > 1)
            {
                if (!groups.TryGetValue(parts[0], out var group))
                {
                    group = new MenuItem { Header = parts[0] };
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
        var choose = new MenuItem { Header = "Run Order Group" };
        choose.Click += (_, _) => { list.Add(new TAct { kind = ActKind.Choose, order = "Straight" }); Changed(); };
        if (groups.TryGetValue("Flow", out var flow)) flow.Items.Add(choose);
        else menu.Items.Add(choose);

        menu.Items.Add(new Separator());
        menu.Items.Add(TemplateItems("From Template", l => l.hasActions, full => { list.Add(new TAct { kind = ActKind.Block, template = full }); Changed(); }));
        return menu;
    }

    private MenuItem TemplateItems(string header, Func<TriggerTemplateLibrary.LoopInfo, bool> filter, Action<string> pick)
    {
        var root = new MenuItem { Header = header };
        var lib = _vm!.Library;
        if (!lib.Available)
        {
            root.Items.Add(new MenuItem { Header = "TriggerTemplates.xml not found", IsEnabled = false });
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
                var item = new MenuItem { Header = string.IsNullOrEmpty(l.name) ? "(unnamed)" : l.name };
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
        row.Children.Add(new TextBlock { Text = "Template" });
        var tpl = TextWithPicker(template, lib.Templates.SelectMany(t => t.loops.Select(l => l.Full)).ToList(), s => { setTemplate(s); Changed(); }, rebuildOnType: true, groupByDot: true);
        Grid.SetColumn(tpl, 1);
        row.Children.Add(tpl);
        var pl = new TextBlock { Text = "Prefix", Margin = new Thickness(8, 0, 4, 0) };
        ToolTip.SetTip(pl, "Makes the template read 1Sound instead of Sound, for example.");
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
                stack.Children.Add(Hint(lib.LoadFailed ? "TriggerTemplates.xml couldn't be read." : $"'{template}' isn't in TriggerTemplates.xml."));
            return stack;
        }
        var lines = info.Lines(section);
        if (lines.Count > 0)
            stack.Children.Add(new Border { Classes = { "card" }, Child = Summary(section == TriggerTemplateLibrary.Section.When ? "When" : section == TriggerTemplateLibrary.Section.OnlyIf ? "Only If" : "Do", lines) });
        stack.Children.Add(Required(lib.CollectUses(new[] { info }, prefix)));
        return stack;
    }

    // ================= VALUE EDITING ================= //

    private Control ParamRow(ParamDef pd, TValue v, VType lit)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*"), Margin = new Thickness(0, 2) };
        var label = new TextBlock { Text = pd.label, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, string.IsNullOrEmpty(pd.hint) ? pd.label : pd.hint);
        grid.Children.Add(label);
        var editor = ValueEditor(pd, v, lit);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static string ModeLabel(ValueMode m, VType lit) => m switch
    {
        ValueMode.Variable => "Variable",
        ValueMode.ModelProp => "Model",
        ValueMode.NodePos => "Node",
        _ => VTypes.SlotLabel(lit),
    };

    /// <summary>Mode dropdown (literal / variable / model property / node position) plus the editor for that mode.</summary>
    private Control ValueEditor(ParamDef pd, TValue v, VType lit)
    {
        // Name slots are a plain variable name, written without the "_".
        if (pd.varName)
            return TextWithPicker(v.text, Data.VarNames(), s => { v.mode = ValueMode.Literal; v.text = s; Changed(); }, rebuildOnType: true);

        var modes = new List<ValueMode> { ValueMode.Literal, ValueMode.Variable };
        if (pd.query) { modes.Add(ValueMode.ModelProp); modes.Add(ValueMode.NodePos); }
        if (!modes.Contains(v.mode)) modes.Add(v.mode); // a file may use a query where none is offered: keep it

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("96,*") };
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
                editor = TextWithPicker(v.varName, VarChoices(pd, lit), s => { v.varName = s; Touch(); });
                break;
            case ValueMode.ModelProp:
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                row.Children.Add(RefEditor(v, false));
                var prop = new ComboBox { ItemsSource = TriggerCatalog.ModelProps, SelectedIndex = Math.Max(0, Array.IndexOf(TriggerCatalog.ModelProps, v.prop)), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 0, 0) };
                prop.SelectionChanged += (_, _) => { if (prop.SelectedItem is string s) { v.prop = s; Touch(); } };
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

    /// <summary>Variables whose type fits the slot first; the others under "Other Types".</summary>
    private List<string> VarChoices(ParamDef? pd, VType lit)
    {
        bool all = pd == null || pd.query || pd.varName;
        var fam = VTypes.FamilyOf(lit);
        var names = Data.VarNames();
        var fits = names.Where(n => all || VTypes.FamilyOf(Data.TypeOf(n)) == fam);
        var others = names.Where(n => !(all || VTypes.FamilyOf(Data.TypeOf(n)) == fam)).Select(n => "Other Types/" + n);
        return fits.Concat(others).ToList();
    }

    /// <summary>Model or node part of a ?getModel query; a leading "_" means a variable.</summary>
    private Control RefEditor(TValue v, bool nodeSlot)
    {
        string shown = nodeSlot ? v.NodeRef : v.ModelRef;
        IList<string> literals = nodeSlot ? TriggerCatalog.CommonNodes : _vm!.Models.Count > 0 ? _vm.Models : TriggerCatalog.CommonModels;
        var choices = literals.Select(n => "Name/" + n).Concat(Data.VarNames().Select(n => "Variable/" + n)).ToList();
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
        }, rebuildOnPick: true);
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
                return StrictCombo(val, _vm!.ModelChoices, set);
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

    // ================= SMALL BUILDERS ================= //

    private static TextBlock Hint(string text) => new() { Text = text, Classes = { "hint" } };

    private static Control BoolEditor(bool value, Action<bool> set, string onText, string offText)
    {
        var box = new CheckBox { IsChecked = value, Content = value ? onText : offText };
        box.IsCheckedChanged += (_, _) =>
        {
            bool on = box.IsChecked == true;
            box.Content = on ? onText : offText;
            set(on);
        };
        return box;
    }

    private static TextBox Text(string value, Action<string> set)
    {
        var box = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
        return box;
    }

    private Control RawBox(string value, Action<string> set)
    {
        var box = new TextBox
        {
            Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 60,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 11,
        };
        box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
        return box;
    }

    /// <summary>Dropdown limited to the options; an unknown value from a file is shown as "(not found)" and kept.</summary>
    private static Control StrictCombo(string value, IList<string> options, Action<string> set)
    {
        var shown = options.ToList();
        int index = shown.IndexOf(value);
        if (index < 0 && !string.IsNullOrEmpty(value))
        {
            shown.Add(value + "  (not found)");
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

    /// <summary>Presets plus "Custom" with a number field (AI, Zoom).</summary>
    private Control ChoiceWithCustom(string[] labels, string[] values, string value, Action<string> set, Func<bool> getCustom, Action<bool> setCustom, bool integer)
    {
        int matched = Array.FindIndex(values, x => integer ? x == value : VTypes.TryFloat(x, out var a) && VTypes.TryFloat(value, out var b) && Math.Abs(a - b) < 0.0001f);
        bool custom = getCustom() || matched < 0;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(custom ? "Auto,*" : "*") };
        var combo = new ComboBox { ItemsSource = labels, SelectedIndex = custom ? labels.Length - 1 : matched, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 100 };
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
    private Control TextWithPicker(string value, IList<string> choices, Action<string> set, bool rebuildOnType = false,
        bool groupByDot = false, string? emptyText = null, bool groupAlphabetically = false, bool rebuildOnPick = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox { Text = value ?? string.Empty };
        if (rebuildOnType)
            box.LostFocus += (_, _) => { if (box.Text != value) set(box.Text ?? string.Empty); };
        else
            box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
        grid.Children.Add(box);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        var menu = new MenuFlyout();
        if (choices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = emptyText ?? "Nothing to pick", IsEnabled = false });
        }
        else
        {
            var groups = new Dictionary<string, MenuItem>();
            bool alphabet = groupAlphabetically && choices.Count > 30;
            foreach (var choice in choices)
            {
                string label = choice;
                string? group = null;
                if (groupByDot && choice.IndexOf('.') is int dot && dot > 0) { group = choice[..dot]; label = choice[(dot + 1)..]; }
                else if (choice.IndexOf('/') is int slash && slash > 0) { group = choice[..slash]; label = choice[(slash + 1)..]; }
                else if (alphabet) group = char.IsLetter(choice[0]) ? char.ToUpperInvariant(choice[0]).ToString() : "#";

                var item = new MenuItem { Header = string.IsNullOrEmpty(label) ? "(unnamed)" : label };
                string picked = choice.StartsWith("Other Types/", StringComparison.Ordinal) ? choice["Other Types/".Length..] : choice;
                item.Click += (_, _) =>
                {
                    if (!rebuildOnPick && !groupByDot)
                        box.Text = picked;
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
        box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
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

        var play = IconButton("Icon.Play", "Preview this animation on the character", () => Preview(box.Text ?? string.Empty));
        play.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(play, 1);
        grid.Children.Add(play);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        ToolTip.SetTip(button, "Pick a move: it opens in the preview, Apply uses it");
        var menu = new MenuFlyout();
        var choices = _vm!.Animations;
        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No moves found in the project's moves.xml", IsEnabled = false });
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
        box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
        grid.Children.Add(box);

        var button = new Button { Classes = { "tool" }, Padding = new Thickness(2), MinWidth = 22, Margin = new Thickness(2, 0, 0, 0), Content = new PathIcon { Data = IconGeometry("Icon.ChevronDown"), Width = 10, Height = 10 } };
        var menu = new MenuFlyout();
        var choices = _vm!.Sounds;
        if (choices.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No sounds found in the project's sounds folder", IsEnabled = false });
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
            ToolTip.SetTip(playIcon, "Play");
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
        grid.Children.Add(new TextBlock { Text = label });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    /// <summary>A section (When / Only If / Do). Returns the body panel; its parent Border is the section.</summary>
    private static StackPanel Block(string title, string description)
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = title, Classes = { "block-title" } });
        body.Children.Add(new TextBlock { Text = description, Classes = { "hint" } });
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
        titleRow.Children.Add(new TextBlock { Text = title, Classes = { "card-title" } });
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
        var up = IconButton("Icon.ArrowUp", "Move up", () => { var t = list[index]; list.RemoveAt(index); list.Insert(index - 1, t); Changed(); });
        up.IsEnabled = index > 0;
        var down = IconButton("Icon.ArrowDown", "Move down", () => { var t = list[index]; list.RemoveAt(index); list.Insert(index + 1, t); Changed(); });
        down.IsEnabled = index < list.Count - 1;
        var remove = IconButton("Icon.Close", "Remove", () => { list.RemoveAt(index); Changed(); });
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
                Children = { new PathIcon { Data = IconGeometry("Icon.Plus"), Width = 12, Height = 12 }, new TextBlock { Text = text } },
            },
        };
        return button;
    }
}
