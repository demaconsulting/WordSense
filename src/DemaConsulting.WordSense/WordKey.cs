using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     The parts of a word key, the one stable string that names the meaning of a classified word.
/// </summary>
/// <remarks>
///     <para>
///     The text form of a key is <c>{spelling}|{lemma}/{Part}[/{Features}][#{label}]</c>:
///     </para>
///     <list type="bullet">
///     <item><description><see cref="Spelling"/>: the written form as the lexicon looks it up, lower-cased and with the typographic apostrophe U+2019 straightened (<c>polish</c> for <c>Polish</c>). Two forms of one analysis can be spelled, and so pronounced, differently (<c>dreamt</c> and <c>dreamed</c>), so the spelling is part of the key.</description></item>
///     <item><description><see cref="Lemma"/>: the lemma of the analysis exactly as <see cref="WordDetails.Lemma"/> has it, with its case (<c>Polish</c>).</description></item>
///     <item><description><see cref="PartOfSpeech"/>: the name of the <see cref="DemaConsulting.WordSense.PartOfSpeech"/> value (<c>Noun</c>, <c>Verb</c>, ...).</description></item>
///     <item><description><see cref="Features"/>: <see cref="WordDetails.Features"/> as it is (<c>Feature=Value</c> items joined by <c>+</c> in the canonical order); the <c>/</c> and the features are left out when they are empty.</description></item>
///     <item><description><see cref="Label"/>: the part after <c>#</c> of a sense id (<c>sea-bass</c> for <c>bass#sea-bass</c>); left out, with its <c>#</c>, when the key names no sense.</description></item>
///     </list>
///     <para>
///     Escaping. A spelling, lemma, features text or label may contain the separators (a few lemmas contain <c>/</c>, for
///     example <c>A/C</c>). In the text form the characters <c>\</c>, <c>|</c>, <c>/</c> and <c>#</c> of such a part are
///     written with a leading backslash (<c>a\/c|A\/C/Noun/Number=Sing</c>); no other character is escaped, so almost every
///     key is written without any backslash. <see cref="TryParse"/> reverses <see cref="ToString"/> exactly.
///     </para>
///     <para>
///     A key is the key of a <see cref="ClassifyResult"/> (<see cref="ClassifyResult.Key"/>), and
///     <see cref="WordLexicon.Keys"/> lists every key that any model can return, so a consumer can attach its own data
///     (for example the phonemes of a speech synthesizer) to every key in advance. WordSense itself never carries
///     phonemes.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var key = new WordKey("bass", "bass", PartOfSpeech.Noun, "Number=Sing", "sea-bass");
///     Console.WriteLine(key);   // bass|bass/Noun/Number=Sing#sea-bass
///
///     if (WordKey.TryParse("polish|Polish/Noun/Number=Sing", out WordKey? parsed))
///     {
///         Console.WriteLine($"{parsed.Lemma} {parsed.PartOfSpeech} {parsed.Label ?? "no sense"}");
///     }
///     </code>
/// </example>
/// <param name="Spelling">The written form, lower-cased with the apostrophe straightened.</param>
/// <param name="Lemma">The lemma of the analysis, with its case.</param>
/// <param name="PartOfSpeech">The part of speech of the analysis.</param>
/// <param name="Features">The features of the analysis, or an empty string.</param>
/// <param name="Label">The label of the sense (the part after <c>#</c> of its id), or <see langword="null"/> when the key names no sense.</param>
public sealed record WordKey(string Spelling, string Lemma, PartOfSpeech PartOfSpeech, string Features, string? Label)
{
    private const char Escape = '\\';
    private const string Separators = "\\|/#";

    /// <summary>
    ///     Gets the key as text: <c>{spelling}|{lemma}/{Part}[/{Features}][#{label}]</c>, with the escaping of the type
    ///     remarks.
    /// </summary>
    /// <returns>The text form of the key; <see cref="TryParse"/> gives the key back.</returns>
    public override string ToString() => Format(Spelling, Lemma, PartOfSpeech, Features, Label);

    /// <summary>
    ///     Parses the text form of a key.
    /// </summary>
    /// <param name="text">The key text, as <see cref="ToString"/> and <see cref="ClassifyResult.Key"/> write it.</param>
    /// <param name="key">Receives the key; <see langword="null"/> when the text is not a key.</param>
    /// <returns><see langword="true"/> when the text is a key in canonical form (a spelling that is its own lexicon key: lower case, straight apostrophe and at least one letter; a part of speech that exists, no empty part, every separator escaped); then <c>key.ToString()</c> equals <paramref name="text"/>.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out WordKey? key)
    {
        key = null;
        if (text is null)
        {
            return false;
        }

        var i = 0;
        if (!ReadField(text, ref i, "|", out var spelling) || !Take(text, ref i, '|')
            || !ReadField(text, ref i, "/", out var lemma) || !Take(text, ref i, '/')
            || !ReadField(text, ref i, "/#", out var part))
        {
            return false;
        }

        if (!Enum.TryParse<PartOfSpeech>(part, out var pos) || !Enum.IsDefined(pos) || !string.Equals(pos.ToString(), part, StringComparison.Ordinal))
        {
            return false;
        }

        var features = string.Empty;
        if (Take(text, ref i, '/') && (!ReadField(text, ref i, "#", out features) || features.Length == 0))
        {
            return false;
        }

        string? label = null;
        if (Take(text, ref i, '#') && (!ReadField(text, ref i, string.Empty, out label) || label.Length == 0))
        {
            return false;
        }

        if (i != text.Length || spelling.Length == 0 || lemma.Length == 0
            || !string.Equals(BasicClassifier.Normalize(spelling), spelling, StringComparison.Ordinal))
        {
            return false;
        }

        key = new WordKey(spelling, lemma, pos, features, label);
        return true;
    }

