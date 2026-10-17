namespace DemaConsulting.WordSense;

/// <summary>
///     The core of DemaConsulting.WordSense: the English word lexicon and the model contract (the interface that every
///     word-sense model package implements).
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c> and has no dependencies. It holds <see cref="WordLexicon"/> (every known word with its lemma, part
///     of speech, meaning and respelling, loaded from an embedded table on first use), the types
///     <see cref="WordDetails"/> and <see cref="PartOfSpeech"/>, the model interface <see cref="IWordSenseModel"/>
///     and the types <see cref="MirrorOptions"/> and <see cref="WordSense"/>. It cannot
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
///     Which model to choose. The figures are from the build notes
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md, which the
///     README repeats exactly); they are rounded here, and latency, first-use time and memory are measured on one
///     machine, so treat them as approximate. "Real text" means sentences from openly licensed or public-domain
///     sources and is the realistic expectation; the holdout was written together with the training sentences and
///     is optimistic. All sizes are in MiB (1 MiB = 1,048,576 bytes). The memory figure is what the model adds to the
///     process <em>on top of</em> the word lexicon: the lexicon of this package (about 43 MiB, once per process, shared
///     by every model) is not included, so add it to the figure of the model you use.
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Model</term>
///     <description>Best sense, holdout / real text; download; latency per sentence; memory added</description>
///     </listheader>
///     <item>
///     <term>Basic</term>
///     <description>about 89% / 85%; none (83 KiB table inside the package); under 0.1 ms; about 21 MiB; no network, no ONNX Runtime</description>
///     </item>
///     <item>
///     <term>MiniLM</term>
///     <description>about 97% / 93%; 22 MiB, once; about 2 ms; about 64 MiB</description>
///     </item>
///     <item>
///     <term>BertBaseCased</term>
///     <description>about 99% / 97%; 104 MiB, once; about 9 ms; about 150 MiB; keeps the case of the text</description>
///     </item>
///     </list>
///     <para>
///     Threshold versus coverage and precision (approximate, rounded; measured on the real-text sentences of the build
///     notes, best-sense accuracy of the words that reach the threshold; "coverage" is the share of the rows that
///     reach it, and the thresholds are specific to the model, so never reuse one across models):
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Model and threshold</term>
///     <description>Coverage / precision on real text (holdout in parentheses)</description>
///     </listheader>
///     <item>
///     <term>Basic, at least 0.6</term>
///     <description>about 90% / 89% (holdout: 95% / 90%)</description>
///     </item>
///     <item>
///     <term>Basic, at least 0.8</term>
///     <description>about 78% / 93% (holdout: 86% / 94%)</description>
///     </item>
///     <item>
///     <term>Basic, at least 0.9</term>
///     <description>about 70% / 96% (holdout: 78% / 96%)</description>
///     </item>
///     <item>
///     <term>MiniLM, at least 0.6</term>
///     <description>about 82% / 97% (holdout: 88% / 99%)</description>
///     </item>
///     <item>
///     <term>MiniLM, at least 0.8</term>
///     <description>about 73% / 98% (holdout: 79% / 99%)</description>
///     </item>
///     <item>
///     <term>MiniLM, at least 0.9</term>
///     <description>about 69% / 98% (holdout: 75% / 99%)</description>
///     </item>
///     <item>
///     <term>BertBaseCased, at least 0.6</term>
///     <description>about 85% / 99% (holdout: 94% / 99.7%)</description>
///     </item>
///     <item>
///     <term>BertBaseCased, at least 0.8</term>
///     <description>about 70% / 99% (holdout: 87% / 99.7%)</description>
///     </item>
///     <item>
///     <term>BertBaseCased, at least 0.9</term>
///     <description>about 64% / 99% (holdout: 81% / 99.7%)</description>
///     </item>
///     </list>
///     <para>
///     Without a threshold (every target word taken) the accuracy is the best-sense figure of the table above. A
///     threshold is specific to a model, so choose one per model from the table. Recommended starting value: 0.8 for
///     each of the three models, read in that model's own scale (never copy a value from one model to another without
///     checking its row); it keeps about three quarters of the real-text target words (Basic 78%, MiniLM 73%,
///     BertBaseCased 70%) with about one error in fourteen (Basic), one in fifty (MiniLM) or one in a hundred
///     (BertBaseCased). For Basic the
///     confidence of a word with n senses is never below 1/n (0.5 for two senses), so a threshold below that keeps
///     every Basic answer for such words. For an encoder, 0.5 is the lowest meaningful value: the confidence is the
///     margin relative to the abstain margin trained for the lemma, so 0.5 means the margin equals the abstain margin,
///     and below 0.5 the model's own abstain rule would not have answered; use 0.6 for more coverage (MiniLM 82%,
///     BertBaseCased 85% of the target words) or 0.9 for more precision. The full tables are
///     in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-comparison.md and
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md.
///     </para>
///     <para>
///     Default recommendation: start with <c>DemaConsulting.WordSense.MiniLM</c>, which is small, fast and accurate
///     enough for most uses. Choose <c>DemaConsulting.WordSense.BertBaseCased</c> when accuracy matters most and the
///     larger download and memory are acceptable. Basic is enough when nothing may be downloaded, startup and memory
///     must stay small, you cannot ship ONNX Runtime, or you can live with roughly one wrong answer in six or seven
///     ambiguous words on real text (about 85% best-sense accuracy), possibly filtered by a confidence threshold.
///     </para>
///     <para>
///     First-use costs (approximate, machine dependent): the lexicon loads on first use of <see cref="WordLexicon"/>
///     (about 120 ms, about 43 MiB); Basic then loads its table in about 22 ms; an encoder model opens its ONNX
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
///     first; the models lower-case the word for you, but the dictionary <see cref="WordLexicon.Words"/> itself is
///     case-sensitive, so look up lower-case keys there; null means <see cref="WordSense.Details"/> is null and the confidence is 0):
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Token</term>
///     <description>Result</description>
///     </listheader>
///     <item>
///     <term><c>record</c>, <c>Record</c>, <c>RECORD</c>, <c>records</c></term>
///     <description>Found (the lookup ignores case; <c>records</c> is a stored inflection): several senses, so the best sense from context with the model's confidence</description>
///     </item>
///     <item>
///     <term><c>the</c>, <c>a</c>, a misspelling, a name that is not in the lexicon</term>
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
///     <term><c>don't</c> (also with U+2019), <c>it's</c>, <c>dog's</c>, <c>didn't</c></term>
///     <description>Unknown: null, 0 (the lexicon has no contraction or possessive keys); the neighboring words are classified normally. If you need the base word of a possessive classified, split it before the <c>'s</c> yourself (<c>record</c>, <c>'s</c>); the evaluation did not do that</description>
///     </item>
///     <item>
///     <term><c>do</c> + <c>n't</c> (split by another tokenizer)</term>
///     <description><c>do</c> is found, <c>n't</c> is unknown; but this is not the form the models were evaluated on, so avoid it (Basic guesses the tag of <c>n't</c> from its suffix)</description>
///     </item>
///     <item>
///     <term><c>well-known</c>, <c>re-read</c>, <c>mother-in-law</c></term>
///     <description>Unknown: null, 0 (hyphenated words are not lexicon keys); keep them as one token</description>
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
///     Naming: <c>WordSense</c> is both this namespace and the result type <see cref="WordSense"/> (a record struct of
///     the word, its details and a confidence). Models return <c>WordSense[]</c>.
///     </para>
/// </remarks>
/// <example>
///     Look a word up without any model, then classify a sentence with a model package.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     foreach (WordDetails sense in WordLexicon.Words["record"])
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///     }
///
///     using IWordSenseModel model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     WordSense[] senses = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
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
