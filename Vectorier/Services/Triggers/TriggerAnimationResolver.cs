using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Vectorier.Services.Triggers;

/// <summary>A &lt;ForceAnimation&gt; a trigger runs, with its values resolved.</summary>
/// <param name="Animation">The move's name (empty when it could not be resolved).</param>
/// <param name="Frame">Start frame; -1 = the move's FirstFrame (the game's default).</param>
/// <param name="Reversed">Reversed="1".</param>
/// <param name="OnExit">The loop fires when the model leaves the trigger (Exit) rather than when it enters.</param>
/// <param name="LoopName">The loop it is in (for messages).</param>
public sealed record TriggerForcedAnimation(string Animation, int Frame, bool Reversed, bool OnExit, string LoopName);

/// <summary>
/// Finds the &lt;ForceAnimation&gt; actions a trigger runs, the way the game reads its &lt;Content&gt;: its own
/// loops and those of its &lt;Template&gt; (TriggerTemplates.xml), loops / events / actions taken from another
/// template ("Template.Loop"), and values read from the trigger's variables ("_AnimName" → the &lt;Init&gt;
/// SetVariable, else the loop's &lt;Using&gt; DefaultValue).
/// </summary>
public static class TriggerAnimationResolver
{
    private const int MaxDepth = 16;

    public static List<TriggerForcedAnimation> Find(string contentXml, XElement? templatesRoot)
    {
        var result = new List<TriggerForcedAnimation>();
        XElement content;
        try
        {
            content = XElement.Parse("<Content>" + (contentXml ?? string.Empty) + "</Content>");
        }
        catch
        {
            return result;
        }

        var templates = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var t in templatesRoot?.Elements("Template") ?? Enumerable.Empty<XElement>())
        {
            if ((string?)t.Attribute("Name") is { Length: > 0 } name)
                templates.TryAdd(name, t);
        }

        // Variables: the template's <Init> first, the trigger's own <Init> over them.
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        var loops = new List<XElement>();
        foreach (var use in content.Elements("Template"))
        {
            if ((string?)use.Attribute("Name") is { } name && templates.TryGetValue(name, out var template))
            {
                ReadInit(template.Element("Init"), vars);
                loops.AddRange(template.Elements("Loop"));
            }
        }
        ReadInit(content.Element("Init"), vars);
        loops.AddRange(content.Elements("Loop"));

        foreach (var loop in loops)
        {
            var resolved = ResolveLoop(loop, templates, 0);
            if (resolved == null)
                continue;
            var events = Section(resolved, "Events", templates, 0);
            bool enter = events.Any(e => e.Name.LocalName is "Enter" or "Collision");
            bool exit = events.Any(e => e.Name.LocalName == "Exit");
            var usingDefaults = UsingDefaults(loop, resolved, templates);
            foreach (var action in Section(resolved, "Actions", templates, 0).Where(a => a.Name.LocalName == "ForceAnimation"))
            {
                string name = Value((string?)action.Attribute("Name"), vars, usingDefaults) ?? string.Empty;
                string frameText = Value((string?)action.Attribute("Frame") ?? "-1", vars, usingDefaults) ?? "-1";
                string reversedText = Value((string?)action.Attribute("Reversed") ?? "0", vars, usingDefaults) ?? "0";
                int frame = int.TryParse(frameText, out int f) ? f : -1;
                bool reversed = int.TryParse(reversedText, out int r) && r == 1;
                result.Add(new TriggerForcedAnimation(name, frame, reversed, exit && !enter, (string?)loop.Attribute("Name") ?? string.Empty));
            }
        }
        return result;
    }

    private static void ReadInit(XElement? init, Dictionary<string, string> vars)
    {
        foreach (var set in init?.Elements("SetVariable") ?? Enumerable.Empty<XElement>())
        {
            if ((string?)set.Attribute("Name") is { Length: > 0 } name)
                vars[name] = (string?)set.Attribute("Value") ?? string.Empty;
        }
    }

    /// <summary>A loop written as &lt;Loop Template="Template.Loop"/&gt; is that template's loop.</summary>
    private static XElement? ResolveLoop(XElement loop, Dictionary<string, XElement> templates, int depth)
    {
        if (depth > MaxDepth)
            return null;
        if ((string?)loop.Attribute("Template") is not { Length: > 0 } reference)
            return loop;
        var target = FindTemplateLoop(reference, templates);
        return target == null ? null : ResolveLoop(target, templates, depth + 1);
    }

    private static XElement? FindTemplateLoop(string reference, Dictionary<string, XElement> templates)
    {
        int dot = reference.IndexOf('.');
        if (dot <= 0 || !templates.TryGetValue(reference[..dot], out var template))
            return null;
        string loopName = reference[(dot + 1)..];
        return template.Elements("Loop").FirstOrDefault(l => (string?)l.Attribute("Name") == loopName);
    }

    /// <summary>The children of a loop's &lt;Events&gt; / &lt;Actions&gt;, following Template="Template.Loop".</summary>
    private static List<XElement> Section(XElement loop, string section, Dictionary<string, XElement> templates, int depth)
    {
        var list = new List<XElement>();
        var element = loop.Element(section);
        if (element == null || depth > MaxDepth)
            return list;
        if ((string?)element.Attribute("Template") is { Length: > 0 } reference)
        {
            var source = FindTemplateLoop(reference, templates);
            if (source != null)
                list.AddRange(Section(ResolveLoop(source, templates, depth + 1) ?? source, section, templates, depth + 1));
        }
        list.AddRange(element.Elements());
        return list;
    }

    private static Dictionary<string, string> UsingDefaults(XElement loop, XElement resolved, Dictionary<string, XElement> templates)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in new[] { resolved, loop })
        {
            foreach (var v in source.Element("Using")?.Elements("Variable") ?? Enumerable.Empty<XElement>())
            {
                if ((string?)v.Attribute("Name") is { Length: > 0 } name)
                    defaults.TryAdd(name, (string?)v.Attribute("DefaultValue") ?? string.Empty);
            }
        }
        return defaults;
    }

    /// <summary>A literal, or "_Name": the variable's value (Init, then the Using default). Null when unknown.</summary>
    private static string? Value(string? raw, Dictionary<string, string> vars, Dictionary<string, string> defaults)
    {
        if (raw == null)
            return null;
        if (!raw.StartsWith('_'))
            return raw;
        string name = raw[1..];
        if (vars.TryGetValue(name, out var value))
            return value;
        return defaults.TryGetValue(name, out var def) ? def : null;
    }
}
