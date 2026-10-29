using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vectorier.Services.IO;

namespace Vectorier.Services.Projects;

/// <summary>
/// Level bookkeeping shared by the Levels and Locations pages: keeps internal names in the
/// LOCATION_MODE_NN convention and moves the level's XML, thumbnail and localization key along with it.
/// </summary>
public sealed class LevelOperations
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    private readonly IFileSystem _fs;
    private readonly ProjectInfo _project;
    private readonly LocationListService _list;
    private readonly LocalizationService _localization;

    public LevelOperations(IFileSystem fs, ProjectInfo project, LocationListService list, LocalizationService localization)
    {
        _fs = fs;
        _project = project;
        _list = list;
        _localization = localization;
    }

    public string StoriesIconFolder => VPath.Combine(_project.Path, "icons", "stories");
    public string LocationsIconFolder => VPath.Combine(_project.Path, "icons", "locations");

    public string XmlPathFor(string levelName) => VPath.Combine(_project.LevelsFolder, levelName + ".xml");

    public async Task<string?> FindThumbnailAsync(string folder, string name)
    {
        foreach (var ext in ImageExtensions)
        {
            string path = VPath.Combine(folder, name + ext);
            if (await _fs.FileExistsAsync(path))
                return path;
        }
        return null;
    }

    public async Task DeleteImagesAsync(string folder, string name)
    {
        foreach (var ext in ImageExtensions)
            await _fs.DeleteFileAsync(VPath.Combine(folder, name + ext));
    }

    public async Task SaveAllAsync()
    {
        await _list.SaveAsync();
        await _localization.SaveAsync();
    }

    public int LocationOrder(string location) => Math.Max(1, _list.Locations.FindIndex(l => l.Name == location) + 1);

    /// <summary>Adds a new level at the end of a location's group. No XML file is created.</summary>
    public LevelData CreateLevel(LocationData location, string mode)
    {
        var group = location.GetGroup(mode);
        int index = group.Count;
        string name = LocationListService.MakeLevelName(location.Name, mode, index);
        string starsCommon = _list.StarsTemplates.FirstOrDefault()?.Name ?? string.Empty;
        string starsHunter = _list.StarsTemplates.FirstOrDefault(t => t.Name.Contains("Hunter", StringComparison.OrdinalIgnoreCase))?.Name ?? starsCommon;
        string reward = _list.RewardTemplates.FirstOrDefault()?.Name ?? string.Empty;

        var level = new LevelData
        {
            Name = name,
            Location = location.Name,
            Mode = mode,
            Common = new TrackData { Name = name, StarsTemplate = starsCommon, RewardTemplate = reward },
            Hunter = new TrackData { Name = name + LocationListService.HunterSuffix, StarsTemplate = starsHunter, RewardTemplate = reward },
        };
        group.Add(level);
        _localization.Set(name, LocationListService.MakeDefaultDisplayName(mode, LocationOrder(location.Name), index));
        return level;
    }

    public async Task DeleteLevelAsync(LevelData level)
    {
        var location = _list.FindLocation(level.Location);
        if (location == null)
            return;

        await _fs.DeleteFileAsync(XmlPathFor(level.Name));
        await DeleteImagesAsync(StoriesIconFolder, level.Name);
        _localization.Remove(level.Name);
        location.GetGroup(level.Mode).Remove(level);
        await RenumberAsync(location, level.Mode);
    }

    public async Task<LevelData> DuplicateLevelAsync(LevelData source)
    {
        var location = _list.FindLocation(source.Location) ?? throw new InvalidOperationException("The level has no location.");
        var copy = CreateLevel(location, source.Mode);
        var common = source.Common.Clone();
        common.Name = copy.Name;
        var hunter = source.Hunter.Clone();
        hunter.Name = copy.Name + LocationListService.HunterSuffix;
        copy.Common = common;
        copy.Hunter = hunter;

        string? display = _localization.Get(source.Name);
        if (!string.IsNullOrEmpty(display))
            _localization.Set(copy.Name, display + " (Copy)");

        if (await _fs.FileExistsAsync(XmlPathFor(source.Name)))
            await _fs.CopyFileAsync(XmlPathFor(source.Name), XmlPathFor(copy.Name));

        string? thumb = await FindThumbnailAsync(StoriesIconFolder, source.Name);
        if (thumb != null)
            await _fs.CopyFileAsync(thumb, VPath.Combine(StoriesIconFolder, copy.Name + VPath.GetExtension(thumb)));

        return copy;
    }

    /// <summary>Moves a level up or down inside its group and renames everything accordingly.</summary>
    public async Task MoveWithinGroupAsync(LevelData level, int direction)
    {
        var location = _list.FindLocation(level.Location);
        if (location == null)
            return;
        var group = location.GetGroup(level.Mode);
        int index = group.IndexOf(level);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= group.Count)
            return;
        group.RemoveAt(index);
        group.Insert(target, level);
        await RenumberAsync(location, level.Mode);
    }

    /// <summary>Moves a level into another location (end of the same mode group) or another mode.</summary>
    public async Task MoveToLocationAsync(LevelData level, LocationData target, string mode, int insertIndex = -1)
    {
        var source = _list.FindLocation(level.Location);
        if (source == null)
            return;

        source.GetGroup(level.Mode).Remove(level);
        string oldMode = level.Mode;
        level.Location = target.Name;
        level.Mode = mode;
        var group = target.GetGroup(mode);
        if (insertIndex < 0 || insertIndex > group.Count)
            group.Add(level);
        else
            group.Insert(insertIndex, level);

        await RenumberAsync(source, oldMode);
        await RenumberAsync(target, mode);
    }

    /// <summary>
    /// Re-applies LOCATION_MODE_NN names to a group. Files are renamed in two phases (through temporary
    /// names) so swapping two levels never overwrites anything.
    /// </summary>
    public async Task RenumberAsync(LocationData location, string mode)
    {
        var group = location.GetGroup(mode);
        var renames = new List<(LevelData Level, string Old, string New)>();
        for (int i = 0; i < group.Count; i++)
        {
            string expected = LocationListService.MakeLevelName(location.Name, mode, i);
            if (group[i].Name != expected)
                renames.Add((group[i], group[i].Name, expected));
        }
        if (renames.Count == 0)
            return;

        await RenameLevelsAsync(renames);
    }

    public async Task RenameLevelsAsync(List<(LevelData Level, string Old, string New)> renames)
    {
        string tempTag = "__vtmp_" + Guid.NewGuid().ToString("N")[..6];

        // Phase 1: move files and keys to temporary names.
        foreach (var (_, oldName, _) in renames)
        {
            await MoveIfExistsAsync(XmlPathFor(oldName), XmlPathFor(oldName + tempTag));
            string? thumb = await FindThumbnailAsync(StoriesIconFolder, oldName);
            if (thumb != null)
                await _fs.MoveFileAsync(thumb, VPath.Combine(StoriesIconFolder, oldName + tempTag + VPath.GetExtension(thumb)), true);
            _localization.Rename(oldName, oldName + tempTag);
        }

        // Phase 2: temporary → final names.
        foreach (var (level, oldName, newName) in renames)
        {
            await MoveIfExistsAsync(XmlPathFor(oldName + tempTag), XmlPathFor(newName));
            string? thumb = await FindThumbnailAsync(StoriesIconFolder, oldName + tempTag);
            if (thumb != null)
                await _fs.MoveFileAsync(thumb, VPath.Combine(StoriesIconFolder, newName + VPath.GetExtension(thumb)), true);
            _localization.Rename(oldName + tempTag, newName);

            level.Name = newName;
            level.Common.Name = newName;
            level.Hunter.Name = newName + LocationListService.HunterSuffix;
            UpdateSubjectReferences(SubjectType.Track, oldName, newName);
        }
    }

    private async Task MoveIfExistsAsync(string from, string to)
    {
        if (await _fs.FileExistsAsync(from))
            await _fs.MoveFileAsync(from, to, true);
    }

    /// <summary>Keeps "Stars Subject=Track/Location Name=..." conditions pointing at renamed entries.</summary>
    public void UpdateSubjectReferences(SubjectType subject, string oldName, string newName)
    {
        void Fix(UnlockData unlock)
        {
            if (unlock.Subject == subject && unlock.SubjectName == oldName)
                unlock.SubjectName = newName;
        }

        foreach (var location in _list.Locations)
        {
            Fix(location.Common);
            Fix(location.Hunter);
            foreach (var level in location.AllLevels)
            {
                Fix(level.Common.Unlock);
                Fix(level.Hunter.Unlock);
            }
        }
    }

    /// <summary>Renames a location and every level inside it (levels keep the LOCATION_MODE_NN convention).</summary>
    public async Task RenameLocationAsync(LocationData location, string newName)
    {
        string oldName = location.Name;
        if (oldName == newName)
            return;

        string? thumb = await FindThumbnailAsync(LocationsIconFolder, oldName);
        if (thumb != null)
            await _fs.MoveFileAsync(thumb, VPath.Combine(LocationsIconFolder, newName + VPath.GetExtension(thumb)), true);

        _localization.Rename(oldName, newName);
        location.Name = newName;
        UpdateSubjectReferences(SubjectType.Location, oldName, newName);

        var renames = new List<(LevelData, string, string)>();
        foreach (var (mode, group) in location.Groups)
        {
            for (int i = 0; i < group.Count; i++)
            {
                group[i].Location = newName;
                string expected = LocationListService.MakeLevelName(newName, mode, i);
                if (group[i].Name != expected)
                    renames.Add((group[i], group[i].Name, expected));
            }
        }
        if (renames.Count > 0)
            await RenameLevelsAsync(renames);
    }

    public async Task DeleteLocationAsync(LocationData location)
    {
        foreach (var level in location.AllLevels.ToList())
        {
            await _fs.DeleteFileAsync(XmlPathFor(level.Name));
            await DeleteImagesAsync(StoriesIconFolder, level.Name);
            _localization.Remove(level.Name);
        }
        await DeleteImagesAsync(LocationsIconFolder, location.Name);
        _localization.Remove(location.Name);
        _list.Locations.Remove(location);
    }

    public static string FormatLocationName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "NEW_LOCATION";
        var chars = input.Trim().ToUpperInvariant().Replace(' ', '_')
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray();
        string name = new string(chars);
        return name.Length == 0 ? "NEW_LOCATION" : name;
    }
}
