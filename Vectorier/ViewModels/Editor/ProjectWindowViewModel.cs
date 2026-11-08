using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// The Project Window: a folder list on the left and the selected folder's content on the right. What it shows
/// depends on the element type chosen in the Element Bar.
/// </summary>
public sealed partial class ProjectWindowViewModel : ObservableObject
{
    public const string TexturesKey = "Textures";
    public const string TriggersKey = "Triggers";
    public const string AreasKey = "Areas";
    public const string ModelsKey = "Models";

    private readonly EditorViewModel _editor;
    private int _loadVersion;
    private int _reloadVersion;
    private readonly List<PwItem> _allItems = new();

    public ProjectWindowViewModel(EditorViewModel editor)
    {
        _editor = editor;
    }

    private ProjectContext Assets => _editor.Context;
    private EditorCache Cache => Assets.EditorCache;

    public TextureLibrary Textures => Assets.Textures;
    public ModelLibrary ModelAssets => Assets.Models;

    /// <summary>Folders shown on the left (filtered by <see cref="FolderSearchText"/>).</summary>
    public ObservableCollection<PwFolder> Folders { get; } = new();
    public ObservableCollection<PwItem> Items { get; } = new();

    /// <summary>Every folder of the context, also the ones hidden by the left search.</summary>
    private readonly List<PwFolder> _allFolders = new();
    public IReadOnlyList<PwFolder> AllFolders => _allFolders;

    private readonly List<PwItem> _selectedItems = new();
    private readonly List<PwFolder> _selectedFolders = new();
    private PwItem? _itemAnchor;
    private PwFolder? _folderAnchor;
    private bool _selectingItems;
    private bool _selectingFolders;

    /// <summary>Selected tiles (several with Ctrl / Shift: for organizing only, never placed).</summary>
    public IReadOnlyList<PwItem> SelectedItems => _selectedItems;

    /// <summary>Selected folders / Object XML files (several with Ctrl / Shift).</summary>
    public IReadOnlyList<PwFolder> SelectedFolders => _selectedFolders;

    public bool HasMultipleItems => _selectedItems.Count > 1;
    public bool HasMultipleFolders => _selectedFolders.Count > 1;

    /// <summary>Where the last click in the Project Window was (Delete acts there).</summary>
    public bool LastClickInFolders { get; set; }

    [ObservableProperty] private string _folderSearchText = string.Empty;

    partial void OnFolderSearchTextChanged(string value) => ApplyFolderFilter();

    // ================= KEYBOARD/SELECTION HELPERS ================= //

    public void Select(PwItem? item) => SelectedItem = item;

    /// <summary>Textures, stored Triggers and Areas, object definitions and model XML files can be deleted.</summary>
    public bool CanDelete(PwItem item) =>
        item.Texture != null || item.Trigger != null || item.Area != null || item.ModelName != null || (item.Definition != null && item.PlatformSurface == null);

    /// <summary>Delete key in the Project Window: the selected files (left panel) or the selected items (right panel).</summary>
    public async Task DeleteSelectionAsync()
    {
        if (LastClickInFolders)
        {
            if (_selectedFolders.Count > 0)
                await DeleteFoldersAsync(_selectedFolders.ToList());
            return;
        }
        var items = _selectedItems.Where(CanDelete).ToList();
        if (items.Count > 0)
            await DeleteItemsAsync(items);
    }
}
