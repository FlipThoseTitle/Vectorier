#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Vectorier.Gameplay.Diagnostics;

public enum GameErrorSeverity
{
    /// <summary>Something was wrong but the game went on (a missing texture, an unknown object).</summary>
    Warning,
    /// <summary>The game could not load, or stopped.</summary>
    Error,
    /// <summary>An error nothing expected: it reached the application's last handler.</summary>
    Crash,
}

/// <summary>
/// One problem of the game, traced to its source as far as it can be: the stage (loading the moves, the level,
/// running...), the file, the element with its line, the attribute, and what is wrong; the exception's type and
/// stack for the developer. Built from any exception by <see cref="From"/>.
/// </summary>
public sealed partial class GameErrorReport
{
    public DateTime Time { get; init; } = DateTime.Now;
    public GameErrorSeverity Severity { get; init; } = GameErrorSeverity.Error;

    /// <summary>What the game was doing: "Loading the moves", "Running the game"...</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>What is wrong, in a sentence.</summary>
    public string Problem { get; init; } = string.Empty;

    public string? File { get; init; }

    /// <summary>The element, e.g. &lt;Move Name="MonkeyToVault"&gt;.</summary>
    public string? Element { get; init; }

    /// <summary>Where the element is: root › Moves › &lt;Move Name="MonkeyToVault"&gt; › Interval #2.</summary>
    public string? ElementPath { get; init; }

    /// <summary>What uses the element, when the element is a shared definition (an event group, a set's object).</summary>
    public string? UsedBy { get; init; }

    public string? Attribute { get; init; }

    /// <summary>The attribute's value as written (when the attribute is known).</summary>
    public string? Value { get; init; }

    public int Line { get; init; }
    public int Column { get; init; }

    /// <summary>The game's code that met the error (Type.Method), for the developer.</summary>
    public string? CodeLocation { get; init; }

    public string? ExceptionType { get; init; }

    /// <summary>The exception in full (its message, inner exceptions and stack).</summary>
    public string? Details { get; init; }

    /// <summary>One line: the problem and where it is.</summary>
    public string Summary
    {
        get
        {
            var where = new List<string>();
            if (File != null)
                where.Add(Line > 0 ? $"{File}:{Line}" : File);
            if (Element != null)
                where.Add(Element);
            if (Attribute != null)
                where.Add("attribute \"" + Attribute + "\"");
            return where.Count > 0 ? $"{Problem} ({string.Join(", ", where)})" : Problem;
        }
    }

    /// <summary>A warning with no exception (the game went on).</summary>
    public static GameErrorReport Warning(string stage, string problem, string? file = null, XmlNode? node = null, string? attribute = null)
    {
        var (line, column) = XmlSource.Locate(node);
        return new GameErrorReport
        {
            Severity = GameErrorSeverity.Warning,
            Stage = stage,
            Problem = problem,
            File = file ?? XmlSource.FileOf(node),
            Element = node != null ? XmlSource.Describe(node) : null,
            ElementPath = node != null ? XmlSource.PathOf(node) : null,
            Attribute = attribute,
            Value = attribute != null ? node?.Attributes?[attribute]?.Value : null,
            Line = line,
            Column = column,
        };
    }

    /// <summary>Traces <paramref name="exception"/> to the game data it came from, as far as it can.</summary>
    public static GameErrorReport From(Exception exception, string stage, GameErrorSeverity severity = GameErrorSeverity.Error)
    {
        var root = Unwrap(exception);
        var data = root as GameDataException ?? Find<GameDataException>(exception);
        var xml = root as XmlException ?? Find<XmlException>(exception);

        // The element: the one the data error names, else the innermost one being read when it happened.
        XmlNode? node = data?.Node ?? XmlSource.NodeOf(root) ?? NotedNode(exception);
        string? file = data?.File ?? XmlSource.FileOf(node) ?? FileOf(root) ?? FileOf(exception);
        string? attribute = data?.Attribute ?? GuessAttribute(root, node);

        XmlNode? user = node != null ? XmlSource.UserOf(root, node) : null;
        string? usedBy = user == null ? null
            : XmlSource.FileOf(user) is { } userFile && userFile != file ? userFile + ": " + XmlSource.PathOf(user) : XmlSource.PathOf(user);

        int line = data?.Line ?? 0, column = data?.Column ?? 0;
        if (line == 0 && xml != null)
            (line, column) = (xml.LineNumber, xml.LinePosition);
        if (line == 0 && node != null)
            (line, column) = XmlSource.Locate(node);

        return new GameErrorReport
        {
            Severity = severity,
            Stage = stage,
            Problem = data?.Problem ?? Explain(root, xml),
            File = file,
            Element = node != null ? XmlSource.Describe(node) : null,
            ElementPath = node != null ? XmlSource.PathOf(node) : null,
            UsedBy = usedBy,
            Attribute = attribute,
            Value = attribute != null ? (node as XmlElement)?.Attributes[attribute]?.Value : null,
            Line = line,
            Column = column,
            CodeLocation = CodeLocationOf(root),
            ExceptionType = root.GetType().FullName,
            Details = exception.ToString(),
        };
    }

    // ================= TRACING ================= //

