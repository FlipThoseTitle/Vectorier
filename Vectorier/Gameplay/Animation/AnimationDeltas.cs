#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Animation;

public class AnimationDeltas
{
    private readonly List<AnimationDelta> _deltas = new List<AnimationDelta>();

    /// <summary>The conditions (Width, Height, DeltaX, DeltaY, VelosityX, VelosityY) of this type.</summary>
    public IReadOnlyList<AnimationDelta> Items => _deltas;

    public AnimationDeltas(XmlNode mainNode, AnimationDeltaType type)
    {
        foreach (XmlNode node in mainNode.ChildNodes)
        {
            if (GetDeltaTypeByString(node.Attributes["Type"].ParseString("")) == type)
            {
                Vector2 p_value = new Vector2(node.Attributes["Min"].ParseFloat(float.NaN), node.Attributes["Max"].ParseFloat(float.NaN));
                int p_corner = node.Attributes["Corner"].ParseInt(-1);
                _deltas.Add(new AnimationDelta(GetDeltaNameByString(node.Name), type, p_value, p_corner) { XmlName = node.Name });
            }
        }
    }

    public static AnimationDeltaName GetDeltaNameByString(string name)
    {
        switch (name)
        {
            case "Width":
                return AnimationDeltaName.Width;
            case "Height":
                return AnimationDeltaName.Height;
            case "DeltaX":
                return AnimationDeltaName.DeltaX;
            case "DeltaY":
                return AnimationDeltaName.DeltaY;
            case "VelosityX":
                return AnimationDeltaName.VelocityX;
            case "VelosityY":
                return AnimationDeltaName.VelocityY;
            default:
                return AnimationDeltaName.Max;
        }
    }

    public static AnimationDeltaType GetDeltaTypeByString(string type)
    {
        switch (type)
        {
            case "H":
                return AnimationDeltaType.Horizontal;
            case "V":
                return AnimationDeltaType.Vertical;
            case "C":
                return AnimationDeltaType.Collision;
            default:
                return AnimationDeltaType.Max;
        }
    }

    public bool IsCheck(AnimationDeltaData deltaSort, int sign, Vector3 velocity)
    {
        if (_deltas.Count == 0)
        {
            return true;
        }
        if (deltaSort == null && _deltas.Count > 0)
        {
            return false;
        }
        foreach (AnimationDelta delta in _deltas)
        {
            if (!IsDeltaName(delta, deltaSort, sign, velocity))
            {
                return false;
            }
        }
        return true;
    }

    public bool IsDeltaName(AnimationDelta delta, AnimationDeltaData deltaData, int sign, Vector3 velocity)
    {
        int corner = delta.GetCorner(sign);
        double deltaValue = deltaData.GetDeltaValue(delta.Name, corner, sign, velocity);
        return delta.IsInterval(deltaValue);
    }
}
