// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Animation;

namespace Vectorier.Gameplay.User
{
    public class UserData
    {
        private string _UserId;

        private Color _Color;

        private int _Gender;

        private List<AnimationInfo> _AvailableAnimation = new List<AnimationInfo>();

        private string _Name;

        private List<string> _Skins = new List<string>();

        private List<string> _Stocks = new List<string>();

        private bool _IsSelf;

        private List<string> _Arrests = new List<string>();

        private List<string> _Murders = new List<string>();

        private List<string> _Respawns = new List<string>();

        private List<string> _Births = new List<string>();

        private string _BirthSpawn;

        private int _AI;

        private bool _IsTrick;

        private bool _IsItem;

        private bool _IsVictory;

        private bool _IsLost;

        private float _StartTime;

        private float _LiveTime;

        private bool _isIcon;

        public string UserId => _UserId;

        public Color Color
        {
            get => _Color;
            set => _Color = value;
        }

        public string Name
        {
            get => _Name;
            set => _Name = value;
        }

        public List<string> Skins
        {
            get => _Skins;
            set => _Skins = value;
        }

        public List<string> Stocks
        {
            get => _Stocks;
            set => _Stocks = value;
        }

        public bool IsSelf
        {
            get => _IsSelf;
            set => _IsSelf = value;
        }

        public bool IsBot => !_IsSelf;

        public List<string> Arrests
        {
            get => _Arrests;
            set => _Arrests = value;
        }

        public List<string> Murders
        {
            get => _Murders;
            set => _Murders = value;
        }

        public List<string> Respawns
        {
            get => _Respawns;
            set => _Respawns = value;
        }

        public List<string> Births
        {
            get => _Births;
            set => _Births = value;
        }

        public string BirthSpawn
        {
            get => _BirthSpawn;
            set => _BirthSpawn = value;
        }

        public int AI
        {
            get => _AI;
            set => _AI = value;
        }

        public bool IsTrick
        {
            get => _IsTrick;
            set => _IsTrick = value;
        }

        public bool IsItem
        {
            get => _IsItem;
            set => _IsItem = value;
        }

        public bool IsVictory
        {
            get => _IsVictory;
            set => _IsVictory = value;
        }

        public bool IsLost
        {
            get => _IsLost;
            set => _IsLost = value;
        }

        public float StartTime
        {
            get => _StartTime;
            set => _StartTime = value;
        }

        public float LiveTime
        {
            get => _LiveTime;
            set => _LiveTime = value * 60;
        }

        public bool isIcon
        {
            get => _isIcon;
            set => _isIcon = value;
        }

        public UserData(string userID)
        {
            _UserId = userID;
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

            foreach (var stock in _Stocks)
            {
                list.Add(stock + ".xml");
            }

            foreach (var skin in _Skins)
            {
                list.Add(skin + ".xml");
            }

            if (IsSelf)
            {
                //foreach (var item in AbstractManager<StoreManager>.Instance.GetItems(StoreItemType.Gear))
                //{
                //    if (Game.IsItemEquipped(item.Id))
                //    {
                //        list.Add(item.Id + ".xml");
                //    }
                //}
            }

            _Skins = list;
        }

        public void SetAnimation()
        {
            _AvailableAnimation.Clear();
            foreach (var anim in Animations.ToList())
            {
                _AvailableAnimation.Add(anim);
            }
        }

        public void AddTrick(int id)
        {
            var animation = Animation(id);
            if (animation != null)
            {
                var list = Animations.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    if (animation.Id == list[i].Id)
                    {
                        _AvailableAnimation.Add(list[i]);
                    }
                }
            }
        }

        public AnimationInfo Animation(string name)
        {
            for (int i = 0; i < _AvailableAnimation.Count; i++)
            {
                if (_AvailableAnimation[i].Name == name)
                {
                    return _AvailableAnimation[i];
                }
            }
            return null;
        }

        public AnimationInfo Animation(int id)
        {
            return null;
        }
    }
}
