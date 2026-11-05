// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Vectorier.Gameplay.Trigger.Events
{
    public class TE_Exit : TriggerEvent
    {
        public TE_Exit()
        {
            _Type = EventType.TET_EXIT;
        }
    }
}
