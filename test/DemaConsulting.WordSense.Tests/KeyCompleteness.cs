using System.Text.RegularExpressions;
using WordSense.Builder;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     A sample check of the enumeration guarantee: the keys that a model gives are members of
///     <see cref="WordLexicon.Keys"/>. The words of the reviewed suite and of a deterministic sample of the held-out corpus
///     sentences are classified and every key is looked up in one pass over the enumeration. The guarantee itself holds by
///     construction, not by this sample: a key of a result is built (<c>WordKey.KeyOf</c>) from an analysis of the lookup of
///     its word and, at most, a catalog sense of that analysis, and the enumeration (<c>WordImage.Keys</c>) lists the key
///     of every analysis of every spelling and of every catalog sense of its lemma and part of speech, both through
///     <c>WordKey.Format</c> and the same normalization; the sample can only catch a mistake in that construction. The
///     file is shared by the test projects of the models (it is linked into them).
/// </summary>
internal static partial class KeyCompleteness
{
    /// <summary>
    ///     Every n-th row of the held-out corpus is a sentence of the check; about 570 sentences.
    /// </summary>
    private const int SampleStep = 20;

    /// <summary>
    ///     Classifies the sentences of the check with a model and lists the problems with the keys.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The problems: a known word without a key, an unknown word with a key, a key that is not enumerated; empty when all keys are in order.</returns>
    public static async Task<IReadOnlyList<string>> ProblemsAsync(IWordSenseModel model, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sentence in Sentences())
        {
            var results = await model.ClassifyAsync(sentence, cancellationToken).ConfigureAwait(false);
            foreach (var result in results)
            {
                var key = result.Key;
                if (result.IsKnown != (key is not null))
                {
                    problems.Add($"'{result.Word}': known {result.IsKnown} but key {key ?? "null"}");
                }
                else if (key is not null)
                {
                    observed.Add(key);
                }
            }
        }

        var seen = observed.Count;
        foreach (var key in WordLexicon.Keys())
        {
            observed.Remove(key);
        }

        problems.AddRange(observed.Select(k => $"key '{k}' is not enumerated"));
        if (seen <= 500)
        {
            problems.Add($"the check observed only {seen} distinct keys");
        }

        return problems;
    }

    /// <summary>
    ///     Reads the sentences of the check: the words of every suite row and the sampled held-out sentences.
    /// </summary>
    /// <returns>The sentences as lists of words.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Sentences()
    {
        var suiteDirectory = SuiteFile.Locate();
        var sentences = new List<IReadOnlyList<string>>(SuiteFile.Read(suiteDirectory).Rows.Select(r => r.Words));
        var holdout = Path.Join(Path.GetDirectoryName(suiteDirectory)!, "holdout", "relabel-holdout.tsv");
        var row = 0;
        foreach (var line in File.ReadLines(holdout).Skip(1))
        {
            if (row++ % SampleStep != 0)
            {
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length >= 4)
            {
                sentences.Add(Split(cells[3]));
            }
        }

        return sentences;
    }

    private static List<string> Split(string sentence) =>
        [.. WordPattern().Matches(sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal)).Select(m => m.Value)];

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+(?:['\u2019][\p{L}\p{M}]+)*|[^\s\p{L}\p{M}\p{N}]")]
    private static partial Regex WordPattern();
}
