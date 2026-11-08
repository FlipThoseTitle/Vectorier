using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

/// <summary>
/// The Trigger Editor's forms are built in code (like Unity's IMGUI window): every entry of a loop can have a
/// different set of parameters and value modes, so the panel is rebuilt whenever the structure changes, while
/// simple value edits write straight to the trigger model.
/// </summary>
public partial class TriggerEditorView : UserControl
{
    // Translations run longer than English, so the label and mode columns widen for other languages.
    private static double LabelWidth => Loc.CurrentCode == "en" ? 110 : 130;
    private static string ModeColumns => Loc.CurrentCode == "en" ? "96,*" : "118,*";
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
        if (_vm.IsTemplateList)
        {
            BuildTemplateList();
            return;
        }
        var offset = MainScroll.Offset;
        BuildLoopList();
        BuildVariables();
        BuildMain();
        Dispatcher.UIThread.Post(() => MainScroll.Offset = offset, DispatcherPriority.Background);
    }

    private TriggerData Data => _vm!.Data;

    private void Touch() => _vm?.Touch();

    private void Changed() => _vm?.Changed();
}