    /// <summary>
    ///     Formats a key; the one place that defines the text form.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="lemma">The lemma.</param>
    /// <param name="pos">The part of speech.</param>
    /// <param name="features">The features; empty is left out.</param>
    /// <param name="label">The label; null or empty is left out.</param>
    /// <returns>The key text.</returns>
    internal static string Format(string spelling, string lemma, PartOfSpeech pos, string features, string? label)
    {
        var text = new StringBuilder(spelling.Length + lemma.Length + features.Length + 24);
        Append(text, spelling).Append('|');
        Append(text, lemma).Append('/').Append(pos.ToString());
        if (features.Length > 0)
        {
            Append(text.Append('/'), features);
        }

        if (!string.IsNullOrEmpty(label))
        {
            Append(text.Append('#'), label);
        }

        return text.ToString();
    }

    /// <summary>
    ///     Gets the label of a sense id: the part after the first <c>#</c>.
    /// </summary>
    /// <param name="senseId">The sense id, <c>lemma#label</c>.</param>
    /// <returns>The label; null when the id has no <c>#</c> or nothing after it.</returns>
    internal static string? LabelOf(string? senseId)
    {
        var hash = senseId?.IndexOf('#', StringComparison.Ordinal) ?? -1;
        return hash < 0 || hash == senseId!.Length - 1 ? null : senseId[(hash + 1)..];
    }

    /// <summary>
    ///     Derives the key of a classification result from its own fields only.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The key text; null for an unknown or unanswered word.</returns>
    internal static string? KeyOf(in ClassifyResult result)
    {
        var details = result.Details;
        if (!result.IsKnown || details is null)
        {
            return null;
        }

        var spelling = BasicClassifier.Normalize(result.Word);
        if (spelling.Length == 0)
        {
            return null;
        }

        var label = result.Level == SenseLevel.Sense && result.SenseId is not null
            ? LabelOf(result.SenseId)
            : LabelOf(TopRankedSense(result.Analyses, details));
        return Format(spelling, details.Lemma, details.PartOfSpeech, details.Features, label);
    }

    // The most probable ranked sense of the selected analysis (the ties by the ordinal order of the sense id, the order of
    // the ranking); the analyses of one slot share their senses, so the slot is the fallback when the exact analysis is
    // not listed, which a model of another package may do
    private static string? TopRankedSense(ImmutableArray<AnalysisCandidate> analyses, WordDetails details)
    {
        var senses = default(ImmutableArray<SenseCandidate>);
        foreach (var analysis in analyses)
        {
            if (analysis.Details.Equals(details))
            {
                senses = analysis.Senses;
                break;
            }

            if (senses.IsDefaultOrEmpty && analysis.Details.PartOfSpeech == details.PartOfSpeech && string.Equals(analysis.Details.Lemma, details.Lemma, StringComparison.Ordinal))
            {
                senses = analysis.Senses;
            }
        }

        if (senses.IsDefaultOrEmpty)
        {
            return null;
        }

        var best = senses[0];
        foreach (var sense in senses)
        {
            if (sense.Probability > best.Probability
                || (sense.Probability.CompareTo(best.Probability) == 0 && string.CompareOrdinal(sense.SenseId, best.SenseId) < 0))
            {
                best = sense;
            }
        }

        return best.SenseId;
    }

    private static StringBuilder Append(StringBuilder text, string part)
    {
        foreach (var c in part)
        {
            if (Separators.Contains(c, StringComparison.Ordinal))
            {
                text.Append(Escape);
            }

            text.Append(c);
        }

        return text;
    }

    private static bool Take(string text, ref int i, char c)
    {
        if (i < text.Length && text[i] == c)
        {
            i++;
            return true;
        }

        return false;
    }

    // Reads an unescaped field up to one of the terminators (not consumed) or the end; a separator that is not escaped
    // and not a terminator, or an escape of anything but a separator, makes the text not a key
    private static bool ReadField(string text, ref int i, string terminators, out string field)
    {
        var value = new StringBuilder();
        field = string.Empty;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == Escape)
            {
                if (i + 1 >= text.Length || !Separators.Contains(text[i + 1], StringComparison.Ordinal))
                {
                    return false;
                }

                value.Append(text[i + 1]);
                i += 2;
            }
            else if (terminators.Contains(c, StringComparison.Ordinal))
            {
                break;
            }
            else if (Separators.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
            else
            {
                value.Append(c);
                i++;
            }
        }

        field = value.ToString();
        return true;
    }
}
