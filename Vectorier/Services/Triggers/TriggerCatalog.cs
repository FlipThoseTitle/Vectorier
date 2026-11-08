// Ported from the Unity Vectorier Editor (Scripts/Trigger/TriggerCatalog.cs).
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Vectorier.Services.Localization;

namespace Vectorier.Services.Triggers;


// ================= Catalog entries =================

public class ParamDef
{
    public string attr;
    public string label;
    public VType type;          // literal editor type, Variable mode is always there on top
    public string[] choices;    // suggestions for Text, fixed list for Orientation
    public string def;
    public bool optional;
    public string hint;

    public bool query;              // allow ?getModel[...] queries
    public bool varName;            // plain variable name, written without the _
    public bool typedByVariable;    // editor follows the type of the variable in Name
    public string onValue = "1", offValue = "0";

    public ParamDef(string attr, string label, VType type = VType.String,
                    string def = "", string[] choices = null, string hint = "", bool optional = false)
    {
        this.attr = attr;
        this.label = label;
        this.type = type;
        this.def = def;
        this.choices = choices;
        this.hint = hint;
        this.optional = optional;
    }

    public ParamDef Queries() { query = true; return this; }
    public ParamDef AsVarName() { varName = true; return this; }
    public ParamDef FollowsVariable() { typedByVariable = true; return this; }
    public ParamDef Toggle(string on, string off) { onValue = on; offValue = off; return this; }
}

public class EntryDef
{
    public string id;       // same as tag except the Camera.* variants
    public string tag;
    public string title;
    public string hint;
    public string menu;     // "/" makes a submenu
    public ParamDef[] ps;

    public EntryDef(string id, string tag, string title, string menu, string hint, params ParamDef[] ps)
    {
        this.id = id;
        this.tag = tag;
        this.title = title;
        this.menu = menu;
        this.hint = hint;
        this.ps = ps ?? new ParamDef[0];
    }
}

public static class TriggerCatalog
{
    public static readonly string[] Results = { "Win", "Loss", "Death" };
    public static readonly string[] LineOrientations = { "Vertical", "Horizontal" };

    public static readonly string[] ModelProps =
    {
        "direction", "isControlled", "isCameraFollow", "animationName", "animationFrame", "condition", "AI"
    };

    public static readonly string[] NodeCoords =
    {
        "worldPositionX", "worldPositionY", "localPositionX", "localPositionY"
    };

    // used by the ?getModel name picker when the scene has no ExportConfig models
    public static readonly string[] CommonModels = { "Player", "Hunter", "Helper" };
    public static readonly string[] CommonNodes = { "COM", "DetectorH", "Head", "Foot" };

    // set by the game at runtime, never declared in Init
    public static readonly string[] BuiltInVars = { "$Model", "$Key", "$ActionID" };

    // the trigger's own settings (and the shared Flag1), always declared in Init
    public static readonly string[] FixedVars = { "$Active", "$AI", "$Node", "Flag1" };

    // nodes a trigger can test entry with ($Node)
    public static readonly string[] TriggerNodes = { "COM", "DetectorH", "DetectorV" };

    // ---------------- Names shown for the $ variables (the XML keeps $Model, _$Model...) ----------------

    private static readonly string[] LabelledVars = { "$Active", "$AI", "$Node", "$Model", "$Key", "$ActionID" };
    private static readonly string[] VarLabels = { "Active", "AI", "Node", "Model inside Trigger", "Key Pressed in Trigger", "Signal's Name" };

    /// <summary>The name shown for a variable: Active, AI, Node, Model inside Trigger... (others unchanged).</summary>
    public static string VarLabel(string name)
    {
        int i = Array.IndexOf(LabelledVars, name);
        return i < 0 ? name : Loc.T(VarLabels[i]);
    }

    /// <summary>Back from a shown name (translated or English) to the variable; anything else is returned as typed.</summary>
    public static string VarFromLabel(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string t = text.Trim();
        for (int i = 0; i < LabelledVars.Length; i++)
            if (string.Equals(t, Loc.T(VarLabels[i]), StringComparison.OrdinalIgnoreCase) || string.Equals(t, VarLabels[i], StringComparison.OrdinalIgnoreCase))
                return LabelledVars[i];
        return text;
    }

    public static bool IsLabelled(string name) => Array.IndexOf(LabelledVars, name) >= 0;

