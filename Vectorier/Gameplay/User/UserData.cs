#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.User;

public class UserData
{
    private string _userId;

    private Color _color;
    private List<AnimationInfo> _availableAnimation = new List<AnimationInfo>();

    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    public string Name { get; set; }

    public List<string> Skins { get; set; } = new List<string>();

    public List<string> Stocks { get; set; } = new List<string>();

    public bool IsSelf { get; set; }

    public bool IsBot => !IsSelf;

    public List<string> Arrests { get; set; } = new List<string>();

    public List<string> Murders { get; set; } = new List<string>();

    public List<string> Respawns { get; set; } = new List<string>();

    public List<string> Births { get; set; } = new List<string>();

    public string BirthSpawn { get; set; }

    public int AI { get; set; }

    public bool IsTrick { get; set; }

    public bool IsItem { get; set; }

    public bool IsVictory { get; set; }

    public bool IsLost { get; set; }

    public float StartTime { get; set; }

    public float LiveTime { get; set => field = value * 60; }

    public bool isIcon { get; set; }

    public UserData(string userID)
    {
        _userId = userID;
    }

    public void Init()
    {
        SetModeles();
        SetAnimation();
    }

    private void SetModeles()
    {
        var list = new List<string>();
        list.Add("0.xml");

        foreach (var stock in Stocks)
        {
            list.Add(stock + ".xml");
        }

        foreach (var skin in Skins)
        {
            list.Add(skin + ".xml");
        }

        if (IsSelf)
        {
        }

        Skins = list;
    }

    public void SetAnimation()
    {
        _availableAnimation.Clear();
        foreach (var anim in Animations.ToList())
        {
            _availableAnimation.Add(anim);
        }
    }

    public AnimationInfo Animation(string name)
    {
        for (int i = 0; i < _availableAnimation.Count; i++)
        {
            if (_availableAnimation[i].Name == name)
            {
                return _availableAnimation[i];
            }
        }
        return null;
    }

    public AnimationInfo Animation(int id)
    {
        return null;
    }
}
