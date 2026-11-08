#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Camera;

public class LocationCamera
{
    private ModelNode _position = new ModelNode(new Vector3());
    private bool _isRender;

    /// <summary>The editor's addition: a new game starts with the game's defaults.</summary>
    public static void ResetAll()
    {
        Current = null;
        MinZoom = Config.MinZoom;
        MaxZoom = Config.MaxZoom;
        CurrentZoom = Config.CurrZoom;
        FluencyCurrent = Config.Fluency;
        MaxSpeed = Config.MaxSpeed;
    }

    /// <summary>
    /// moves.xml's &lt;Config&gt;&lt;Camera MinZoom MaxZoom CurrZoom MaxSpeed Fluency/&gt; (the game's defaults when it has
    /// none). Every level starts from these (a Camera trigger may change the current ones while it plays).
    /// </summary>
    public static (float MinZoom, float MaxZoom, float CurrZoom, float MaxSpeed, float Fluency) Config = (0.1f, 1.3f, 0.5f, 50f, 2f);

    /// <summary>The camera's top speed, in level units per tick (moves.xml's MaxSpeed; the game had 50 built in).</summary>
    public static float MaxSpeed = 50;

    public static float MinZoom = 0.1f;

    public static float MaxZoom = 1.3f;

    public static float CurrentZoom = 0.5f;

    public static float FluencyCurrent = 2;

    public float Zoom { get; private set; } = 1;

    public CameraNode Node { get; set; }

    public static LocationCamera Current { get; private set; }

    public float X => (float)_position.Start.X;

    public float Y => (float)_position.Start.Y;

    public Vector3 Position
    {
        get
        {
            return _position.Start;
        }
        set
        {
            _position.Start = value;
        }
    }

    public void Init()
    {
        _position.Attenuation = 0;
        Current = this;
        _isRender = true;
    }

    public void Reset()
    {
        _position.PositionStart(new Vector3(0, 0, 0));
        _position.PositionEnd(new Vector3(0, 0, 0));
        _isRender = true;
        Node = null;
    }

    public void StartPosition(Vector3 position)
    {
        _position = new ModelNode(position);
        Node = new CameraNode(_position);
    }

    public void UpdatePosition()
    {
        if (!_isRender)
        {
            return;
        }
        _position.TimeStep(0);
        var nodeStart = Node.Start;
        var nodeEnd = Node.End;
        var posStart = _position.Start;
        var posEnd = _position.End;

        nodeStart.Z = 0;
        nodeEnd.Z = 0;
        posStart.Z = 0;
        posEnd.Z = 0;

        Vector3 v1 = posEnd + nodeStart;
        nodeEnd = v1 - nodeEnd;
        nodeEnd = nodeEnd - posStart;

        nodeStart = nodeStart - posStart;
        nodeStart = nodeStart.Multiply(0.15);
        nodeEnd = nodeEnd + nodeStart;

        if (FluencyCurrent < nodeEnd.Length())
        {
            nodeEnd = nodeEnd.Normalize();
            nodeEnd = nodeEnd.Multiply(FluencyCurrent);
        }
        posStart.Add(nodeEnd);
        nodeEnd = posStart - posEnd;

        if (nodeEnd.Length() > MaxSpeed)
        {
            nodeEnd = nodeEnd.Multiply(MaxSpeed / nodeEnd.Length());
            posStart = posEnd + nodeEnd;
        }
        _position.PositionStart(posStart);
    }

    public void Stop()
    {
        Vector3 p_position = Node.Start + (Node.Start - Node.End) * 20f;
        Node = new CameraNode(new ModelNode(p_position));
    }

    public void Zooming(float value = 1f, bool isStart = false)
    {
        if (Zoom != value)
        {
            if (value < MinZoom)
            {
                value = MinZoom;
            }
            if (value > MaxZoom)
            {
                value = MaxZoom;
            }
            Zoom = value;
            foreach (var container in Sets.Current.Containers.Values)
            {
                container.Zooming(value, isStart);
            }
        }
    }
}
