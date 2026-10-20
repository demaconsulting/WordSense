namespace DemaConsulting.WordSense;

/// <summary>
///     The core of DemaConsulting.WordSense: the English word lexicon and the model contract (the interface that every
///     word-sense model package implements).
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c> and has no dependencies. It holds <see cref="WordLexicon"/> (every known written form with its
///     analyses, lemma, part of speech and grammatical features, and the catalog of the senses and groups of every lemma,
///     opened in place from an embedded image on first use), the types
///     <see cref="WordDetails"/>, <see cref="PartOfSpeech"/>, <see cref="SenseInfo"/> and <see cref="SenseGroup"/>, the
///     model interface <see cref="IWordSenseModel"/>
///     and the types <see cref="MirrorOptions"/>, <see cref="ClassifyResult"/>, <see cref="SenseLevel"/> and
///     <see cref="SenseCandidate"/>. It cannot
///     disambiguate a word in context by itself: add one of the model packages for that.
///     </para>
///     <para>
///     Model packages: <c>DemaConsulting.WordSense.Basic</c> (statistical tagger, no model files, no ONNX; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Basic),
///     <c>DemaConsulting.WordSense.MiniLM</c> (a small encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.MiniLM) and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> (a larger, case-aware encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.BertBaseCased). The two
///     encoder packages share the ONNX plumbing of <c>DemaConsulting.WordSense.Onnx</c>. The comparison of the models is
///     in the build notes: https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes.
///     </para>
///     <para>
///     Which model to choose. The comparison of the models is generated into the build notes
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md, which the README
///     repeats exactly). The encoder packages score the fine senses of the lemmas of the relabeled corpus (see the
///     senses appendix of the user guide) and answer every other ambiguous word through the Basic model (they compose it
///     with <see cref="FallbackWordSenseModel"/>); the table shows the figures measured on the held-out sentences of
///     that batch. Choose
///     <c>DemaConsulting.WordSense.Basic</c> unless you need the encoders: it has no model files, no ONNX Runtime and
///     the smallest startup and memory cost. "Real text" means sentences from openly licensed or public-domain sources
///     and is the realistic expectation; the holdout was written together with the training sentences and is
///     optimistic.
///     </para>
///     <para>
///     First-use costs (approximate, machine dependent): the lexicon opens on first use of <see cref="WordLexicon"/>
///     (about 55 ms, about 15 MiB); Basic then loads its table in about 22 ms; an encoder model opens its ONNX
///     session on the first classification that needs it (about 0.4 s for MiniLM, about 0.5 s for BertBaseCased), after
///     the files are ensured: <c>ClassifyAsync</c> does that itself when a word needs the model (with the default
///     mirror settings), or call <c>EnsureModelAsync</c> first. A word list without an ambiguous word never touches
///     model files. The Basic model has no files, so it needs no
///     preceding <c>EnsureModelAsync</c>; for the encoder models it is optional too (call it to fail early or to pass a
///     <see cref="MirrorOptions"/>). Hashing the model files takes about 30 ms (MiniLM) and about 110 ms (BertBaseCased)
///     with a warm file cache. The encoder models run on the CPU only.
///     </para>
///     <para>
///     Tokenizing text. You split the text into words before classifying; the models expect the tokens that the
///     evaluation used: a word is a run of letters, digits and combining marks that may contain inner apostrophes
///     (<c>'</c> or the curly U+2019) and hyphens, so a contraction (<c>don't</c>), a possessive (<c>dog's</c>) and a
///     hyphenated word (<c>well-known</c>) are one token; every other non-space character (punctuation, quotes, dashes,
///     symbols) is a token of its own; a decimal or a number with thousands separators (<c>3.5</c>, <c>1,000</c>) is
///     best kept as one token. One sentence per call. The reference tokenizer in the example below does exactly
///     that (it is verified by a unit test of this repository), with one limitation: it knows nothing about
///     abbreviations, so <c>Mr.</c> and <c>U.S.</c> are split at every period (see the table). The tokens are not trimmed or lower-cased by you; the
///     models do that for the lookup.
///     </para>
///     <para>
///     What the lookup does with typical tokens (the same for all three models, because the lexicon lookup comes
///     first; the models lower-case the word for you, and <see cref="WordLexicon.Lookup"/> finds a word by its exact
///     spelling first and then without regard to case; null means <see cref="ClassifyResult.Details"/> is null, the level is <see cref="SenseLevel.None"/> and the confidence is 0):
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Token</term>
///     <description>Result</description>
///     </listheader>
///     <item>
///     <term><c>record</c>, <c>Record</c>, <c>RECORD</c>, <c>records</c></term>
///     <description>Found (the lookup ignores case; <c>records</c> is a stored inflection): several analyses, so the part of speech from context with the model's confidence, and the level <see cref="SenseLevel.PartOfSpeech"/> while the senses are not scored</description>
///     </item>
///     <item>
///     <term><c>the</c>, <c>a</c>, <c>his</c></term>
///     <description>Found: a determiner with one sense, so the level <see cref="SenseLevel.Sense"/> with confidence 1 (the lexicon holds every word of its source dictionary)</description>
///     </item>
///     <item>
///     <term>A misspelling, a name that is not in the lexicon</term>
///     <description>Unknown: null, 0 (a name that is in the lexicon, such as <c>Smith</c>, is found)</description>
///     </item>
///     <item>
///     <term>A comma <c>,</c>, a quote <c>"</c>, a hyphen <c>-</c>, an em dash, a semicolon <c>;</c>, a parenthesis <c>)</c></term>
///     <description>Unknown: null, 0; punctuation is always its own token. For Basic, only the tokens <c>.</c>, <c>!</c> and <c>?</c> also end a sentence segment</description>
///     </item>
///     <item>
///     <term><c>race.</c> (period attached)</term>
///     <description>Unknown: null, 0, because <c>race.</c> is not a lexicon key; Basic then sees no sentence end. Always split the period off</description>
///     </item>
///     <item>
///     <term><c>don't</c> (also with U+2019), <c>it's</c>, <c>didn't</c></term>
///     <description>Found: a stored contraction (a closed list) with the analysis of its head word and <c>Contraction=Yes</c></description>
///     </item>
///     <item>
///     <term><c>dog's</c>, <c>dogs'</c></term>
///     <description>Found when the stem is known: the analysis of the stem with <c>Poss=Yes</c>. The <c>'s</c> stays inside the token by the tokenizer contract; if you split it off, look the stem up on its own</description>
///     </item>
///     <item>
///     <term><c>do</c> + <c>n't</c> (split by another tokenizer)</term>
///     <description><c>do</c> is found, <c>n't</c> is unknown; but this is not the form the models were evaluated on, so avoid it (Basic guesses the tag of <c>n't</c> from its suffix)</description>
///     </item>
///     <item>
///     <term><c>well-known</c>, <c>re-read</c>, <c>mother-in-law</c></term>
///     <description>Found only when the whole hyphenated word is in the lexicon (for example <c>well-known</c>); otherwise unknown: null, 0. Keep them as one token</description>
///     </item>
///     <item>
///     <term><c>3</c>, <c>3.5</c>, <c>1,000</c></term>
///     <description>Unknown: null, 0; a token without a letter is a number or punctuation. Keep <c>3.5</c> as one token: for Basic, split into <c>3</c>, <c>.</c>, <c>5</c> the period ends a sentence segment</description>
///     </item>
///     <item>
///     <term><c>Mr</c> + <c>.</c>, <c>U</c> + <c>.</c> + <c>S</c> + <c>.</c></term>
///     <description>Each period is a sentence end for Basic and cuts its context window; the reference tokenizer below does not handle abbreviations and splits <c>Mr.</c> into <c>Mr</c> and <c>.</c>, so merge such abbreviations into one token yourself before classifying (<c>Mr.</c>, unknown, null, 0) or drop their periods. Encoders are not affected</description>
///     </item>
///     <item>
///     <term>An empty string; a null element; an empty list</term>
///     <description>An empty string is unknown (null, 0); an element that contains a space (<c>"New York"</c>) is not split: it is looked up as one key, which the lexicon never has, so it is unknown, so pass one token per element; a null element throws <see cref="ArgumentException"/>; an empty list gives an empty array</description>
///     </item>
///     </list>
///     <para>
///     Case. The lexicon lookup lower-cases every word for all models, so <c>Record</c> and <c>RECORD</c> find the same
///     entry. After the lookup the models differ. MiniLM is uncased: its tokenizer lower-cases the text, so case never
///     changes its answer. BertBaseCased is cased: it sees <c>READ</c>, <c>Read</c> and <c>read</c> as different word
///     pieces, so keep the case of the original text and do not lower-case it yourself. Basic uses case in one place only: a word that is not in the lexicon and starts
///     with an upper-case letter inside a sentence segment (not first) is tagged a noun (a likely proper noun), which
///     helps the tagging of its neighbors; the lemma lookup and the cue words of Basic are lower-cased.
///     </para>
///     <para>
///     Naming: the result of a classification is the record struct <see cref="ClassifyResult"/> (the word, the ranked
///     candidate analyses with their probabilities, the selected analysis, the answer level, the group and sense ids
///     and the confidence); models return <c>ClassifyResult[]</c>. The null test for an unknown word is
///     <c>Details is null</c> (or <see cref="ClassifyResult.IsKnown"/> false, or the level
///     <see cref="SenseLevel.None"/>); a known word is answered at least at the part-of-speech level. A group answer
///     names its member senses through <see cref="WordLexicon.TryGetGroup"/>.
///     </para>
/// </remarks>
/// <example>
///     Look a word up without any model, then classify a sentence with a model package.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     foreach (WordDetails analysis in WordLexicon.Lookup("record"))
///     {
///         Console.WriteLine($"{analysis.Lemma}: {analysis.PartOfSpeech} {analysis.Features}");
///     }
///
///     using IWordSenseModel model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     ClassifyResult[] results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
///     Tokenizing text: a reference tokenizer (a word, a number with separators, or one other character per token).
///     <code>
///     using System.Linq;
///     using System.Text.RegularExpressions;
///
///     var tokenPattern = new Regex(
///         @"\d+(?:[.,]\d+)+|[\p{L}\p{Nd}\p{Mn}\p{Mc}]+(?:['\u2019-][\p{L}\p{Nd}\p{Mn}\p{Mc}]+)*|[\uD800-\uDBFF][\uDC00-\uDFFF]|\S",
///         RegexOptions.CultureInvariant);
///
///     string[] Tokenize(string sentence) => tokenPattern.Matches(sentence).Select(match => match.Value).ToArray();
///
///     // Smith's | well-known | dog | didn't | " | bark | " | at | 3.5 | cats | .
///     string[] tokens = Tokenize("Smith's well-known dog didn't \"bark\" at 3.5 cats.");
///     </code>
/// </example>
internal static class NamespaceDoc;
