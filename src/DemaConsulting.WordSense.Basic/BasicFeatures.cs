using System.Text;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     Turns the words around a target word into a set of hashed sparse features.
/// </summary>
/// <remarks>
///     <para>
///     The features describe a window of three words on each side of the target: the words themselves, the class of
///     function words (determiner, preposition, pronoun, auxiliary, conjunction), the possible parts of speech the word
///     table gives the neighbor, the ending of a neighbor, capitalization, the sentence boundaries and a few word pairs.
///     Every feature is hashed (FNV-1a, 32 bit) into a space of <see cref="HashSpace"/> ids, so no feature dictionary
///     is needed. The value of every active feature is <c>1 / sqrt(count)</c>, which gives every context a unit
///     length. The model and the trainer share this one implementation so they can never diverge.
///     </para>
/// </remarks>
internal static class BasicFeatures
{
    /// <summary>
    ///     The size of the hash space; feature ids are in <c>[0, HashSpace)</c>.
    /// </summary>
    public const int HashSpace = 1 << 18;

    private const int Window = 3;

    private static readonly Dictionary<string, string> FunctionClasses = BuildFunctionClasses();

    /// <summary>
    ///     Computes the features of one target word.
    /// </summary>
    /// <param name="words">The words of the sentence as written.</param>
    /// <param name="target">The index of the target word.</param>
    /// <param name="table">The word table that supplies the parts of speech of the neighbors.</param>
    /// <returns>The distinct, ascending feature ids and the value every one of them carries.</returns>
    public static (int[] Ids, float Value) Extract(IReadOnlyList<string> words, int target, IReadOnlyDictionary<string, WordDetails[]> table)
    {
        var ids = new HashSet<int>();
        void Add(string feature) => ids.Add(Hash(feature));

        // The sentence boundaries and the capitalization of the target
        if (target == 0)
        {
            Add("start");
        }

        if (target == words.Count - 1)
        {
            Add("end");
        }

        if (IsUpper(words[target]))
        {
            Add("cap0");
        }

        var keys = new string[(2 * Window) + 1];
        for (var o = -Window; o <= Window; o++)
        {
            var index = target + o;
            keys[o + Window] = index < 0 ? "<s>" : index >= words.Count ? "</s>" : Normalize(words[index]);
            if (o == 0 || index < 0 || index >= words.Count)
            {
                continue;
            }

            var key = keys[o + Window];
            Add($"w{o}={key}");
            if (FunctionClasses.TryGetValue(key, out var cls))
            {
                Add($"c{o}={cls}");
            }

            if (table.TryGetValue(key, out var senses))
            {
                var mask = 0;
                foreach (var sense in senses)
                {
                    mask |= 1 << (int)sense.PartOfSpeech;
                }

                Add($"p{o}={mask}");
            }

            // Endings and capitals of the nearest neighbors
            if (Math.Abs(o) == 1)
            {
                if (key.Length >= 4)
                {
                    Add($"s3{o}={key[^3..]}");
                }

                if (key.Length >= 3)
                {
                    Add($"s2{o}={key[^2..]}");
                }

                if (IsUpper(words[index]))
                {
                    Add($"cap{o}");
                }
            }
        }

        // A few word pairs around the target
        Add($"b-2={keys[Window - 2]}_{keys[Window - 1]}");
        Add($"b+1={keys[Window + 1]}_{keys[Window + 2]}");
        Add($"b0={keys[Window - 1]}_{keys[Window + 1]}");

        var sorted = ids.ToArray();
        Array.Sort(sorted);
        return (sorted, (float)(1.0 / Math.Sqrt(sorted.Length)));
    }

    /// <summary>
    ///     Lower-cases a word for the feature key; digits become a single number token.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The key.</returns>
    private static string Normalize(string word)
    {
        var key = Words.Normalize(word);
        return key.Length > 0 && key.All(char.IsDigit) ? "<num>" : key;
    }

    /// <summary>
    ///     Checks whether a word starts with a capital letter.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>True when the first character is an upper-case letter.</returns>
    private static bool IsUpper(string word) => word.Length > 0 && char.IsUpper(word[0]);

    /// <summary>
    ///     Hashes a feature name into the hash space with FNV-1a.
    /// </summary>
    /// <param name="feature">The feature name.</param>
    /// <returns>The id in <c>[0, HashSpace)</c>.</returns>
    internal static int Hash(string feature)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(feature))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (int)(hash & (HashSpace - 1));
    }

    /// <summary>
    ///     Builds the function-word classes.
    /// </summary>
    /// <returns>A map from a word to its class.</returns>
    private static Dictionary<string, string> BuildFunctionClasses()
    {
        var groups = new (string Class, string Words)[]
        {
            ("det", "a an the this that these those my your his her its our their some any no every each either neither another such what which whose"),
            ("prep", "of in on at by for with from to into onto over under about above below between among through during before after against along around behind beside beyond despite down inside near off out outside past since than toward towards until up upon within without"),
            ("pron", "i me you he him she it we us they them who whom myself yourself himself herself itself ourselves themselves mine yours hers ours theirs one"),
            ("aux", "am is are was were be been being have has had having do does did doing"),
            ("modal", "can could may might must shall should will would ought"),
            ("conj", "and or but nor so yet because although though while whereas if unless whether as"),
            ("adv", "not very too quite just also only even still already always never often rather there here then now"),
            ("neg", "n't"),
        };

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cls, list) in groups)
        {
            foreach (var word in list.Split(' '))
            {
                map.TryAdd(word, cls);
            }
        }

        return map;
    }
}
