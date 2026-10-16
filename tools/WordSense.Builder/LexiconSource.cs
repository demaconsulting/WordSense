using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The outcome of importing the Moby Part-of-Speech II list.
/// </summary>
/// <param name="Words">The kept words in ordinal order, each with its part-of-speech mask.</param>
/// <param name="Gated">The words held back by the class gate, in ordinal order, each with the mask they would have had.</param>
/// <param name="Counts">The counts of every filter step, in the order the steps are applied.</param>
internal sealed record LexiconImport(
    IReadOnlyList<(string Word, byte Mask)> Words,
    IReadOnlyList<(string Word, byte Mask)> Gated,
    LexiconCounts Counts);

/// <summary>
///     The counts of the import filter steps.
/// </summary>
/// <param name="Entries">Lines of the input that hold a word and codes.</param>
/// <param name="SingleWords">Distinct words that are single lower-case words of the letters <c>a-z</c>.</param>
/// <param name="DroppedDeterminer">Words dropped because a code marks them as a determiner, article or numeral.</param>
/// <param name="DroppedNoCode">Words dropped because none of their codes maps to a part of speech.</param>
/// <param name="Participles">Adjective-only words ending in <c>-ed</c> or <c>-ing</c>, and <c>-ing</c> words of a verb stem, that gained the verb sense.</param>
/// <param name="Corrected">Words whose part-of-speech set was replaced by a correction.</param>
/// <param name="DroppedByClassGate">Words dropped because every part of speech they have is held back by the class gate.</param>
internal sealed record LexiconCounts(
    int Entries,
    int SingleWords,
    int DroppedDeterminer,
    int DroppedNoCode,
    int Participles,
    int Corrected,
    int DroppedByClassGate)
{
    /// <summary>
    ///     Gets the number of words that remain.
    /// </summary>
    public int Words => SingleWords - DroppedDeterminer - DroppedNoCode - DroppedByClassGate;
}

/// <summary>
///     The outcome of merging the dictionary with the trained lemmas.
/// </summary>
/// <param name="Entries">Every lemma in ordinal order: the trained ones and the dictionary words that remain.</param>
/// <param name="Shadowed">The number of dictionary words dropped because a trained lemma or a form spells them.</param>
internal sealed record LexiconMerge(IReadOnlyList<LemmaEntry> Entries, int Shadowed);

/// <summary>
///     Imports, reads, writes and merges the part-of-speech lexicon: the filtered form of the public-domain Moby
///     Part-of-Speech II word list.
/// </summary>
/// <remarks>
///     The committed files are <c>lexicon.tsv</c> (<c>word</c> TAB part-of-speech letters, ordinal order, LF),
///     <c>corrections.tsv</c>, <c>classes.tsv</c>, <c>morphology.tsv</c>, <c>gated.tsv</c> and <c>PROVENANCE.md</c>. The
///     letters are N noun, R pronoun, V verb, J adjective, B adverb, P preposition, C conjunction and I interjection,
///     in the order of <see cref="PartOfSpeech"/>.
/// </remarks>
internal static class LexiconSource
{
    /// <summary>
    ///     The letter of every part of speech, indexed by the value of <see cref="PartOfSpeech"/>.
    /// </summary>
    public const string Letters = "NRVJBPCI";

    /// <summary>
    ///     The name of the lexicon file.
    /// </summary>
    public const string LexiconFile = "lexicon.tsv";

    /// <summary>
    ///     The name of the corrections file.
    /// </summary>
    public const string CorrectionsFile = "corrections.tsv";

    /// <summary>
    ///     The name of the file that lists the classes held back by the class gate.
    /// </summary>
    public const string ClassesFile = "classes.tsv";

    /// <summary>
    ///     The name of the file that enables or disables the morphology rules.
    /// </summary>
    public const string MorphologyFile = "morphology.tsv";

    /// <summary>
    ///     The name of the file that lists the words held back by the class gate.
    /// </summary>
    public const string GatedFile = "gated.tsv";

