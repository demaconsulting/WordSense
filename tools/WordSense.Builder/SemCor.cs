using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WordSense.Builder;

/// <summary>
///     One word of SemCor that carries a sense annotation.
/// </summary>
/// <param name="Index">The index of the word in the words of its sentence.</param>
/// <param name="Form">The word as written.</param>
/// <param name="SenseKeys">The WordNet 3.0 sense keys of the annotation (<c>lemma%lexsn</c>); more than one when the annotators gave several.</param>
internal sealed record SemCorTarget(int Index, string Form, IReadOnlyList<string> SenseKeys);

/// <summary>
///     One sentence of SemCor.
/// </summary>
/// <param name="Id">The id: the file name and the sentence number, for example <c>br-a01:1</c>.</param>
/// <param name="Words">The words of the sentence, punctuation as separate words.</param>
/// <param name="Targets">The words that carry exactly one single-word sense annotation.</param>
internal sealed record SemCorSentence(string Id, IReadOnlyList<string> Words, IReadOnlyList<SemCorTarget> Targets);

/// <summary>
///     What the reader found in SemCor, so that a report can say how many annotations are used.
/// </summary>
/// <param name="Sentences">The sentences that have at least one annotated word.</param>
/// <param name="Annotated">The annotated words (a sense annotation, whatever its form).</param>
/// <param name="MultiWord">The annotated multiword expressions, which are left out.</param>
/// <param name="MissingSense">The annotated words whose sense was dropped by the mapping from WordNet 1.6 to 3.0 (<c>wnsn=0</c>), which are left out.</param>
/// <param name="Used">The annotated single words that are targets.</param>
internal sealed record SemCorCounts(int Sentences, int Annotated, int MultiWord, int MissingSense, int Used);

/// <summary>
///     Reads SemCor 3.0, the sense-annotated part of the Brown Corpus, for the independent evaluation of the models.
/// </summary>
/// <remarks>
///     <para>
///     SemCor 3.0 (Mihalcea's automatic mapping of the Princeton SemCor 1.6 to WordNet 3.0) is distributed under the
///     license of WordNet: use, copy, modify and distribute for any purpose without fee, provided that the copyright
///     notice and the disclaimer are kept on every copy. The builder only downloads the archive to a cache that is not
///     part of the repository, reads it and reports scores; it never writes a sentence of it to any file of the
///     repository and never trains on it (<c>tools/open-corpus/cache</c> is ignored by git). The notice is in
///     <c>data/PROVENANCE.md</c>.
///     </para>
///     <para>
///     A tag file has one <c>&lt;wf ...&gt;word&lt;/wf&gt;</c> element for every word and one <c>&lt;punc&gt;</c> element for
///     every punctuation mark; an annotated word has the attributes <c>lemma</c>, <c>wnsn</c> and <c>lexsn</c>, whose
///     <c>lexsn</c> with the lemma is the WordNet sense key (<c>group%1:03:00::</c>).
///     </para>
/// </remarks>
internal static partial class SemCor
{
    /// <summary>The place the archive is downloaded from.</summary>
#pragma warning disable S1075 // the pinned download place of an evaluation corpus is a constant of the tool, verified by its SHA-256
    public const string ArchiveUrl = "https://web.eecs.umich.edu/~mihalcea/downloads/semcor/semcor3.0.tar.gz";
#pragma warning restore S1075

    /// <summary>The name of the archive in the cache.</summary>
    public const string ArchiveName = "semcor3.0.tar.gz";

    /// <summary>The pinned SHA-256 of the archive.</summary>
    public const string ArchiveSha256 = "A8000014D6FC864F8BD9D83C62BE601151CADD617C6554A39A1AD38B4B3F017B";

    private static readonly string[] Clitics = ["n't", "'s", "'re", "'ve", "'ll", "'d", "'m"];

    /// <summary>
    ///     Gets the cache directory below the repository (shared with the other third-party downloads, ignored by git).
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The directory.</returns>
    public static string CacheDirectory(string repository) => Path.Join(repository, "tools", "open-corpus", "cache");

