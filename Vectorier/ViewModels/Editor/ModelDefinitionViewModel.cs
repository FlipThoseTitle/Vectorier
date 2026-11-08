using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>A toggleable choice of a multi-select dropdown (ForceBlasts, Murders, Arrests).</summary>
public sealed partial class ModelChoice : ObservableObject
{
    private readonly Action<ModelChoice, bool> _toggled;
    private bool _isChecked;

    public ModelChoice(string name, bool isChecked, Action<ModelChoice, bool> toggled)
    {
        Name = name;
        _isChecked = isChecked;
        _toggled = toggled;
    }

    public string Name { get; }
    public bool IsNone => Name == ModelDefinitionViewModel.NoneChoice;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            _toggled(this, value);
        }
    }

    /// <summary>Updates the check without writing the attribute.</summary>
    public void Sync(bool value)
    {
        if (_isChecked == value) return;
        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
    }
}

/// <summary>
/// One &lt;Model&gt; of the level's Models block, edited through friendlier controls (Type names, color picker,
/// dropdowns of the level's spawns and models). The XML attributes are written exactly as before; attributes the
/// editor does not know are kept.
/// </summary>
public sealed partial class ModelDefinitionViewModel : ObservableObject
{
    public const string NoneChoice = "None";
    private bool _aiCustom;
    private bool _stocksCustom;

    public ModelDefinitionViewModel(LevelModelDefinition model, LevelSettingsViewModel? owner = null, bool hunter = false)
    {
        Model = model;
        Owner = owner;
        IsHunter = hunter;
        _aiCustom = AI > 1;
        _stocksCustom = !string.IsNullOrEmpty(Stocks) && Stocks != "bike";
    }

    public LevelModelDefinition Model { get; }
    public LevelSettingsViewModel? Owner { get; set; }
    public bool IsHunter { get; set; }

    private string Get(string key) => Model.Get(key);

    private void Set(string key, string? value, bool keepEmpty = false, [CallerMemberName] string? property = null)
    {
        if (keepEmpty && value != null)
        {
            int index = Model.Attributes.FindIndex(a => a.Key == key);
            if (index >= 0) Model.Attributes[index] = new KeyValuePair<string, string>(key, value);
            else Model.Attributes.Add(new KeyValuePair<string, string>(key, value));
        }
        else
        {
            Model.Set(key, value);
        }
        OnPropertyChanged(property);
    }

    private bool GetFlag(string key) => Get(key) == "1";
    private void SetFlag(string key, bool value, [CallerMemberName] string? property = null) => Set(key, value ? "1" : null, property: property);

    public string Name
    {
        get => Get("Name");
        set
        {
            Set("Name", value, keepEmpty: true);
            OnPropertyChanged(nameof(Summary));
            Owner?.OnModelRenamed();
        }
    }

    public int Type { get => Num.ParseInt(Get("Type")); set => Set("Type", value.ToString(), keepEmpty: true); }

    /// <summary>Type shown by name: 0 is AI, 1 is Player.</summary>
    public static IReadOnlyList<string> TypeNames => new[] { Loc.T("AI (0)"), Loc.T("Player (1)") };

    public int TypeIndex
    {
        get => Math.Clamp(Type, 0, 1);
        set
        {
            if (value < 0) return;
            Type = value;
            OnPropertyChanged();
        }
    }

    public string Color { get => Get("Color"); set => Set("Color", value, keepEmpty: true); }

    /// <summary>"0" is black; other colors are written as RRGGBB like the Unity model editor.</summary>
    public Avalonia.Media.Color ColorValue
    {
        get
        {
            string hex = Color.Trim().TrimStart('#');
            if (hex.Length == 0 || hex == "0")
                return Avalonia.Media.Colors.Black;
            return Avalonia.Media.Color.TryParse("#" + hex, out var c) ? c : Avalonia.Media.Colors.Black;
        }
        set
        {
            Color = value.R == 0 && value.G == 0 && value.B == 0 ? "0" : $"{value.R:X2}{value.G:X2}{value.B:X2}";
            OnPropertyChanged();
        }
    }

    public string BirthSpawn { get => Get("BirthSpawn"); set { if (value != null) Set("BirthSpawn", value, keepEmpty: true); } }

    /// <summary>Spawns of the level (each name once), "None" when there are none; the current value is always listed.</summary>
    public IReadOnlyList<string> BirthSpawnChoices
    {
        get
        {
            var list = (Owner?.SpawnNames ?? new List<string>()).ToList();
            if (list.Count == 0)
                list.Add(NoneChoice);
            if (!string.IsNullOrEmpty(BirthSpawn) && !list.Contains(BirthSpawn))
                list.Add(BirthSpawn);
            return list;
        }
    }

    public int AI
    {
        get => Num.ParseInt(Get("AI"));
        set => Set("AI", Math.Max(0, value).ToString(), keepEmpty: true);
    }

    public static IReadOnlyList<string> AiModes => new[] { Loc.T("Player (0)"), Loc.T("Hunter (1)"), Loc.T("Custom") };