    /// <summary>Replaces $Model, _$Model... in a text with their shown names (template descriptions, hints).</summary>
    public static string FriendlyText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0) return text;
        // longest first, so $ActionID is not read as $Active... and _$X before $X
        foreach (var n in LabelledVars.OrderByDescending(n => n.Length))
            text = text.Replace("_" + n, VarLabel(n)).Replace(n, VarLabel(n));
        return text;
    }

    /// <summary>Value type of a model property (?getModel[...].prop), with fixed choices where it has them.</summary>
    public static VType ModelPropType(string prop, out string[] choices)
    {
        choices = null;
        switch (prop)
        {
            case "direction":
            case "isControlled":
            case "isCameraFollow": return VType.Bool;
            case "animationName": return VType.Animation;
            case "animationFrame": return VType.UInt;
            case "condition": choices = Results; return VType.String;
            case "AI": return VType.Int;
            default: return VType.String;
        }
    }

    // ---------------- Events ----------------

    public static readonly EntryDef[] Events =
    {
        new EntryDef("Enter", "Enter", "Model Enters the Trigger", "Model Enters the Trigger",
            "Fires once when a model's node crosses into the trigger area."),

        new EntryDef("Exit", "Exit", "Model Leaves the Trigger", "Model Leaves the Trigger",
            "Fires once when the model leaves the trigger area."),

        new EntryDef("Collision", "Collision", "Model is Touching the Trigger", "Model is Touching the Trigger",
            "Same as Enter."),

        new EntryDef("KeyPressed", "KeyPressed", "A key is Pressed", "A key is Pressed",
            "Fires on arrow keys press inside the Trigger."),

        new EntryDef("Timeout", "Timeout", "Timer Finished", "Timer Finished",
            "Fires when the timer started by the 'Start Timer' action runs out."),

        new EntryDef("Activate", "Activate", "Signal Received", "Signal Received",
            "Fires when another loop sends a signal. Compare Signal's Name in 'Only If' to pick which one.",
            new ParamDef("ActionID", "Only this signal", VType.String, "", null,
                "Leave empty to catch every signal and filter it in 'Only If'.", true)),

        new EntryDef("ValueChange", "ValueChange", "A Value Changes", "A Value Changes",
            "Fires whenever the value becomes different.",
            new ParamDef("Value", "Watch", VType.String, "", null, "Usually a model property, e.g. Player's animation name.").Queries()),

        new EntryDef("OnShow", "OnShow", "Trigger Appears on Screen", "Trigger Appears on Screen", ""),
        new EntryDef("OnHide", "OnHide", "Trigger Leaves the Screen", "Trigger Leaves the Screen", ""),

        new EntryDef("OnStartGame", "OnStartGame", "Level Starts", "Level Starts",
            "Fires once when the level loads."),

        new EntryDef("Line", "Line", "Model Crosses a Line", "Model Crosses a Line", "",
            new ParamDef("Type", "Line", VType.Orientation, "Vertical", LineOrientations),
            new ParamDef("Position", "Position", VType.Number, "0")),
    };

    // ---------------- Actions ----------------

    public static readonly EntryDef[] Actions =
    {
        // model & animation
        new EntryDef("ForceAnimation", "ForceAnimation", "Force Animation", "Model/Force Animation",
            "Play Animation on a Model.",
            new ParamDef("Name", "Animation", VType.Animation, "RunForward"),
            new ParamDef("Model", "On model", VType.Model, "_$Model"),
            new ParamDef("Frame", "Start frame", VType.UInt, "1"),
            new ParamDef("Reversed", "Reversed", VType.Bool, "0", null, "", true)),

        new EntryDef("ModelExecute", "ModelExecute", "Play Object Animation", "Model/Play Object Animation",
            "Runs an animation on the object this Trigger belongs to.",
            new ParamDef("AnimName", "Animation", VType.Animation, "_AnimName"),
            new ParamDef("AnimFrame", "Frame", VType.UInt, "_AnimFrame")),

        new EntryDef("Kill", "Kill", "Kill Model", "Model/Kill Model", "",
            new ParamDef("Model", "Model", VType.Model, "_$Model")),

        new EntryDef("Control", "Control", "Player Control On/Off", "Model/Player Control On Off",
            "Off takes the controls away from the Player, On gives them back.",
            new ParamDef("Switch", "Control", VType.Bool, "Off").Toggle("On", "Off"),
            new ParamDef("Model", "Model", VType.Model, "_$Model")),

        new EntryDef("Press", "Press", "Press a Key for the model", "Model/Press a Key for the model",
            "Makes the model act as if that key was pressed, used to control bots.",
            new ParamDef("Key", "Key", VType.Key, "Up"),
            new ParamDef("Model", "Model", VType.Model, "_$Model")),

        new EntryDef("Spawn", "Spawn", "Spawn Model", "Model/Spawn Model", "",
            new ParamDef("Model", "Model", VType.Model, "Hunter"),
            new ParamDef("Spawn", "At spawn point", VType.Spawn, "Respawn", null,
                "Objects tagged 'Spawn' in the open scene.")),

        // camera: the game reads one attribute per <Camera>, so each one is its own entry
        new EntryDef("Camera.Follow", "Camera", "Camera Follows Model", "Camera/Follow Model", "",
            new ParamDef("Follow", "Model", VType.Model, "_$Model")),

        new EntryDef("Camera.Zoom", "Camera", "Camera Zoom", "Camera/Zoom", "",
            new ParamDef("Zoom", "Zoom", VType.Zoom, "1")),

        new EntryDef("Camera.Smoothness", "Camera", "Camera Smoothness", "Camera/Smoothness",
            "Higher is lazier, the camera lags further behind the model.",
            new ParamDef("Smoothness", "Smoothness", VType.UInt, "100")),

        new EntryDef("Camera.Stop", "Camera", "Camera Stops Following", "Camera/Stop Following", "",
            new ParamDef("Stop", "Stop", VType.Bool, "1")),

        // variables
        new EntryDef("SetVariable", "SetVariable", "Set Variable", "Variables/Set Variable", "",
            new ParamDef("Name", "Variable", VType.String, "Flag1").AsVarName(),
            new ParamDef("Value", "To", VType.String, "1").FollowsVariable()),

        new EntryDef("AppendValue", "AppendValue", "Add to Variable", "Variables/Add to Variable",
            "Adds to a number, or sticks text onto the end of a string.",
            new ParamDef("Name", "Variable", VType.String, "Flag1").AsVarName(),
            new ParamDef("Value", "Add", VType.String, "1").FollowsVariable()),

        // flow
        new EntryDef("Wait", "Wait", "Wait", "Flow/Wait",
            "Holds the rest of the actions for this many frames.",
            new ParamDef("Frames", "Frames", VType.UInt, "30")),

        new EntryDef("SetTimer", "SetTimer", "Start Timer", "Flow/Start Timer",
            "Starts the countdown that the 'Timer finished' event listens for.",
            new ParamDef("Frames", "Frames", VType.UInt, "60")),

        new EntryDef("Activate", "Activate", "Send Signal", "Flow/Send Signal",
            "Wakes up any loop listening for this signal, here or in another trigger.",
            new ParamDef("ActionID", "Signal", VType.String, "MySignal")),

        // object & world
        new EntryDef("Transform", "Transform", "Play Transformation", "Object/Play Transformation",
            "Runs a transformation defined on the object, by name.",
            new ParamDef("Name", "Transformation", VType.String, "")),

        new EntryDef("Execute", "Execute", "Execute This Object", "Object/Execute This Object",
            "Runs whatever behaviour the trigger's own object has."),

        new EntryDef("Use", "Use", "Use Item", "Object/Use Item", "",
            new ParamDef("Object", "Item", VType.String, "")),

        new EntryDef("Sound", "Sound", "Play Sound", "Game/Play Sound", "",
            new ParamDef("Name", "Sound", VType.Sound, "")),

        new EntryDef("MessageOnScreen", "MessageOnScreen", "Show Message", "Game/Show Message", "",
            new ParamDef("Text", "Text", VType.String, "Hello World!"),
            new ParamDef("Color", "Color", VType.Color, VTypes.White),
            // No AppearStyle / DisappearStyle: the game only ever fades the message in and out (Unity and
            // Vector Raylib ignore both). An existing file's attributes are kept as they are on save.
            new ParamDef("Frames", "Frames", VType.UInt, "120")),

        new EntryDef("EndGame", "EndGame", "End The Level", "Game/End The Level", "",
            new ParamDef("Result", "Result", VType.String, "Win", Results),
            new ParamDef("Model", "Model", VType.Model, "_$Model"),
            new ParamDef("Frames", "Frames", VType.UInt, "120")),
    };

    public static EntryDef FindEvent(string id)
    {
        for (int i = 0; i < Events.Length; i++) if (Events[i].id == id) return Events[i];
        return null;
    }

    public static EntryDef FindAction(string id)
    {
        for (int i = 0; i < Actions.Length; i++) if (Actions[i].id == id) return Actions[i];
        return null;
    }

    // <Camera> cant be told apart by tag, go by whichever attribute it has
    public static EntryDef MatchAction(string tag, IEnumerable<string> attrNames)
    {
        if (tag == "Camera")
        {
            foreach (var a in attrNames)
            {
                var d = FindAction("Camera." + a);
                if (d != null) return d;
            }
            return FindAction("Camera.Follow");
        }
        return FindAction(tag);
    }
}
