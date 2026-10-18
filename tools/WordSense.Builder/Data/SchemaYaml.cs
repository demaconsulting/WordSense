using System.Globalization;

namespace WordSense.Builder.Data;

/// <summary>
///     Reports a malformed or inconsistent schema file together with the one-based line it was found at.
/// </summary>
/// <remarks>
///     It is converted to <see cref="InvalidDataException"/> with the file path by <see cref="DataSchema.Load"/>, so
///     callers of the schema reader see one error type.
/// </remarks>
public sealed class SchemaException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemaException"/> class.
    /// </summary>
    /// <param name="line">The one-based line, or 0 when the problem has no single line.</param>
    /// <param name="message">The reason.</param>
    public SchemaException(int line, string message)
        : base(message)
    {
        Line = line;
    }

    /// <summary>
    ///     Gets the one-based line of the problem, or 0.
    /// </summary>
    public int Line { get; }
}

/// <summary>
///     A reader for the restricted YAML subset used by <c>data/schema.yaml</c>.
/// </summary>
/// <remarks>
///     <para>
///     Supported: <c>key: value</c> mappings (nested by indentation), <c>- item</c> sequences, flow sequences
///     <c>[a, b]</c> of scalars, plain and single- or double-quoted scalars (no escapes), full-line and trailing
///     <c>#</c> comments, an optional <c>---</c> marker. Everything else (tabs, anchors, block scalars, nested flow
///     collections, duplicate keys) is rejected with its line number, so the schema stays simple enough to review.
///     </para>
///     <para>
///     The reader is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class SchemaYaml
{
    /// <summary>
    ///     A node of the parsed tree.
    /// </summary>
    /// <param name="line">The one-based source line of the node.</param>
    internal abstract class Node(int line)
    {
        /// <summary>
        ///     Gets the one-based source line of the node.
        /// </summary>
        public int Line { get; } = line;

        /// <summary>
        ///     Returns the node as a mapping.
        /// </summary>
        /// <returns>The mapping.</returns>
        /// <exception cref="SchemaException">Thrown when the node is not a mapping.</exception>
        public Mapping AsMap() => this as Mapping ?? throw new SchemaException(Line, "Expected a mapping.");

        /// <summary>
        ///     Returns the node as a sequence.
        /// </summary>
        /// <returns>The sequence.</returns>
        /// <exception cref="SchemaException">Thrown when the node is not a sequence.</exception>
        public Sequence AsSequence() => this as Sequence ?? throw new SchemaException(Line, "Expected a sequence.");

        /// <summary>
        ///     Returns the scalar text of the node.
        /// </summary>
        /// <returns>The text.</returns>
        /// <exception cref="SchemaException">Thrown when the node is not a scalar.</exception>
        public string AsScalar() => (this as Scalar ?? throw new SchemaException(Line, "Expected a scalar value.")).Text;
    }

    /// <summary>
    ///     A scalar value.
    /// </summary>
    internal sealed class Scalar(int line, string text) : Node(line)
    {
        /// <summary>
        ///     Gets the text of the scalar without quotes.
        /// </summary>
        public string Text { get; } = text;
    }

    /// <summary>
    ///     A sequence of nodes.
    /// </summary>
    internal sealed class Sequence(int line, List<Node> items) : Node(line)
    {
        /// <summary>
        ///     Gets the items in source order.
        /// </summary>
        public IReadOnlyList<Node> Items { get; } = items;
    }

    /// <summary>
    ///     A mapping that keeps the source order of its keys.
    /// </summary>
    internal sealed class Mapping(int line, List<KeyValuePair<string, Node>> entries) : Node(line)
    {
        /// <summary>
        ///     Gets the entries in source order.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, Node>> Entries { get; } = entries;

        /// <summary>
        ///     Gets the value of a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value node.</returns>
        /// <exception cref="SchemaException">Thrown when the key is missing.</exception>
        public Node Require(string key) =>
            TryGet(key, out var node) ? node : throw new SchemaException(Line, $"Missing key '{key}'.");

        /// <summary>
        ///     Tries to get the value of a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="node">The value when present.</param>
        /// <returns>True when the key exists.</returns>
        public bool TryGet(string key, out Node node)
        {
            foreach (var entry in Entries)
            {
                if (entry.Key == key)
                {
                    node = entry.Value;
                    return true;
                }
            }

            node = null!;
            return false;
        }
    }

    private readonly record struct Line(int Number, int Indent, string Text);

    /// <summary>
    ///     Parses the lines of a schema file.
    /// </summary>
    /// <param name="fileLines">The lines of the file.</param>
    /// <returns>The root node, a mapping.</returns>
    /// <exception cref="SchemaException">Thrown for any construct outside the supported subset.</exception>
    public static Node Parse(string[] fileLines)
    {
        var lines = Prepare(fileLines);
        if (lines.Count == 0)
        {
            throw new SchemaException(0, "The schema file is empty.");
        }

        var position = 0;
        var root = ParseBlock(lines, ref position, lines[0].Indent);
        if (position < lines.Count)
        {
            throw new SchemaException(lines[position].Number, "Unexpected indentation.");
        }

        return root;
    }

    private static List<Line> Prepare(string[] fileLines)
    {
        var result = new List<Line>();

        for (var i = 0; i < fileLines.Length; i++)
        {
            var number = i + 1;
            var raw = fileLines[i];
            if (raw.Contains('\t', StringComparison.Ordinal))
            {
                throw new SchemaException(number, "Tabs are not allowed.");
            }

            var text = StripComment(raw).TrimEnd();
            if (text.Length == 0 || text == "---")
            {
                continue;
            }

            result.Add(new Line(number, text.Length - text.TrimStart().Length, text.TrimStart()));
        }

        return result;
    }

    private static string StripComment(string raw)
    {
        // A '#' starts a comment at the start of a line or after a space, outside quotes
        char? quote = null;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (quote is not null)
            {
                quote = c == quote ? null : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '#' && (i == 0 || raw[i - 1] == ' '))
            {
                return raw[..i];
            }
        }

        return raw;
    }

    private static Node ParseBlock(List<Line> lines, ref int position, int indent)
    {
        return lines[position].Text.StartsWith("- ", StringComparison.Ordinal)
            ? ParseSequence(lines, ref position, indent)
            : ParseMapping(lines, ref position, indent);
    }

    private static Mapping ParseMapping(List<Line> lines, ref int position, int indent)
    {
        var first = lines[position];
        var entries = new List<KeyValuePair<string, Node>>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (position < lines.Count && lines[position].Indent == indent && !lines[position].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            var line = lines[position];
            var split = line.Text.IndexOf(':', StringComparison.Ordinal);
            if (split <= 0 || (split + 1 < line.Text.Length && line.Text[split + 1] != ' '))
            {
                throw new SchemaException(line.Number, "Expected 'key: value'.");
            }

            var key = line.Text[..split];
            if (!keys.Add(key))
            {
                throw new SchemaException(line.Number, $"Duplicate key '{key}'.");
            }

            var rest = line.Text[(split + 1)..].Trim();
            position++;
            entries.Add(new KeyValuePair<string, Node>(key, rest.Length > 0 ? ParseValue(rest, line.Number) : ParseChild(lines, ref position, indent, line.Number)));
        }

        if (position < lines.Count && lines[position].Indent > indent)
        {
            throw new SchemaException(lines[position].Number, "Unexpected indentation.");
        }

        return new Mapping(first.Number, entries);
    }

    private static Node ParseChild(List<Line> lines, ref int position, int indent, int parentLine)
    {
        // A key without an inline value is either an empty scalar or the owner of a deeper block
        if (position >= lines.Count || lines[position].Indent <= indent)
        {
            return new Scalar(parentLine, string.Empty);
        }

        return ParseBlock(lines, ref position, lines[position].Indent);
    }

    private static Sequence ParseSequence(List<Line> lines, ref int position, int indent)
    {
        var first = lines[position];
        var items = new List<Node>();
        while (position < lines.Count && lines[position].Indent == indent && lines[position].Text.StartsWith("- ", StringComparison.Ordinal))
        {
            var line = lines[position];
            items.Add(ParseValue(line.Text[2..].Trim(), line.Number));
            position++;
        }

        return new Sequence(first.Number, items);
    }

    private static Node ParseValue(string text, int line)
    {
        if (text.StartsWith('['))
        {
            return ParseFlowSequence(text, line);
        }

        return new Scalar(line, ParseScalar(text, line));
    }

    private static Sequence ParseFlowSequence(string text, int line)
    {
        if (!text.EndsWith(']'))
        {
            throw new SchemaException(line, "A flow sequence must end with ']' on the same line.");
        }

        var inner = text[1..^1].Trim();
        var items = new List<Node>();
        if (inner.Length > 0)
        {
            foreach (var part in inner.Split(','))
            {
                items.Add(new Scalar(line, ParseScalar(part.Trim(), line)));
            }
        }

        return new Sequence(line, items);
    }

    private static string ParseScalar(string text, int line)
    {
        if (text.Length >= 2 && text[0] is '"' or '\'')
        {
            if (text[^1] != text[0] || text.Length < 2)
            {
                throw new SchemaException(line, "Unterminated quoted scalar.");
            }

            return text[1..^1];
        }

        if (text.Length > 0 && (text[0] is '&' or '*' or '|' or '>' or '{' or '!' or '%' or '@' or '`'))
        {
            throw new SchemaException(line, $"Unsupported YAML construct '{text[0].ToString(CultureInfo.InvariantCulture)}'.");
        }

        return text;
    }
}