    /// <summary>
    ///     The name of the provenance file.
    /// </summary>
    public const string ProvenanceFile = "PROVENANCE.md";

    /// <summary>
    ///     The class mode of a class that produces dictionary answers.
    /// </summary>
    public const string AnswerMode = "answer";

    /// <summary>
    ///     The class mode of a class that fails the precision gate and so only appears among candidates.
    /// </summary>
    public const string CandidateOnlyMode = "candidate-only";

    /// <summary>
    ///     The state of an enabled morphology rule.
    /// </summary>
    public const string EnabledState = "enabled";

    /// <summary>
    ///     The state of a disabled morphology rule.
    /// </summary>
    public const string DisabledState = "disabled";

    /// <summary>
    ///     The URL of the source list.
    /// </summary>
#pragma warning disable S1075 // the URL is documentation text for the provenance file
    public const string SourceUrl = "https://www.gutenberg.org/ebooks/3203";
#pragma warning restore S1075

    /// <summary>
    ///     Gets the part-of-speech mask bit of a part of speech.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The bit.</returns>
    public static byte Bit(PartOfSpeech pos) => (byte)(1 << (int)pos);

    /// <summary>
    ///     Parses the Moby Part-of-Speech II text and applies the pinned policy.
    /// </summary>
    /// <param name="text">The file content, decoded as Latin-1; entries end with a line feed and read <c>word\codes</c>.</param>
    /// <param name="corrections">The part-of-speech mask that replaces the one of a word, by word.</param>
    /// <param name="candidateOnly">The classes held back by the class gate.</param>
    /// <returns>The kept words, the gated words and the counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when a correction names a word that the filtered list does not hold.</exception>
    /// <remarks>
    ///     <para>
    ///     Only single lower-case words of the letters <c>a-z</c> are kept. The codes map as follows: <c>N</c>,
    ///     <c>h</c> (noun phrase) and <c>p</c> (plural) are a noun; <c>V</c> is the ordinary verb flag, and its sub-types
    ///     <c>t</c> and <c>i</c> are a verb too; <c>A</c> is an adjective; <c>v</c> is an adverb; <c>r</c> is a pronoun;
    ///     <c>P</c> is a preposition; <c>C</c> is a conjunction; <c>!</c> is an interjection. A word with a determiner,
    ///     article or numeral code (<c>D</c>, <c>I</c> or <c>o</c>) is dropped: determiners stay unknown. A word whose
    ///     codes map to nothing is dropped.
    ///     </para>
    ///     <para>
    ///     A word that is only an adjective and ends in <c>-ed</c> or <c>-ing</c> becomes adjective and verb, because
    ///     real text uses such participles as verbs; so does an <c>-ing</c> word of any other class whose stem is a
    ///     verb (gerunds such as 'being'). Corrections then replace the set of a word. Finally a word whose
    ///     every part of speech is held back by the class gate is not emitted, because with a single part of speech it
    ///     would be answered directly.
    ///     </para>
    /// </remarks>
    public static LexiconImport Parse(string text, IReadOnlyDictionary<string, byte> corrections, IReadOnlySet<PartOfSpeech> candidateOnly)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(candidateOnly);
        var heldBack = candidateOnly.Aggregate(0, (m, p) => m | Bit(p));

        // Collect the codes of every distinct single word; a word listed twice counts once
        var seen = new SortedDictionary<string, (int Mask, bool Determiner)>(StringComparer.Ordinal);
        var entries = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var split = line.LastIndexOf('\\');
            if (split <= 0)
            {
                continue;
            }

            entries++;
            var word = line[..split];
            if (!IsLowerWord(word))
            {
                continue;
            }

