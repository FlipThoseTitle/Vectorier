using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.Editor;

/// <summary>An editable file name in a Sets list.</summary>
public sealed partial class SetEntry : ObservableObject
{
    public SetEntry(string file) => _file = file;

    [ObservableProperty] private string _file;

    /// <summary>Re-reads the value once the dropdown has its choices.</summary>
    public void Refresh() => OnPropertyChanged(nameof(File));
}

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

    private readonly LevelModelDefinition _model;
    private bool _aiCustom;
    private bool _stocksCustom;

    public ModelDefinitionViewModel(LevelModelDefinition model, LevelSettingsViewModel? owner = null, bool hunter = false)
    {
        _model = model;
        Owner = owner;
        IsHunter = hunter;
        _aiCustom = AI > 1;
        _stocksCustom = !string.IsNullOrEmpty(Stocks) && Stocks != "bike";
    }

    public LevelModelDefinition Model => _model;
    public LevelSettingsViewModel? Owner { get; set; }
    public bool IsHunter { get; set; }

    private string Get(string key) => _model.Get(key);

    private void Set(string key, string? value, bool keepEmpty = false, [CallerMemberName] string? property = null)
    {
        if (keepEmpty && value != null)
        {
            int index = _model.Attributes.FindIndex(a => a.Key == key);
            if (index >= 0) _model.Attributes[index] = new KeyValuePair<string, string>(key, value);
            else _model.Attributes.Add(new KeyValuePair<string, string>(key, value));
        }
        else
        {
            _model.Set(key, value);
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
    public static IReadOnlyList<string> TypeNames { get; } = new[] { "AI (0)", "Player (1)" };

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

    public static IReadOnlyList<string> AiModes { get; } = new[] { "Player (0)", "Hunter (1)", "Custom" };

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

    private IReadOnlyList<ModelChoice>? _forceBlastChoices, _murderChoices, _arrestChoices;
    public IReadOnlyList<ModelChoice> ForceBlastChoices => _forceBlastChoices ??= BuildChoices(() => ForceBlasts, v => ForceBlasts = v ?? string.Empty);
    public IReadOnlyList<ModelChoice> MurderChoices => _murderChoices ??= BuildChoices(() => Murders, v => Murders = v ?? string.Empty);
    public IReadOnlyList<ModelChoice> ArrestChoices => _arrestChoices ??= BuildChoices(() => Arrests, v => Arrests = v ?? string.Empty);

    /// <summary>The model lists of the mode changed (added, removed, renamed): rebuild the dropdowns.</summary>
    public void RefreshChoices()
    {
        _forceBlastChoices = _murderChoices = _arrestChoices = null;
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

    public string Summary => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;

    /// <summary>Plays run.bin in a loop on models/0.xml + this model's skins (1.xml when it has none).</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (Owner == null)
            return;
        await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Owner.Project, $"Preview - {Summary}",
            Services.Assets.CharacterModel.SkinsFrom(Skins),
            ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(Owner.Project, ViewModels.Preview.CharacterPreviewViewModel.RunAnimation),
            ViewModels.Preview.PreviewCamera.FixedOnPivot);
    }

    [RelayCommand]
    private async Task AddSkinsAsync()
    {
        if (Owner == null)
            return;
        await AppServices.Dialogs.ShowAsync(new SkinPickerDialogViewModel(this, Owner.Project));
    }
}

/// <summary>Level Settings window (Unity's ExportConfig without file/export options).</summary>
public sealed partial class LevelSettingsViewModel : DialogViewModel
{
    private readonly EditorViewModel _editor;
    private readonly LevelSettings _settings;

