using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>A skin choice: a gear (with its shop image and display name) or a model XML file.</summary>
public sealed partial class SkinChoiceViewModel : ObservableObject
{
    private readonly SkinPickerDialogViewModel _owner;

    public SkinChoiceViewModel(SkinPickerDialogViewModel owner, string xmlName, string displayName)
    {
        _owner = owner;
        XmlName = xmlName;
        DisplayName = string.IsNullOrEmpty(displayName) ? xmlName : displayName;
    }

    /// <summary>Name written in Skins: the model XML file without its extension.</summary>
    public string XmlName { get; }
    public string DisplayName { get; }

    [ObservableProperty] private Bitmap? _image;

    public bool IsSelected
    {
        get => _owner.Contains(XmlName);
        set
        {
            _owner.Toggle(XmlName, value);
            OnPropertyChanged();
        }
    }

    public void Raise() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>
/// "Add Skins": pick gears (their model XML names) or model XML files; every click updates the model's Skins
/// right away (the field stays a "a|b" string).
/// </summary>
public sealed partial class SkinPickerDialogViewModel : DialogViewModel
{
    private readonly ModelDefinitionViewModel _model;
    private readonly ProjectInfo _project;

    public SkinPickerDialogViewModel(ModelDefinitionViewModel model, ProjectInfo project)
    {
        _model = model;
        _project = project;
        Title = Loc.F("Add Skins - {0}", model.Summary);
        _ = LoadAsync();
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 700;
    public override double PreferredHeight => 540;

    public ObservableCollection<SkinChoiceViewModel> Gears { get; } = new();
    public ObservableCollection<SkinChoiceViewModel> XmlFiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsXmlTab))]
    private bool _isGearsTab = true;

    public bool IsXmlTab
    {
        get => !IsGearsTab;
        set => IsGearsTab = !value;
    }

    [ObservableProperty] private bool _isLoading = true;

    public string SkinsText => string.IsNullOrEmpty(_model.Skins) ? Loc.T("(none)") : _model.Skins;

    private List<string> Current() => _model.Skins.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    public bool Contains(string name) => Current().Contains(name, StringComparer.OrdinalIgnoreCase);

    public void Toggle(string name, bool on)
    {
        var list = Current();
        int index = list.FindIndex(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        if (on && index < 0)
            list.Add(name);
        else if (!on && index >= 0)
            list.RemoveAt(index);
        else
            return;
        _model.Skins = string.Join("|", list);
        OnPropertyChanged(nameof(SkinsText));
        // The same XML can be listed in both tabs.
        foreach (var c in Gears.Concat(XmlFiles).Where(c => string.Equals(c.XmlName, name, StringComparison.OrdinalIgnoreCase)))
            c.Raise();
    }

    private async Task LoadAsync()
    {
        try
        {
            var fs = AppServices.FileSystem;
            var shop = new ShopService(fs, _project);
            var localization = new LocalizationService(fs, _project);
            await shop.LoadAsync();
            await localization.LoadAsync();
            var gears = shop.GetItems(ShopService.ClothingGroup);
            foreach (var gear in gears)
                Gears.Add(new SkinChoiceViewModel(this, gear.Name, localization.Get(gear.Name) ?? string.Empty));

            var files = await fs.GetFilesAsync(_project.ModelsFolder);
            foreach (var name in files.Where(f => VPath.HasExtension(f, ".xml")).Select(VPath.GetFileNameWithoutExtension)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                XmlFiles.Add(new SkinChoiceViewModel(this, name, name));

            IsLoading = false;
            string folder = VPath.Combine(_project.Path, "icons", "shop");
            for (int i = 0; i < gears.Count; i++)
            {
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
                {
                    string path = VPath.Combine(folder, gears[i].ShopImage + ext);
                    if (string.IsNullOrEmpty(gears[i].ShopImage) || !await fs.FileExistsAsync(path)) continue;
                    try { Gears[i].Image = await ImageLoader.LoadAsync(fs, path, 160); }
                    catch { /* "No Image" */ }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            IsLoading = false;
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Add Skins"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private void Done() => Close(true);
}
