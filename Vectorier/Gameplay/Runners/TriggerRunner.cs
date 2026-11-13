#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Trigger;
using Vectorier.Gameplay.Trigger.Events;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Runners;

public class TriggerRunner : QuadRunner
{
    public enum TriggerColisionType
    {
        OneNode = 0,
        MultiNode = 1
    }

    private TriggerTimer _timer;
    private Variable _nodeVar;
    private Variable _activeVar;

    private ModelHuman _checkedModel;

    private Dictionary<string, Variable> vars = new Dictionary<string, Variable>();

    private List<TriggerLoop> _loops = new List<TriggerLoop>();
    private List<TE_ChangeVar> _renderEvents = new List<TE_ChangeVar>();

    private XmlNode _xmlNode;
    private XmlNode _rawNode;

    private float _w;

    private float _h;
    public List<string> _NodesName;

    public string CollisionNodeName { get; set; }

    public List<string> TriggerNodesName => _NodesName;

    public List<TriggerLine> Lines { get; private set; }

    public Variable AIVar { get; private set; }

    public Variable ModelVar { get; private set; }

    public string NodeName => _nodeVar == null ? "COM" : _nodeVar.ValueString;

    public bool IsActive => _activeVar.ValueInt == 1;

    public string statistic { get; }

    public TriggerColisionType CollisionType { get; private set; } = TriggerColisionType.OneNode;

    public bool IsVisible;

    public TriggerRunner(float x, float y, float width, float height, XmlNode node)
: base(x, y, width, height, sticky: false, 0, node.Attributes["Name"].ParseString(string.Empty))
    {
        TypeClass = RunnerType.Trigger;
        _timer = new TriggerTimer(this);
        _w = width;
        _h = height;
        _xmlNode = node["Content"];
        statistic = node.Attributes["Statistic"].ParseString();
        _rawNode = node;

        Z = -14;
    }

    public void Init()
    {
        // An error while the trigger is read names it (and its line) in the error report.
        try
        {
            InitCore();
        }
        catch (Exception ex) when (Diagnostics.XmlSource.Note(ex, _rawNode)) { throw; }
    }

    private void InitCore()
    {
        parseVariable(_xmlNode["Init"]);
        SetTriggerCollisionType();
        XmlNode xmlNode = _xmlNode["Template"];
        if (xmlNode != null)
        {
            string value = xmlNode.Attributes["Name"].Value;
            parseTemplate(TemplateModule.getTemplateXmlNode(value));
        }
        parseLoops(_xmlNode);
        InitEvents();
    }

    private void SetTriggerCollisionType()
    {
        string[] array = _nodeVar.ValueString.Split('|');
        if (array.Length == 1)
        {
            CollisionType = TriggerColisionType.OneNode;
            CollisionNodeName = array[0];
        }
        else
        {
            CollisionType = TriggerColisionType.MultiNode;
            _NodesName = new List<string>(array);
        }
    }

    private void InitEvents()
    {
        foreach (TriggerLoop loop in _loops)
        {
            foreach (TriggerEvent @event in loop.Events)
            {
                if (@event.Type == TriggerEvent.EventType.TET_ON_SHOW || @event.Type == TriggerEvent.EventType.TET_ON_HIDE)
                {
                    GlobalObjects.AddDrawable(this);
                }
                if (@event.Type == TriggerEvent.EventType.TET_VAR_CHANGE)
                {
                    _renderEvents.Add((TE_ChangeVar)@event);
                }
            }
        }
    }

    public void DrawRectangle()
    {
        if (!IsEnabled)
            return;

        var vs = Sets.Current.Containers[1];
        var pos = vs.TransformPoint(rectangle.Origin);
        Raylib.DrawRectangleV(pos, rectangle.Size * WorldScale, new Color(255, 255, 0, 64));
    }

    public override void Draw()
    {
        var vs = Sets.Current.Containers[1];

        var rect = new Rectangle(rectangle);
        rect.Origin = vs.TransformPoint(rect.Origin);
        rect.Size *= vs.FrameScale;

        bool visibleNow = VectorGame.Instance.CameraRectangle.Intersect(rect);

        if (!IsVisible && visibleNow)
        {
            IsVisible = true;
            OnBecameVisible();
        }
        else if (IsVisible && !visibleNow)
        {
            IsVisible = false;
            OnBecameUnvisible();
        }
    }

    private void OnBecameVisible()
    {
        CheckEvent(new TE_OnShow(), null);
    }

    private void OnBecameUnvisible()
    {
        CheckEvent(new TE_OnHide(), null);
    }

    public void parseTemplate(XmlNode node)
    {
        parseLoops(node);
    }