    public LevelSettingsViewModel(EditorViewModel editor, LevelSettings settings)
    {
        _editor = editor;
        _settings = settings;
        IsObjectMode = editor.IsEditingObject;
        Title = IsObjectMode ? $"Object Settings - {editor.ObjectEdit!.Name}" : $"Level Settings - {editor.Document.LevelName}";

        SpawnNames = editor.Document.AllElements().OfType<SpawnElement>().Select(sp => sp.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

        // City / Ground are automatic: the files of the objects used in the level.
        var (autoCity, autoGround) = editor.ComputeAutoSets();
        foreach (var f in autoCity) CitySets.Add(new SetEntry(f));
        foreach (var f in autoGround) GroundSets.Add(new SetEntry(f));
        foreach (var f in settings.LibrarySets) LibrarySets.Add(new SetEntry(f));
        _musicName = settings.MusicName;
        _musicVolume = settings.MusicVolume;
        _coinValue = settings.CoinValue;
        foreach (var m in settings.CommonModels) CommonModels.Add(new ModelDefinitionViewModel(m.Clone(), this, false));
        foreach (var m in settings.HunterModels) HunterModels.Add(new ModelDefinitionViewModel(m.Clone(), this, true));
        _selectedModel = CommonModels.FirstOrDefault();
        CommonModels.CollectionChanged += (_, _) => OnModelRenamed();
        HunterModels.CollectionChanged += (_, _) => OnModelRenamed();
        _ = LoadChoicesAsync();
        if (!IsObjectMode)
            _ = LoadThumbnailAsync();
        CloseRequested += StopMusicPreview;
    }

    // ================= MUSIC PREVIEW ================= //


    /// <summary>The music is playing in the preview.</summary>
    [ObservableProperty] private bool _isMusicPlaying;

    /// <summary>Fixed preview volume, independent of the level's music volume.</summary>
    private const double MusicPreviewVolume = 0.8;

    private int _playVersion;

    /// <summary>Plays the selected music (or stops it when it is playing).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleMusicPreview()
    {
        if (IsMusicPlaying)
        {
            StopMusicPreview();
            return;
        }
        string? name = MusicName?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        string folder = VPath.Combine(_editor.Project.Path, "music");
        string? path = (await AppServices.FileSystem.GetFilesAsync(folder))
            .FirstOrDefault(f => string.Equals(VPath.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            await AppServices.Dialogs.ShowMessageAsync("Music Preview", $"'{name}' was not found in the project's music folder.", MessageKind.Warning);
            return;
        }
        int version = ++_playVersion;
        IsMusicPlaying = true;
        try
        {
            await AppServices.Platform.PlaySoundAsync(AppServices.FileSystem, path, MusicPreviewVolume);
        }
        finally
        {
            if (version == _playVersion)
                IsMusicPlaying = false;
        }
    }

    private void StopMusicPreview()
    {
        _playVersion++;
        if (IsMusicPlaying)
            AppServices.Platform.StopSound();
        IsMusicPlaying = false;
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 700;

    public ProjectInfo Project => _editor.Project;

    /// <summary>Opened from Edit Object mode: only what applies to an object is shown (no thumbnail, music, coins, models).</summary>
    public bool IsObjectMode { get; }
    public bool IsLevelMode => !IsObjectMode;
    public bool HasCitySets => CitySets.Count > 0;
    public bool HasGroundSets => GroundSets.Count > 0;
    public string LevelName => _editor.Document.LevelName;

    public ObservableCollection<SetEntry> CitySets { get; } = new();
    public ObservableCollection<SetEntry> GroundSets { get; } = new();
    public ObservableCollection<SetEntry> LibrarySets { get; } = new();
    public ObservableCollection<string> XmlFiles { get; } = new();
    public ObservableCollection<string> MusicChoices { get; } = new();

    public ObservableCollection<ModelDefinitionViewModel> CommonModels { get; } = new();
    public ObservableCollection<ModelDefinitionViewModel> HunterModels { get; } = new();

    /// <summary>Spawn names of the level, each once.</summary>
    public IReadOnlyList<string> SpawnNames { get; }

    [ObservableProperty] private string _musicName;

    partial void OnMusicNameChanged(string value) => StopMusicPreview();
    [ObservableProperty] private double _musicVolume;
    [ObservableProperty] private int _coinValue;
    [ObservableProperty] private ModelDefinitionViewModel? _selectedModel;
    [ObservableProperty] private string? _fileToAdd;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    [ObservableProperty] private string _thumbnailStatus = string.Empty;

    /// <summary>The Models tab edits Hunter mode's models instead of the common ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentModels), nameof(ModeTitle))]
    private bool _isHunterMode;

    public ObservableCollection<ModelDefinitionViewModel> CurrentModels => IsHunterMode ? HunterModels : CommonModels;
    public string ModeTitle => IsHunterMode ? "Hunter Mode Models" : "Common Mode Models";

    partial void OnIsHunterModeChanged(bool value)
    {
        // The list must switch before the selection, or it would clear the new selection.
        OnPropertyChanged(nameof(CurrentModels));
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    partial void OnCoinValueChanged(int value)
    {
        if (value < 0) CoinValue = 0;
    }

    public List<string> ModelNames(bool hunter) =>
        (hunter ? HunterModels : CommonModels).Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

    public void OnModelRenamed()
    {
        foreach (var m in CommonModels.Concat(HunterModels))
            m.RefreshChoices();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var project = _editor.Project;
            foreach (var file in (await AppServices.FileSystem.GetFilesAsync(project.XmlRoot))
                         .Where(f => VPath.HasExtension(f, ".xml"))
                         .Select(VPath.GetFileName)
                         .Where(f => !string.Equals(f, "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                XmlFiles.Add(file);
            // Files already used stay selectable even when they are missing.
            foreach (var used in CitySets.Concat(GroundSets).Concat(LibrarySets).Select(e => e.File))
                if (!string.IsNullOrEmpty(used) && !XmlFiles.Contains(used))
                    XmlFiles.Add(used);
            foreach (var entry in CitySets.Concat(GroundSets).Concat(LibrarySets))
                entry.Refresh();

            string musicFolder = VPath.Combine(project.Path, "music");
            foreach (var file in (await AppServices.FileSystem.GetFilesAsync(musicFolder))
                         .Select(VPath.GetFileNameWithoutExtension).Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                MusicChoices.Add(file);
            if (!string.IsNullOrEmpty(MusicName) && !MusicChoices.Contains(MusicName))
                MusicChoices.Add(MusicName);
            OnPropertyChanged(nameof(MusicName));
        }
        catch
        {
            // Choices are a convenience.
        }
    }

    // ================= THUMBNAIL ================= //

    private LevelThumbnail Thumbnails => new(_editor.Project, LevelName);

    private async Task LoadThumbnailAsync()
    {
        try
        {
            Thumbnail = await Thumbnails.LoadAsync(512);
            ThumbnailStatus = Thumbnail == null ? "No thumbnail yet" : string.Empty;
        }
        catch
        {
            Thumbnail = null;
        }
    }

    [RelayCommand]
    private async Task PickThumbnailAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Level Thumbnail (512 × 340 recommended)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count == 0)
            return;
        try
        {
            await Thumbnails.ReplaceAsync(files[0]);
            await LoadThumbnailAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Thumbnail", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private async Task CaptureThumbnailAsync()
    {
        var capture = new ThumbnailCaptureViewModel(_editor);
        if (await AppServices.Dialogs.ShowAsync(capture) is true)
            await LoadThumbnailAsync();
    }

    // ================= SETS ================= //

    [RelayCommand]
    private void AddSet(string? list)
    {
        var target = list switch { "City" => CitySets, "Ground" => GroundSets, _ => LibrarySets };
        target.Add(new SetEntry(FileToAdd ?? (list == "City" ? "buildings.xml" : "objects.xml")));
    }

    [RelayCommand]
    private void RemoveSet(SetEntry? entry)
    {
        if (entry == null) return;
        CitySets.Remove(entry);
        GroundSets.Remove(entry);
        LibrarySets.Remove(entry);
    }

    [RelayCommand]
    private void MoveSetUp(SetEntry? entry) => MoveSet(entry, -1);

    [RelayCommand]
    private void MoveSetDown(SetEntry? entry) => MoveSet(entry, 1);

    private void MoveSet(SetEntry? entry, int delta)
    {
        if (entry == null) return;
        foreach (var list in new[] { CitySets, GroundSets, LibrarySets })
        {
            int i = list.IndexOf(entry);
            if (i < 0) continue;
            int j = Math.Clamp(i + delta, 0, list.Count - 1);
            if (i != j) list.Move(i, j);
            return;
        }
    }

    // ================= MODELS ================= //

    [RelayCommand]
    private void AddModel()
    {
        var model = new ModelDefinitionViewModel(new LevelModelDefinition
        {
            Attributes = new List<KeyValuePair<string, string>>
            {
                new("Name", "NewModel"), new("Type", "0"), new("Color", "0"),
                new("BirthSpawn", SpawnNames.FirstOrDefault() ?? "DefaultSpawn"), new("AI", "0"), new("Time", "0"),
            },
        }, this, IsHunterMode);
        CurrentModels.Add(model);
        SelectedModel = model;
    }

    [RelayCommand]
    private void RemoveModel()
    {
        if (SelectedModel != null)
            CurrentModels.Remove(SelectedModel);
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    [RelayCommand]
    private void MoveModel(string? direction)
    {
        var list = CurrentModels;
        if (SelectedModel == null) return;
        int i = list.IndexOf(SelectedModel);
        int j = Math.Clamp(i + (direction == "Up" ? -1 : 1), 0, list.Count - 1);
        if (i >= 0 && i != j)
        {
            var keep = SelectedModel;
            list.Move(i, j);
            SelectedModel = keep;
        }
    }

    /// <summary>Restores the default Player and Hunter of both modes, after a confirmation.</summary>
    [RelayCommand]
    private async Task ResetModelsAsync()
    {
        bool ok = await AppServices.Dialogs.ConfirmAsync("Default Models",
            "Replace the models of both Common Mode and Hunter Mode with the default Player and Hunter?\nEvery model you configured in this level is removed.",
            "Use Defaults", "Cancel", MessageKind.Warning);
        if (!ok)
            return;
        CommonModels.Clear();
        foreach (var m in LevelSettings.ParseModels(LevelSettings.DefaultCommonModels)) CommonModels.Add(new ModelDefinitionViewModel(m, this, false));
        HunterModels.Clear();
        foreach (var m in LevelSettings.ParseModels(LevelSettings.DefaultHunterModels)) HunterModels.Add(new ModelDefinitionViewModel(m, this, true));
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    [RelayCommand]
    private void Apply()
    {
        _settings.CitySets = CitySets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.GroundSets = GroundSets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.LibrarySets = LibrarySets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.MusicName = MusicName?.Trim() ?? string.Empty;
        _settings.MusicVolume = Math.Clamp(MusicVolume, 0, 1);
        _settings.CoinValue = Math.Max(0, CoinValue);
        _settings.CommonModels = CommonModels.Select(m => m.Model).ToList();
        _settings.HunterModels = HunterModels.Select(m => m.Model).ToList();
        Close(_settings);
    }

    [RelayCommand]
    private void CancelDialog() => Close(null);
}