    /// <summary>The exception that started it (Parallel.ForEach and Task wrap the real one).</summary>
    private static Exception Unwrap(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case AggregateException aggregate when aggregate.InnerExceptions.Count > 0:
                    exception = aggregate.InnerExceptions[0];
                    continue;
                case System.Reflection.TargetInvocationException { InnerException: { } inner }:
                    exception = inner;
                    continue;
                case TypeInitializationException { InnerException: { } inner }:
                    exception = inner;
                    continue;
                default:
                    return exception;
            }
        }
    }

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? e = exception; e != null; e = e is AggregateException a && a.InnerExceptions.Count > 0 ? a.InnerExceptions[0] : e.InnerException)
            if (e is T found)
                return found;
        return null;
    }

    private static XmlNode? NotedNode(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e is AggregateException a && a.InnerExceptions.Count > 0 ? a.InnerExceptions[0] : e.InnerException)
            if (XmlSource.NodeOf(e) is { } node)
                return node;
        return null;
    }

    private static string? FileOf(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
            if (e.Data["GameFile"] is string file)
                return file;
        return null;
    }

    /// <summary>
    /// A value that could not be read: the exception quotes it ("The input string 'abc' was not in a correct
    /// format", "The given key 'x' was not present"), and the element's attribute holding it is the one at fault.
    /// </summary>
    private static string? GuessAttribute(Exception exception, XmlNode? node)
    {
        if (node?.Attributes == null || node.Attributes.Count == 0)
            return null;
        if (exception is not (FormatException or OverflowException or KeyNotFoundException or ArgumentException))
            return null;
        var match = QuotedValue().Match(exception.Message);
        if (!match.Success)
            return null;
        string value = match.Groups[1].Value;
        XmlAttribute? contains = null;
        foreach (XmlAttribute attribute in node.Attributes)
        {
            if (attribute.Value == value)
                return attribute.Name;
            if (value.Length > 0 && contains == null && attribute.Value.Contains(value, StringComparison.Ordinal))
                contains = attribute;
        }
        return contains?.Name;
    }

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex QuotedValue();

    /// <summary>What the exception means for the game's data, in a sentence.</summary>
    private static string Explain(Exception exception, XmlException? xml)
    {
        if (xml != null)
            return "The file is not valid XML: " + xml.Message;
        return exception switch
        {
            NullReferenceException => "A required element or attribute is missing (the game read a value that is not there).",
            FormatException => "A value is not written in the expected format: " + exception.Message,
            OverflowException => "A number is too large or too small: " + exception.Message,
            KeyNotFoundException => "Something is referred to by a name that does not exist: " + exception.Message,
            IndexOutOfRangeException or ArgumentOutOfRangeException => "A list has fewer entries than the data needs (e.g. a move's frames, or a value with too few parts): " + exception.Message,
            System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException => exception.Message,
            System.IO.EndOfStreamException => "A binary file ends too early (it is cut short or not the expected format): " + exception.Message,
            InvalidCastException => "A value has the wrong type: " + exception.Message,
            OutOfMemoryException => "The game ran out of memory.",
            StackOverflowException or InsufficientExecutionStackException => "The game went too deep (an object or trigger that refers to itself?).",
            _ => exception.Message,
        };
    }

    /// <summary>The game's method that met the error: the first frame of the stack in the editor's own code.</summary>
    private static string? CodeLocationOf(Exception exception)
    {
        try
        {
            foreach (var frame in new StackTrace(exception, false).GetFrames())
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                // Lambdas and iterators: the method they belong to.
                while (type?.DeclaringType != null && type.Name.StartsWith('<'))
                    type = type.DeclaringType;
                if (type?.Namespace?.StartsWith("Vectorier", StringComparison.Ordinal) != true
                    || type.Namespace.StartsWith("Vectorier.Gameplay.Diagnostics", StringComparison.Ordinal))
                    continue;
                string name = method!.Name;
                int open = name.IndexOf('<'), close = name.IndexOf('>');
                if (open >= 0 && close > open + 1)
                    name = name[(open + 1)..close];
                return type.Name + "." + name;
            }
        }
        catch
        {
        }
        return null;
    }

    // ================= TEXT ================= //

    /// <summary>The report as the logs and Copy write it.</summary>
    public string ToText(bool withDetails = true)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(Time.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").Append(Severity.ToString().ToUpperInvariant());
        if (Stage.Length > 0)
            sb.Append(" — ").Append(Stage);
        sb.AppendLine();
        sb.Append("  Problem:   ").AppendLine(Problem);
        if (File != null)
            sb.Append("  File:      ").AppendLine(Line > 0 ? $"{File} (line {Line}, column {Column})" : File);
        if (ElementPath != null)
            sb.Append("  Element:   ").AppendLine(ElementPath);
        if (UsedBy != null)
            sb.Append("  Used by:   ").AppendLine(UsedBy);
        if (Attribute != null)
            sb.Append("  Attribute: ").AppendLine(Value != null ? $"{Attribute} = \"{Value}\"" : Attribute);
        if (CodeLocation != null)
            sb.Append("  Code:      ").AppendLine(CodeLocation);
        if (withDetails && Details != null)
        {
            sb.AppendLine("  Details:");
            foreach (var line in Details.Split('\n'))
                sb.Append("    ").AppendLine(line.TrimEnd('\r'));
        }
        return sb.ToString();
    }

    public override string ToString() => Summary;
}