    /// <summary>
    ///     Checks the SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="sha256">The expected hash, hexadecimal.</param>
    /// <exception cref="InvalidDataException">Thrown when the hash differs.</exception>
    public static void Verify(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The file '{path}' has the SHA-256 {actual}, not the pinned {sha256}.");
        }
    }

    /// <summary>
    ///     Downloads the archive to a file and verifies its SHA-256; a download with another hash is deleted and rejected.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="url">The address.</param>
    /// <param name="destination">The file to write.</param>
    /// <param name="sha256">The pinned hash, hexadecimal.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the verified file is in place.</returns>
    /// <exception cref="InvalidDataException">Thrown when the downloaded file has another hash.</exception>
    public static async Task DownloadAsync(HttpClient client, Uri url, string destination, string sha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temporary = destination + ".part";
        try
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = File.Create(temporary);
                await network.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            Verify(temporary, sha256);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    ///     Reads the sentences of the tag files of the archive, after verifying its SHA-256.
    /// </summary>
    /// <param name="archive">The <c>semcor3.0.tar.gz</c> file.</param>
    /// <param name="sha256">The pinned hash, hexadecimal.</param>
    /// <returns>The sentences that have at least one annotated word, in file order, and the counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the hash differs.</exception>
    public static (IReadOnlyList<SemCorSentence> Sentences, SemCorCounts Counts) ReadArchive(string archive, string sha256)
    {
        Verify(archive, sha256);
        var files = new List<(string Name, string Text)>();
        using (var stream = File.OpenRead(archive))
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
        using (var tar = new TarReader(gzip))
        {
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType == TarEntryType.RegularFile && entry.DataStream is { } data && entry.Name.Contains("/tagfiles/", StringComparison.Ordinal))
                {
                    using var reader = new StreamReader(data, Encoding.Latin1);
                    files.Add((Path.GetFileName(entry.Name), reader.ReadToEnd()));
                }
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return Parse(files);
    }

    /// <summary>
    ///     Parses tag files.
    /// </summary>
    /// <param name="files">The name and the text of every tag file.</param>
    /// <returns>The sentences that have at least one annotated word, and the counts.</returns>
    public static (IReadOnlyList<SemCorSentence> Sentences, SemCorCounts Counts) Parse(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sentences = new List<SemCorSentence>();
        int annotated = 0, multiWord = 0, missing = 0, used = 0;
        foreach (var (name, text) in files)
        {
            var words = new List<string>();
            var targets = new List<SemCorTarget>();
            var number = string.Empty;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("<s ", StringComparison.Ordinal))
                {
                    number = SentenceNumber().Match(line) is { Success: true } m ? m.Groups[1].Value : string.Empty;
                    words.Clear();
                    targets.Clear();
                }
                else if (line == "</s>")
                {
                    if (targets.Count > 0)
                    {
                        sentences.Add(new SemCorSentence(name + ":" + number, [.. words], [.. targets]));
                    }
                }
                else if (WordElement().Match(line) is { Success: true } word)
                {
                    var form = WebUtility.HtmlDecode(word.Groups[2].Value);
                    var attributes = Attributes().Matches(word.Groups[1].Value).ToDictionary(a => a.Groups[1].Value, a => a.Groups[2].Value, StringComparer.Ordinal);
                    AddWord(words, targets, form, attributes, ref annotated, ref multiWord, ref missing, ref used);
                }
                else if (line.StartsWith("<punc>", StringComparison.Ordinal))
                {
                    words.Add(WebUtility.HtmlDecode(line["<punc>".Length..line.LastIndexOf("</punc>", StringComparison.Ordinal)]));
                }
            }
        }

        return (sentences, new SemCorCounts(sentences.Count, annotated, multiWord, missing, used));
    }

    private static void AddWord(List<string> words, List<SemCorTarget> targets, string form, Dictionary<string, string> attributes, ref int annotated, ref int multiWord, ref int missing, ref int used)
    {
        var lemma = attributes.GetValueOrDefault("lemma");
        var tagged = attributes.ContainsKey("lexsn") && lemma is not null;
        if (tagged)
        {
            annotated++;
        }

        if (form.Contains('_', StringComparison.Ordinal))
        {
            words.AddRange(form.Split('_', StringSplitOptions.RemoveEmptyEntries));
            multiWord += tagged ? 1 : 0;
            return;
        }

        var glue = words.Count > 0 && Array.IndexOf(Clitics, form.ToLowerInvariant()) >= 0 && !targets.Any(t => t.Index == words.Count - 1);
        if (!tagged)
        {
            Append(words, form, glue);
            return;
        }

        if (attributes.TryGetValue("wnsn", out var wnsn) && wnsn == "0")
        {
            missing++;
            Append(words, form, glue);
            return;
        }

        var baseLemma = Parenthetical().Replace(lemma!, string.Empty).ToLowerInvariant();
        var keys = attributes["lexsn"].Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => baseLemma + "%" + s)
            .ToArray();
        used++;
        targets.Add(new SemCorTarget(words.Count, form, keys));
        words.Add(form);
    }

    private static void Append(List<string> words, string form, bool glue)
    {
        if (glue)
        {
            words[^1] += form;
        }
        else
        {
            words.Add(form);
        }
    }

    [GeneratedRegex(@"\(.*\)$", RegexOptions.None, 1000)]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"^<s\s+snum=(\d+)", RegexOptions.None, 1000)]
    private static partial Regex SentenceNumber();

    [GeneratedRegex(@"^<wf\s+([^>]*)>([^<]*)</wf>", RegexOptions.None, 1000)]
    private static partial Regex WordElement();

    [GeneratedRegex(@"(\w+)=(\S+)", RegexOptions.None, 1000)]
    private static partial Regex Attributes();
}
