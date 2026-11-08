// Ported from the Unity Vectorier Editor (Scripts/Trigger/TriggerModel.cs).
#nullable disable
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Vectorier.Services.Triggers;

// ================= Values =================

public enum ValueMode { Literal, Variable, ModelProp, NodePos }

// A single attribute value: literal, _Variable or a ?getModel query.
public class TValue
{
    public ValueMode mode = ValueMode.Literal;
    public string text = "";

    public string varName = "$Model";

    public bool modelIsVar = true;
    public string model = "$Model";
    public string prop = "animationName";

    public bool nodeIsVar = true;
    public string node = "$Node";
    public string coord = "worldPositionX";

    public bool uiCustom;   // editor state, remembers "Custom" on the Zoom/AI popups

    public static TValue Literal(string s) => new TValue { mode = ValueMode.Literal, text = s ?? "" };

    public string ModelRef => modelIsVar ? "_" + model : model;
    public string NodeRef => nodeIsVar ? "_" + node : node;

    public string ToXml()
    {
        switch (mode)
        {
            case ValueMode.Variable: return "_" + (varName ?? "");
            case ValueMode.ModelProp: return "?getModel[" + ModelRef + "]." + prop;
            case ValueMode.NodePos: return "?getModel[" + ModelRef + "].getNode[" + NodeRef + "]." + coord;
            default: return text ?? "";
        }
    }

    static readonly Regex NodeRx = new Regex(@"^\?getModel\[(.+?)\]\.getNode\[(.+?)\]\.(\w+)$");
    static readonly Regex PropRx = new Regex(@"^\?getModel\[(.+?)\]\.(\w+)$");

    public static TValue Parse(string s)
    {
        var v = new TValue();
        s = s ?? "";

        var m = NodeRx.Match(s);
        if (m.Success)
        {
            v.mode = ValueMode.NodePos;
            SetRef(m.Groups[1].Value, out v.modelIsVar, out v.model);
            SetRef(m.Groups[2].Value, out v.nodeIsVar, out v.node);
            v.coord = m.Groups[3].Value;
            return v;
        }

        m = PropRx.Match(s);
        if (m.Success)
        {
            v.mode = ValueMode.ModelProp;
            SetRef(m.Groups[1].Value, out v.modelIsVar, out v.model);
            v.prop = m.Groups[2].Value;
            return v;
        }

        if (s.Length > 1 && s[0] == '_')
        {
            v.mode = ValueMode.Variable;
            v.varName = s.Substring(1);
            return v;
        }

        v.mode = ValueMode.Literal;
        v.text = s;
        return v;
    }

    static void SetRef(string raw, out bool isVar, out string name)
    {
        if (!string.IsNullOrEmpty(raw) && raw[0] == '_') { isVar = true; name = raw.Substring(1); }
        else { isVar = false; name = raw; }
    }
}

public class TParam
{
    public string attr;
    public TValue v = new TValue();
    public TParam(string attr, TValue v) { this.attr = attr; this.v = v; }
}

// ================= Events =================

public enum EvKind { Event, Block, Raw }

public class TEvent
{
    public EvKind kind = EvKind.Event;
    public string id = "Enter";
    public List<TParam> ps = new List<TParam>();
    public string template = "";
    public string prefix = "";
    public string raw = "";

    public TValue Get(string attr)
    {
        for (int i = 0; i < ps.Count; i++) if (ps[i].attr == attr) return ps[i].v;
        var nv = new TValue();
        ps.Add(new TParam(attr, nv));
        return nv;
    }
}

// ================= Conditions =================

public enum CondKind { Compare, Group, Select, Block, Raw }
public enum Cmp { Equal, Greater, Less }

public class TCond
{
    public CondKind kind = CondKind.Compare;

    public Cmp cmp = Cmp.Equal;
    public bool not;
    public TValue a = new TValue();
    public TValue b = new TValue();
    public string type = "";        // some files have Type="Bool", just pass it through
    public bool altPair;            // Greater/Less that used Value1/Value2, write it back that way

    public string op = "And";       // group
    public List<TCond> children = new List<TCond>();

    public TValue selObject = new TValue();   // <Select Object From>
    public TValue selFrom = new TValue();

    public string template = "";
    public string prefix = "";
    public string raw = "";
}

// ================= Actions =================

public enum ActKind { Action, Choose, Block, Raw }

public class TAct
{
    public ActKind kind = ActKind.Action;

    public string id = "SetVariable";
    public List<TParam> ps = new List<TParam>();

    public string order = "Straight";
    public int set = 1;
    public List<TAct> children = new List<TAct>();

    public string template = "";
    public string prefix = "";
    public string raw = "";

    public TValue Get(string attr)
    {
        for (int i = 0; i < ps.Count; i++) if (ps[i].attr == attr) return ps[i].v;
        var nv = new TValue();
        ps.Add(new TParam(attr, nv));
        return nv;
    }
}

// ================= Loops =================

public enum LoopKind { Custom, TemplateLoop, FullTemplate, Raw }

// One <Variable> of a loop's <Using> (TriggerTemplates.xml): a variable the loop reads, with its type and
// default. The editor uses it; the game ignores <Using>.
public class TUsing
{
    public string name = "NewVariable";
    public string def = "";
    public string type = "String";          // as written (Type="UInt"...)
    public bool complexName;                // ComplexName="1"
    public List<KeyValuePair<string, string>> extra = new List<KeyValuePair<string, string>>();

    public VType VType => VTypes.FromName(type);
}

public class TLoop
{
    public const string NoName = "Loop";

    public string name = NoName;
    public LoopKind kind = LoopKind.Custom;
    public string template = "";

    public List<TUsing> usings = new List<TUsing>();
    public List<TEvent> events = new List<TEvent>();
    public string rootOp = "And";
    public List<TCond> conds = new List<TCond>();
    public List<TAct> acts = new List<TAct>();

    // round-trip only: the file had Template="..." directly on <Events>/<Conditions>/<Actions>
    public bool evTagForm, condTagForm, actTagForm;

    public string raw = "";

    public bool HasName => !string.IsNullOrEmpty(name) && name != NoName;

    public string Display
    {
        get
        {
            if (kind == LoopKind.FullTemplate) return string.IsNullOrEmpty(template) ? "Template" : template;
            if (kind == LoopKind.Raw) return "Unparsed";
            return HasName ? name : NoName;
        }
    }
}

// ================= Variables =================

public class TVar
{
    public string name = "NewVariable";
    public string value = "";
    public VType type = VType.String;
    public bool locked;
    public bool uiCustom;   // editor only, see TValue.uiCustom

    public TVar() { }
    public TVar(string n, string v, bool locked = false) { name = n; value = v; this.locked = locked; }
    public TVar(string n, string v, VType type) { name = n; value = v; this.type = type; }
}
