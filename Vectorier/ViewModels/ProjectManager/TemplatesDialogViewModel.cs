using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.ProjectManager;

public sealed partial class StarsTemplateItem : ObservableObject
{
    [ObservableProperty] private string _name = "New_Stars_Template";
    [ObservableProperty] private bool _completed = true;
    [ObservableProperty] private bool _bonuses = true;
    [ObservableProperty] private bool _tricks = true;
}

public sealed partial class RewardTemplateItem : ObservableObject
{
    [ObservableProperty] private string _name = "New_Reward_Template";
    [ObservableProperty] private int _coins1 = 100;
    [ObservableProperty] private int _coins2 = 150;
    [ObservableProperty] private int _coins3 = 250;
}

/// <summary>Edits the Stars and Reward templates of List_Payed.xml (Unity's "Edit Templates" view).</summary>
public sealed partial class TemplatesDialogViewModel : DialogViewModel
{
    private readonly LocationListService _list;

    public TemplatesDialogViewModel(LocationListService list)
    {
        _list = list;
        Title = Loc.T("Edit Templates");
        foreach (var t in list.StarsTemplates)
            Stars.Add(new StarsTemplateItem { Name = t.Name, Completed = t.Completed, Bonuses = t.Bonuses, Tricks = t.Tricks });
        foreach (var t in list.RewardTemplates)
            Rewards.Add(new RewardTemplateItem { Name = t.Name, Coins1 = t.Coins1, Coins2 = t.Coins2, Coins3 = t.Coins3 });
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 760;
    public override double PreferredHeight => 560;

    public ObservableCollection<StarsTemplateItem> Stars { get; } = new();
    public ObservableCollection<RewardTemplateItem> Rewards { get; } = new();

    [RelayCommand] private void AddStars() => Stars.Add(new StarsTemplateItem());
    [RelayCommand] private void RemoveStars(StarsTemplateItem? item) { if (item != null) Stars.Remove(item); }
    [RelayCommand] private void AddReward() => Rewards.Add(new RewardTemplateItem());
    [RelayCommand] private void RemoveReward(RewardTemplateItem? item) { if (item != null) Rewards.Remove(item); }

    [RelayCommand] private void Save() => Close(true);
    [RelayCommand] private void CancelDialog() => Close(false);

    public void Apply()
    {
        _list.StarsTemplates.Clear();
        _list.StarsTemplates.AddRange(Stars.Where(s => !string.IsNullOrWhiteSpace(s.Name)).Select(s => new StarsTemplate
        {
            Name = s.Name.Trim(),
            Completed = s.Completed,
            Bonuses = s.Bonuses,
            Tricks = s.Tricks,
        }));
        _list.RewardTemplates.Clear();
        _list.RewardTemplates.AddRange(Rewards.Where(r => !string.IsNullOrWhiteSpace(r.Name)).Select(r => new RewardTemplate
        {
            Name = r.Name.Trim(),
            Coins1 = r.Coins1,
            Coins2 = r.Coins2,
            Coins3 = r.Coins3,
        }));
    }
}