            var codes = line[(split + 1)..];
            var (mask, determiner) = seen.GetValueOrDefault(word);
            seen[word] = (mask | MapCodes(codes), determiner || codes.AsSpan().IndexOfAny("DIo") >= 0);
        }

        int dropDeterminer = 0, dropNoCode = 0, participles = 0, corrected = 0;
        var verbs = seen.Where(s => (s.Value.Mask & Bit(PartOfSpeech.Verb)) != 0).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var kept = new SortedDictionary<string, byte>(StringComparer.Ordinal);
        foreach (var (word, (rawMask, determiner)) in seen)
        {
            if (determiner)
            {
                dropDeterminer++;
                continue;
            }

            if (rawMask == 0)
            {
                dropNoCode++;
                continue;
            }

            var mask = rawMask;
            // Participles are listed as adjectives and gerunds such as 'being' as nouns, but real text uses them as verbs
            var adjectiveParticiple = mask == Bit(PartOfSpeech.Adjective) &&
                (word.EndsWith("ed", StringComparison.Ordinal) || word.EndsWith("ing", StringComparison.Ordinal));
            if (adjectiveParticiple || ((mask & Bit(PartOfSpeech.Verb)) == 0 && IsGerundOfVerb(word, verbs)))
            {
                mask |= Bit(PartOfSpeech.Verb);
                participles++;
            }

            kept[word] = (byte)mask;
        }

        foreach (var (word, mask) in corrections.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (!kept.ContainsKey(word))
            {
                throw new InvalidDataException($"The correction for '{word}' names a word that the filtered list does not hold.");
            }

            kept[word] = mask;
            corrected++;
        }

        var words = new List<(string Word, byte Mask)>(kept.Count);
        var gated = new List<(string Word, byte Mask)>();
        foreach (var (word, mask) in kept)
        {
            (((mask & ~heldBack) == 0) ? gated : words).Add((word, mask));
        }

        return new LexiconImport(words, gated, new LexiconCounts(entries, seen.Count, dropDeterminer, dropNoCode, participles, corrected, gated.Count));
    }

    /// <summary>
    ///     Determines whether a word is the <c>-ing</c> form of a Moby verb: the base is the stem, the stem plus
    ///     <c>e</c>, or the stem without a doubled final consonant.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="verbs">The words that have a verb code.</param>
    /// <returns>True when the word is a gerund of a verb.</returns>
    private static bool IsGerundOfVerb(string word, HashSet<string> verbs)
    {
        if (word.Length < 5 || !word.EndsWith("ing", StringComparison.Ordinal))
        {
            return false;
        }

        var stem = word[..^3];
        return verbs.Contains(stem) || verbs.Contains(stem + "e") || (stem.Length > 2 && stem[^1] == stem[^2] && verbs.Contains(stem[..^1]));
    }

    /// <summary>
    ///     Maps the Moby codes of one entry to a part-of-speech mask.
    /// </summary>
    /// <param name="codes">The code letters.</param>
    /// <returns>The mask; 0 when no code maps.</returns>
    private static int MapCodes(string codes)
    {
        var mask = 0;
        foreach (var code in codes)
        {
            mask |= code switch
            {
                'N' or 'h' or 'p' => Bit(PartOfSpeech.Noun),
                'V' or 't' or 'i' => Bit(PartOfSpeech.Verb),
                'A' => Bit(PartOfSpeech.Adjective),
                'v' => Bit(PartOfSpeech.Adverb),
                'r' => Bit(PartOfSpeech.Pronoun),
                'P' => Bit(PartOfSpeech.Preposition),
                'C' => Bit(PartOfSpeech.Conjunction),
                '!' => Bit(PartOfSpeech.Interjection),
                _ => 0,
            };
        }

        return mask;
    }

    /// <summary>
    ///     Determines whether a word is a single lower-case word of the letters <c>a-z</c>.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>True when it is.</returns>
    private static bool IsLowerWord(string word) => word.Length is > 0 and <= 255 && word.All(c => c is >= 'a' and <= 'z');

    /// <summary>
    ///     Converts a part-of-speech mask to its letters, in the order of <see cref="PartOfSpeech"/>.
    /// </summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The letters.</returns>
    public static string ToLetters(byte mask)
    {
        var text = new StringBuilder(4);
        for (var i = 0; i < Letters.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                text.Append(Letters[i]);
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     Converts part-of-speech letters to a mask.
    /// </summary>
    /// <param name="letters">The letters, in the order of <see cref="PartOfSpeech"/> and without repeats.</param>
    /// <param name="context">A description of the place of the letters, for the error message.</param>
    /// <returns>The mask.</returns>
    /// <exception cref="InvalidDataException">Thrown when the letters are empty, unknown, repeated or out of order.</exception>
    public static byte ToMask(string letters, string context)
    {
        var mask = 0;
        var last = -1;
        foreach (var letter in letters)
        {
            var index = Letters.IndexOf(letter, StringComparison.Ordinal);
            if (index <= last)
            {
                throw new InvalidDataException($"{context}: '{letters}' is not a set of part-of-speech letters from '{Letters}' in order.");
            }

            mask |= 1 << index;
            last = index;
        }

        return mask == 0 ? throw new InvalidDataException($"{context}: the part-of-speech set is empty.") : (byte)mask;
    }

    /// <summary>
    ///     Renders words as TSV: <c>word</c> TAB letters, one line each, LF line ends.
    /// </summary>
    /// <param name="words">The words in ordinal order.</param>
    /// <returns>The text.</returns>
    public static string RenderWords(IEnumerable<(string Word, byte Mask)> words)
    {
        var text = new StringBuilder();
        foreach (var (word, mask) in words)
        {
            text.Append(word).Append('\t').Append(ToLetters(mask)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads a lexicon TSV, rejecting anything that is not strictly ascending lower-case words with letter sets.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The words with their masks, in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed or out of order.</exception>
    public static List<(string Word, byte Mask)> ReadWords(string path)
    {
        var words = new List<(string Word, byte Mask)>();
        var number = 0;
        foreach (var line in File.ReadAllText(path, Encoding.UTF8).Split('\n'))
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            var where = $"{Path.GetFileName(path)} line {number}";
            if (columns.Length != 2 || !IsLowerWord(columns[0]))
            {
                throw new InvalidDataException($"{where}: expected 'word<TAB>letters' with a word of the letters a-z.");
            }

            if (words.Count > 0 && string.CompareOrdinal(words[^1].Word, columns[0]) >= 0)
            {
                throw new InvalidDataException($"{where}: the words are not unique and in ordinal order.");
            }

            words.Add((columns[0], ToMask(columns[1], where)));
        }

        return words;
    }

    /// <summary>
    ///     Reads a three-column configuration file (<c>key</c> TAB <c>value</c> TAB <c>evidence</c>).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The rows in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a row does not have three non-empty columns or repeats a key.</exception>
    private static List<(string Key, string Value, string Evidence)> ReadTriples(string path)
    {
        var rows = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var line in File.ReadAllText(path, Encoding.UTF8).Split('\n'))
        {
            number++;
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.TrimEnd('\r').Split('\t');
            if (columns.Length != 3 || columns.Any(c => c.Length == 0) || !seen.Add(columns[0]))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} line {number}: expected three non-empty columns and a unique first column.");
            }

            rows.Add((columns[0], columns[1], columns[2]));
        }

        return rows;
    }

    /// <summary>
    ///     Reads the corrections: <c>word</c> TAB letters TAB evidence.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The mask by word, in ordinal order.</returns>
    public static SortedDictionary<string, byte> ReadCorrections(string path)
    {
        var result = new SortedDictionary<string, byte>(StringComparer.Ordinal);
        foreach (var (word, letters, _) in ReadTriples(path))
        {
            if (!IsLowerWord(word))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}: '{word}' is not a word of the letters a-z.");
            }

            result[word] = ToMask(letters, $"{Path.GetFileName(path)} '{word}'");
        }

        return result;
    }

    /// <summary>
    ///     Reads the class modes: class name TAB <see cref="AnswerMode"/> or <see cref="CandidateOnlyMode"/> TAB evidence.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The mode by class, in the order of the file.</returns>
    public static List<(PartOfSpeech Pos, string Mode, string Evidence)> ReadClasses(string path)
    {
        var result = new List<(PartOfSpeech, string, string)>();
        foreach (var (name, mode, evidence) in ReadTriples(path))
        {
            if (!Enum.TryParse<PartOfSpeech>(name, out var pos) || !Enum.IsDefined(pos) || mode is not (AnswerMode or CandidateOnlyMode))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}: '{name}\t{mode}' is not a part of speech with the mode '{AnswerMode}' or '{CandidateOnlyMode}'.");
            }

            result.Add((pos, mode, evidence));
        }

        return result;
    }

    /// <summary>
    ///     Reads the morphology rule states: rule name TAB <see cref="EnabledState"/> or <see cref="DisabledState"/> TAB evidence.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The state by rule, in the order of the file; a rule that is not listed is disabled.</returns>
    public static List<(LemmaMorphologyRules Rule, bool Enabled, string Evidence)> ReadMorphology(string path)
    {
        var result = new List<(LemmaMorphologyRules, bool, string)>();
        foreach (var (name, state, evidence) in ReadTriples(path))
        {
            if (!Enum.TryParse<LemmaMorphologyRules>(name, out var rule) || rule is LemmaMorphologyRules.None or LemmaMorphologyRules.Default ||
                !Enum.IsDefined(rule) || state is not (EnabledState or DisabledState))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}: '{name}\t{state}' is not a morphology rule with the state '{EnabledState}' or '{DisabledState}'.");
            }

            result.Add((rule, state == EnabledState, evidence));
        }

        return result;
    }

    /// <summary>
    ///     Gets the enabled morphology rules of a configuration.
    /// </summary>
    /// <param name="rows">The rows of <see cref="ReadMorphology"/>.</param>
    /// <returns>The enabled rules combined.</returns>
    public static LemmaMorphologyRules EnabledRules(IEnumerable<(LemmaMorphologyRules Rule, bool Enabled, string Evidence)> rows)
    {
        return rows.Where(r => r.Enabled).Aggregate(LemmaMorphologyRules.None, (m, r) => m | r.Rule);
    }

    /// <summary>
    ///     Reads the morphology configuration next to a lexicon, if there is one.
    /// </summary>
    /// <param name="directory">The lexicon directory.</param>
    /// <returns>The rows, or null when the file does not exist.</returns>
    public static List<(LemmaMorphologyRules Rule, bool Enabled, string Evidence)>? ReadMorphologyIfPresent(string directory)
    {
        var path = Path.Combine(directory, MorphologyFile);
        return File.Exists(path) ? ReadMorphology(path) : null;
    }

    /// <summary>
    ///     Renders the provenance document.
    /// </summary>
    /// <param name="import">The import result.</param>
    /// <param name="inputName">The file name of the input.</param>
    /// <param name="inputBytes">The size of the input in bytes.</param>
    /// <param name="inputSha256">The upper-case hexadecimal SHA-256 of the input.</param>
    /// <param name="corrections">The corrections that were applied.</param>
    /// <param name="classes">The class modes that were applied.</param>
    /// <param name="morphology">The morphology rule states, or null when the configuration is absent.</param>
    /// <returns>The Markdown text with LF line ends.</returns>
    public static string RenderProvenance(
        LexiconImport import,
        string inputName,
        long inputBytes,
        string inputSha256,
        IReadOnlyDictionary<string, byte> corrections,
        IReadOnlyList<(PartOfSpeech Pos, string Mode, string Evidence)> classes,
        IReadOnlyList<(LemmaMorphologyRules Rule, bool Enabled, string Evidence)>? morphology)
    {
        var inv = CultureInfo.InvariantCulture;
        var c = import.Counts;
        var text = new StringBuilder();
        text.Append("# Lexicon Provenance\n\n");
        text.Append("<!-- Generated by 'WordSense.Builder import-moby'. Do not edit. -->\n\n");
        text.Append("## Source\n\n");
        text.Append("- **Work**: Moby Part-of-Speech II (`mobypos.txt`)\n");
        text.Append("- **Author**: Grady Ward\n");
        text.Append("- **Licence**: public domain (dedicated by the author)\n");
        text.Append("- **URL**: <").Append(SourceUrl).Append(">\n");
        text.Append("- **Input file**: `").Append(inputName).Append("`, ").Append(inputBytes.ToString(inv)).Append(" bytes\n");
        text.Append("- **Input SHA-256**: `").Append(inputSha256).Append("`\n");
        text.Append("- **Not committed**: the raw list; only the filtered lexicon is committed\n");
        text.Append("- **Not used**: no data from ASD-STE100 is used or embedded\n\n");
        text.Append("## Command\n\n");
        text.Append("```text\ndotnet run --project tools/WordSense.Builder -- import-moby --input mobypos.txt \\\n");
        text.Append("  --output corpus/lexicon/lexicon.tsv\n```\n\n");
        text.Append("The command reads `corrections.tsv`, `classes.tsv` and `morphology.tsv` from the directory of the output.\n\n");
        text.Append("## Filter Rules\n\n");
        text.Append("1. The input is read as Latin-1; entries are separated by line feeds and read `word\\codes`.\n");
        text.Append("2. Only single lower-case words of the letters `a-z` are kept (no spaces, hyphens, digits or capitals).\n");
        text.Append("3. A word with a determiner, article or numeral code (`D`, `I`, `o`) is dropped: determiners stay unknown.\n");
        text.Append("4. Codes map to parts of speech: `N`, `h` and `p` noun; `V`, `t` and `i` verb (`V` is the ordinary verb flag);\n");
        text.Append("   `A` adjective; `v` adverb; `r` pronoun; `P` preposition; `C` conjunction; `!` interjection.\n");
        text.Append("   A word without a mappable code is dropped.\n");
        text.Append("5. A word that is only an adjective and ends in `-ed` or `-ing` is adjective and verb.\n");
        text.Append("   An `-ing` word of five or more letters without a verb code gains the verb sense when its stem\n");
        text.Append("   (plain, plus `e`, or without a doubled final consonant) has a verb code.\n");
        text.Append("6. The corrections replace the set of a word.\n");
        text.Append("7. The class gate holds back the classes that failed the precision gate: a word whose every part of speech is\n");
        text.Append("   held back is not emitted (it is listed in `gated.tsv` for the evaluation).\n\n");
        text.Append("The output letters are N noun, R pronoun, V verb, J adjective, B adverb, P preposition, C conjunction and I\n");
        text.Append("interjection, in this order within a word.\n\n");
        text.Append("## Corrections\n\n");
        text.Append("| Word | Set |\n| --- | --- |\n");
        foreach (var (word, mask) in corrections)
        {
            text.Append("| ").Append(word).Append(" | ").Append(ToLetters(mask)).Append(" |\n");
        }

        text.Append("\nThe evidence of every correction is in `corrections.tsv`.\n\n");
        text.Append("## Class Gate\n\n");
        text.Append("| Class | Mode |\n| --- | --- |\n");
        foreach (var (pos, mode, _) in classes)
        {
            text.Append("| ").Append(pos).Append(" | ").Append(mode).Append(" |\n");
        }

        text.Append("\n## Morphology Rules\n\n");
        if (morphology is null)
        {
            text.Append("`morphology.tsv` was not present: every rule is enabled.\n\n");
        }
        else
        {
            text.Append("| Rule | State |\n| --- | --- |\n");
            foreach (var (rule, enabled, _) in morphology)
            {
                text.Append("| ").Append(rule).Append(" | ").Append(enabled ? EnabledState : DisabledState).Append(" |\n");
            }

            text.Append("\nThe evidence of every rule is in `morphology.tsv`.\n\n");
        }

        text.Append("## Counts\n\n");
        text.Append("| Step | Words |\n| --- | ---: |\n");
        text.Append("| Entries in the input | ").Append(c.Entries.ToString(inv)).Append(" |\n");
        text.Append("| Single lower-case words | ").Append(c.SingleWords.ToString(inv)).Append(" |\n");
        text.Append("| Dropped: determiner, article or numeral code | ").Append(c.DroppedDeterminer.ToString(inv)).Append(" |\n");
        text.Append("| Dropped: no mappable code | ").Append(c.DroppedNoCode.ToString(inv)).Append(" |\n");
        text.Append("| Dropped: class gate | ").Append(c.DroppedByClassGate.ToString(inv)).Append(" |\n");
        text.Append("| Participle and gerund words that gained the verb sense | ").Append(c.Participles.ToString(inv)).Append(" |\n");
        text.Append("| Words replaced by a correction | ").Append(c.Corrected.ToString(inv)).Append(" |\n");
        text.Append("| **Words in `lexicon.tsv`** | **").Append(c.Words.ToString(inv)).Append("** |\n\n");
        text.Append("Words in lexicon.tsv: ").Append(c.Words.ToString(inv)).Append('\n');
        return text.ToString();
    }

    /// <summary>
    ///     Imports the Moby list into the lexicon files.
    /// </summary>
    /// <param name="inputPath">The Moby file.</param>
    /// <param name="outputPath">The lexicon file to write; the other files are written next to it.</param>
    /// <param name="correctionsPath">The corrections file, or null for the one next to the output when it exists.</param>
    /// <param name="classesPath">The classes file, or null for the one next to the output when it exists.</param>
    /// <returns>The import result.</returns>
    public static LexiconImport Import(string inputPath, string outputPath, string? correctionsPath, string? classesPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        correctionsPath ??= Path.Combine(directory, CorrectionsFile);
        classesPath ??= Path.Combine(directory, ClassesFile);
        var corrections = File.Exists(correctionsPath) ? ReadCorrections(correctionsPath) : [];
        var classes = File.Exists(classesPath) ? ReadClasses(classesPath) : [];
        var candidateOnly = classes.Where(k => k.Mode == CandidateOnlyMode).Select(k => k.Pos).ToHashSet();
        var bytes = File.ReadAllBytes(inputPath);
        var import = Parse(Encoding.Latin1.GetString(bytes), corrections, candidateOnly);

        Directory.CreateDirectory(directory);
        var provenance = RenderProvenance(
            import,
            Path.GetFileName(inputPath),
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            corrections,
            classes,
            ReadMorphologyIfPresent(directory));
        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(outputPath, RenderWords(import.Words), utf8);
        File.WriteAllText(Path.Combine(directory, GatedFile), RenderWords(import.Gated), utf8);
        File.WriteAllText(Path.Combine(directory, ProvenanceFile), provenance, utf8);
        return import;
    }

    /// <summary>
    ///     Merges the dictionary words with the trained lemmas, the trained lemma taking precedence.
    /// </summary>
    /// <param name="trained">The trained lemmas in ordinal order.</param>
    /// <param name="forms">The form map; a form never spells a dictionary word.</param>
    /// <param name="words">The dictionary words in ordinal order.</param>
    /// <returns>Every lemma in ordinal order and the number of dictionary words that a trained lemma or a form shadowed.</returns>
    public static LexiconMerge Merge(
        IReadOnlyList<LemmaEntry> trained,
        IReadOnlyDictionary<string, string> forms,
        IReadOnlyList<(string Word, byte Mask)> words)
    {
        var taken = new HashSet<string>(trained.Select(t => t.Lemma), StringComparer.Ordinal);
        taken.UnionWith(forms.Keys);
        var all = new List<LemmaEntry>(trained);
        var shadowed = 0;
        foreach (var (word, mask) in words)
        {
            if (taken.Contains(word))
            {
                shadowed++;
                continue;
            }

            all.Add(WordSenseTable.CreateDictionaryEntry(word, mask));
        }

        all.Sort((a, b) => string.CompareOrdinal(a.Lemma, b.Lemma));
        return new LexiconMerge(all, shadowed);
    }
}