    public void parseVariable(XmlNode node, bool isTemplate = false)
    {
        if (node == null)
        {
            return;
        }
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (!childNode.LocalName.Equals("SetVariable"))
            {
                continue;
            }
            string text = childNode.Attributes["Name"].ParseString();
            string text2 = childNode.Attributes["Value"].ParseString(string.Empty);
            if (vars.ContainsKey("_" + text))
            {
                switch (vars["_" + text].Type)
                {
                    case VariableTypeE.VT_INT:
                        vars["_" + text].setValue(int.Parse(text2, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case VariableTypeE.VT_DOUBLE:
                        vars["_" + text].setValue(float.Parse(text2, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case VariableTypeE.VT_STRING:
                        vars["_" + text].setValue(text2);
                        break;
                }
            }
            else
            {
                vars["_" + text] = Variable.createVariable(text2, text, this);
            }
        }
        if (!isTemplate)
        {
            AIVar = vars["_$AI"];
            _nodeVar = vars["_$Node"];
            _activeVar = vars["_$Active"];
            vars["_$ActionID"] = Variable.createVariable(" ", "$ActionID", this);
            if (!vars.ContainsKey("_$Model"))
            {
                ModelVar = Variable.createVariable(" ", "$Model", this);
                vars["_$Model"] = ModelVar;
            }
            else
            {
                ModelVar = vars["_$Model"];
            }
            vars["_$Key"] = Variable.createVariable(" ", "$Key", this);
            if (AIVar == null)
            {
                AIVar = Variable.createVariable("-1", "$AI");
            }
            if (_activeVar == null)
            {
            }
            if (_nodeVar != null)
            {
            }
        }
    }

    public void parseLoops(XmlNode node)
    {
        if (node == null)
        {
            return;
        }
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (string.Equals(childNode.LocalName, "Loop"))
            {
                TriggerLoop triggerRunnerLoop;
                if (childNode.Attributes["Template"] != null)
                {
                    string value = childNode.Attributes["Template"].Value;
                    XmlNode templateLoopXML = TemplateModule.getTemplateLoopXML(value);
                    triggerRunnerLoop = TriggerLoop.createLoop(templateLoopXML, this);
                    _loops.Add(triggerRunnerLoop);
                }
                else
                {
                    triggerRunnerLoop = TriggerLoop.createLoop(childNode, this);
                    _loops.Add(triggerRunnerLoop);
                }
            }
        }
    }

    public void CheckEvent(TriggerEvent @event, ModelHuman model)
    {
        try
        {
            CheckEventCore(@event, model);
        }
        catch (Exception ex) when (Diagnostics.XmlSource.Note(ex, _rawNode)) { throw; }
    }

    private void CheckEventCore(TriggerEvent @event, ModelHuman model)
    {
        if (!IsEnabled || (!IsActive && !@event.IsTimeOutOrActivate()) || (model != null && model.IsPhysics && !@event.IsCollision()))
        {
            return;
        }
        _checkedModel = model;
        List<List<TriggerAction>> list = new List<List<TriggerAction>>();
        foreach (var loop in _loops)
        {
            if (loop.CheckEvent(@event))
            {
                list.Add(loop.Actions);
            }
        }
        if (list.Count != 0)
        {
            foreach (var actions in list)
            {
                TriggerActionsRenderer.Current.AddActions(actions);
            }
            if (model != null)
            {
                if (model.UserData.IsSelf)
                {
                    model.controllerStatistics.SetTrigger(this);
                }
            }
            ModelVar.setValue("");
            _checkedModel = null;
        }
    }

    public void ResetRenderEvents()
    {
        foreach (var @event in _renderEvents)
        {
            @event.Reset();
        }
    }

    public void CheckRenderEvent(ModelHuman model)
    {
        if (_renderEvents == null)
        {
            return;
        }
        ModelVar.setValue(model.ModelName);
        foreach (TE_ChangeVar renderEvent in _renderEvents)
        {
            if (renderEvent.IsChange())
            {
                CheckEvent(renderEvent, model);
                ModelVar.setValue(model.ModelName);
            }
        }
        ModelVar.setValue(string.Empty);
    }

    public override bool Render()
    {
        return _timer.Render();
    }

    public override void Reset()
    {
        base.Reset();
        _timer.Reset();
        foreach (var vars in vars.Values)
        {
            vars.resetValues();
        }
        foreach (var loop in _loops)
        {
            loop.Reset();
        }
        if (Lines != null)
        {
            foreach (var line in Lines)
            {
                line.Reset();
            }
        }
    }

    public Variable GetVar(string key)
    {
        if (!vars.ContainsKey(key))
        {
            Console.WriteLine("No Var Name = " + key + " in trigger " + Name, true);
            return null;
        }
        return vars[key];
    }

    public SpawnRunner GetSpawnByName(string name)
    {
        foreach (var spawn in ParentElements.Spawns)
        {
            if (spawn.Name == name)
            {
                return spawn;
            }
        }
        return null;
    }

    public void SetModelVar()
    {
        if (_checkedModel == null)
        {
            return;
        }
        ModelVar.setValue(_checkedModel.ModelName);
    }

    public void AddLine(TriggerLine line)
    {
        if (Lines == null)
        {
            Lines = new List<TriggerLine>();
        }
        Lines.Add(line);
    }

    public void SetKeyVar(string key)
    {
        vars["_$Key"].setValue(key);
    }

    public void SetTimer(int frames)
    {
        _timer.Start(frames);
    }

    public override string ToString()
    {
        return null;
    }
}