    public int AiMode
    {
        get => _aiCustom || AI > 1 ? 2 : Math.Max(0, AI);
        set
        {
            if (value < 0) return;
            _aiCustom = value == 2;
            if (value < 2) AI = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAiCustom));
        }
    }

    public bool IsAiCustom => AiMode == 2;

    public double Time { get => Num.Parse(Get("Time")); set => Set("Time", Num.Format(Math.Max(0, value)), keepEmpty: true); }
    public string Respawns { get => Get("Respawns"); set => Set("Respawns", value); }
    public string AllowedSpawns { get => Get("AllowedSpawns"); set => Set("AllowedSpawns", value); }

    public string Skins { get => Get("Skins"); set => Set("Skins", value); }

    public string ForceBlasts { get => Get("ForceBlasts"); set { Set("ForceBlasts", value); OnPropertyChanged(nameof(ForceBlastsText)); } }
    public string Murders { get => Get("Murders"); set { Set("Murders", value); OnPropertyChanged(nameof(MurdersText)); } }
    public string Arrests { get => Get("Arrests"); set { Set("Arrests", value); OnPropertyChanged(nameof(ArrestsText)); } }

    public string ForceBlastsText => Display(ForceBlasts);
    public string MurdersText => Display(Murders);
    public string ArrestsText => Display(Arrests);

    private static string Display(string value) => string.IsNullOrEmpty(value) ? NoneChoice : value.Replace("|", ", ");

    [AllowNull]
    public IReadOnlyList<ModelChoice> ForceBlastChoices { get => field ??= BuildChoices(() => ForceBlasts, v => ForceBlasts = v ?? string.Empty); private set; }

    [AllowNull]
    public IReadOnlyList<ModelChoice> MurderChoices { get => field ??= BuildChoices(() => Murders, v => Murders = v ?? string.Empty); private set; }

    [AllowNull]
    public IReadOnlyList<ModelChoice> ArrestChoices { get => field ??= BuildChoices(() => Arrests, v => Arrests = v ?? string.Empty); private set; }

    /// <summary>The model lists of the mode changed (added, removed, renamed): rebuild the dropdowns.</summary>
    public void RefreshChoices()
    {
        ForceBlastChoices = MurderChoices = ArrestChoices = null;
        OnPropertyChanged(nameof(ForceBlastChoices));
        OnPropertyChanged(nameof(MurderChoices));
        OnPropertyChanged(nameof(ArrestChoices));
        OnPropertyChanged(nameof(BirthSpawnChoices));
    }

    /// <summary>"None" first, then every model of the current mode (and any value already written that is not one).</summary>
    private IReadOnlyList<ModelChoice> BuildChoices(Func<string> get, Action<string?> set)
    {
        var selected = get().Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        var names = (Owner?.ModelNames(IsHunter) ?? new List<string>()).ToList();
        foreach (var extra in selected)
            if (!names.Contains(extra))
                names.Add(extra);

        var list = new List<ModelChoice>();
        void Toggled(ModelChoice choice, bool on)
        {
            if (choice.IsNone)
            {
                // None clears every other selection (the attribute is not written).
                if (on)
                    foreach (var c in list) c.Sync(c.IsNone);
            }
            else
            {
                list[0].Sync(false);
            }
            var picked = list.Where(c => !c.IsNone && c.IsChecked).Select(c => c.Name).ToList();
            if (picked.Count == 0)
                list[0].Sync(true);
            set(picked.Count == 0 ? null : string.Join("|", picked));
        }
        list.Add(new ModelChoice(NoneChoice, selected.Count == 0, Toggled));
        foreach (var name in names)
            list.Add(new ModelChoice(name, selected.Contains(name), Toggled));
        return list;
    }

    public string Stocks { get => Get("Stocks"); set => Set("Stocks", value); }

    public static IReadOnlyList<string> StockModes { get; } = new[] { "None", "bike", "Custom" };

    public int StocksMode
    {
        get => _stocksCustom ? 2 : string.IsNullOrEmpty(Stocks) ? 0 : Stocks == "bike" ? 1 : 2;
        set
        {
            if (value < 0) return;
            _stocksCustom = value == 2;
            if (value == 0) Stocks = string.Empty;
            else if (value == 1) Stocks = "bike";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStocksCustom));
        }
    }

    public bool IsStocksCustom => StocksMode == 2;

    public int LifeTime { get => Num.ParseInt(Get("LifeTime")); set => Set("LifeTime", value <= 0 ? null : value.ToString()); }
    public bool Trick { get => GetFlag("Trick"); set => SetFlag("Trick", value); }
    public bool Item { get => GetFlag("Item"); set => SetFlag("Item", value); }
    public bool Victory { get => GetFlag("Victory"); set => SetFlag("Victory", value); }
    public bool Lose { get => GetFlag("Lose"); set => SetFlag("Lose", value); }
    public bool Icon { get => GetFlag("Icon"); set => SetFlag("Icon", value); }

    public string Summary => string.IsNullOrEmpty(Name) ? Loc.T("(unnamed)") : Name;

    /// <summary>Plays run.bin in a loop on models/0.xml + this model's skins (1.xml when it has none).</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (Owner == null)
            return;
        await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Owner.Project, Loc.F("Preview - {0}", Summary),
            Services.Assets.CharacterModel.SkinsFrom(Skins),
            ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(Owner.Project, ViewModels.Preview.CharacterPreviewViewModel.RunAnimation));
    }

    [RelayCommand]
    private async Task AddSkinsAsync()
    {
        if (Owner == null)
            return;
        await AppServices.Dialogs.ShowAsync(new SkinPickerDialogViewModel(this, Owner.Project));
    }
}
