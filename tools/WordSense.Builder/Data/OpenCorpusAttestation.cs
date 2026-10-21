using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The attestation of inflected forms by the tagged tokens of the open part-of-speech corpus (<c>corpus/open</c>, CC0 1.0 over
///     public-domain and CC0 text): a token whose lemma and part of speech are given attests its spelling as a form of that lemma.
/// </summary>
/// <remarks>
///     <para>
///     Only the training splits are read. The attestation joins the authored forms as a second source for the generated forms,
///     because the authors were asked only for the forms that this corpus does not already attest (see "Authored forms" in
///     <c>data/PROVENANCE.md</c>). The map has the shape of <see cref="AuthoredForms.Attested"/>: a lower-case spelling to the
///     lower-case lemma and to the lemma with its part of speech (<c>bag/n</c>; adverbs file under <c>a</c>).
///     </para>
/// </remarks>
internal static class OpenCorpusAttestation
{
    private const int TokenColumns = 7;

    /// <summary>
    ///     Reads the attestation of the training splits of every part of the open corpus.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The lemmas by attested spelling; empty when the open corpus is absent.</returns>
    public static Dictionary<string, HashSet<string>> Read(string corpus)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var part in new[] { OpenCorpus.Representative, OpenCorpus.Targeted })
        {
            var path = OpenCorpus.DataFile(corpus, part, "train");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var columns = line.Split('\t');
                if (columns.Length < TokenColumns || Tag(columns[3]) is not { } tag)
                {
                    continue;
                }

                var form = columns[1].ToLowerInvariant();
                var lemma = columns[2].ToLowerInvariant();
                if (!result.TryGetValue(form, out var lemmas))
                {
                    result[form] = lemmas = new HashSet<string>(StringComparer.Ordinal);
                }

                lemmas.Add(lemma);
                lemmas.Add(lemma + "/" + tag);
            }
        }

        return result;
    }

    /// <summary>
    ///     Adds the attestation of a second source to a map.
    /// </summary>
    /// <param name="target">The map that receives the attestation.</param>
    /// <param name="source">The attestation to add.</param>
    public static void Merge(Dictionary<string, HashSet<string>> target, Dictionary<string, HashSet<string>> source)
    {
        foreach (var (form, lemmas) in source)
        {
            if (!target.TryGetValue(form, out var have))
            {
                target[form] = have = new HashSet<string>(StringComparer.Ordinal);
            }

            have.UnionWith(lemmas);
        }
    }

    private static string? Tag(string upos) => upos switch
    {
        "NOUN" or "PROPN" => "n",
        "VERB" or "AUX" => "v",
        "ADJ" or "ADV" => "a",
        _ => null,
    };
}
