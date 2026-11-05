// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events
{
    public class TE_StartGame : TriggerEvent
    {
        private static List<TriggerRunner> _WithThisEvent;

        /// <summary>The editor's addition: a new game has its own start triggers.</summary>
        public static void ResetAll() => _WithThisEvent = null;

        public TE_StartGame(TriggerRunner p_parent)
        {
            _Type = EventType.TET_ON_START_GAME;
            if (p_parent != null)
            {
                AddToWithThisEvent(p_parent);
            }
        }

        private static void AddToWithThisEvent(TriggerRunner p_parent)
        {
            if (_WithThisEvent == null)
            {
                _WithThisEvent = new List<TriggerRunner>();
            }
            if (!_WithThisEvent.Contains(p_parent))
            {
                _WithThisEvent.Add(p_parent);
            }
        }

        public static void ActivateThisEvent()
        {
            if (_WithThisEvent == null)
            {
                return;
            }
            TE_StartGame p_event = new TE_StartGame(null);
            foreach (TriggerRunner item in _WithThisEvent)
            {
                item.CheckEvent(p_event, null);
            }
        }
    }
}
