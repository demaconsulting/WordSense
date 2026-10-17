namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The basic model for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from the
///     words around it, with no neural network, no model files and no ONNX Runtime.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.Basic</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends only on <c>DemaConsulting.WordSense</c>. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.Basic</c>. It uses a statistical part-of-speech tagger and a context cue
///     model whose small tables are embedded; nothing is downloaded. It is small and fast and less accurate than the encoder models; the comparison is
///     in the build notes (https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes).
///     </para>
///     <para>
///     Start with <see cref="BasicWordSenseModel"/>: create it and call <c>Create</c> to get an
///     <see cref="IWordSenseClassifier"/>. <c>EnsureModelAsync</c> is optional for this model (it completes at once and
///     checks nothing, because there are no files), and <c>Create</c> works with or without it; call it only to keep
///     the code identical to the encoder models. Dispose the model with <c>using</c> like every <see cref="IWordSenseModel"/>
///     (it holds nothing to release, but afterwards <c>EnsureModelAsync</c>, <c>Create</c> and the classifiers it created
///     throw <see cref="ObjectDisposedException"/>). With a token that is already
///     cancelled, <c>EnsureModelAsync</c> returns a cancelled task (awaiting it throws
///     <see cref="OperationCanceledException"/>); otherwise the token is ignored.
///     </para>
///     <para>
///     First use (approximate, machine dependent): the word lexicon of the core package loads on first use (about 120 ms,
///     about 43 MiB), at the latest in <c>Create</c>, and the statistical table (83 KiB, embedded) loads in about 22 ms
///     on the first <c>ClassifyAsync</c> of a non-empty list. After that a sentence takes about 0.03 ms (about 30
///     microseconds per sentence, about 2 microseconds per word, measured) and the model adds about 21 MiB of memory,
///     not counting the 43 MiB of the lexicon. The classifier is safe for concurrent use.
///     </para>
///     <para>
///     Accuracy (build notes, https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md and
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md): about 89% best-sense
///     accuracy on the holdout and about 85% on real text, against about 93% and 97% on real text for MiniLM and
///     BertBaseCased. Basic is enough when nothing may be downloaded, startup time and memory must stay small, or
///     ONNX Runtime cannot be shipped; use an encoder model when real-text accuracy matters more.
///     </para>
///     <para>
///     Tokens: split punctuation into elements of their own, one sentence per call is recommended. The model splits a
///     list into sentence segments at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>, tags each segment and uses the
///     five words on each side of the target as context; there is no length limit. Keep a contraction such as
///     <c>don't</c> or a hyphenated word as one token; it has no lexicon entry and is returned as unknown (null,
///     confidence 0), and a tokenizer that splits it into <c>do</c> and <c>n't</c> changes what the tagger sees.
///     </para>
///     <para>
///     Confidence: 0 means the word is unknown to the lexicon (<c>Details</c> is null), 1 means the word has a single
///     sense in the lexicon, and a word with several senses gets the probability of the chosen sense, between one over
///     the number of senses and 1; <c>Details</c> is never null for a word that is in the lexicon, so an ambiguous
///     word is never left undecided. This probability scale is specific to the Basic model: do not reuse a threshold
///     from, or for, MiniLM or BertBaseCased, whose confidence is a margin score. The value is a relative score, not a
///     calibrated probability, but higher values are more often right: on real text a threshold of 0.6 keeps about
///     90% of the target words at about 89% precision, 0.8 about 78% at about 93%, and 0.9 about 70% at about 96%
///     (holdout: 95% at 90%, 86% at 94%, 78% at 96%; the table is in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md).
///     </para>
///     <para>
///     Case: the lookup and the cue words are lower-cased, so <c>Record</c> and <c>record</c> are the same word. The
///     only use of case is that a word that is not in the lexicon, starts with an upper-case letter and is not the
///     first word of its sentence segment is tagged a noun (a likely proper noun). A period inside the list ends the
///     segment, so write an abbreviation as one token (<c>Mr.</c>) and a decimal as one token (<c>3.5</c>); see the
///     table of edge cases in the documentation of the <c>DemaConsulting.WordSense</c> namespace.
///     </para>
///     <para>
///     Which model to choose: Basic is the smallest and fastest and needs nothing else, but it is less accurate than
///     the encoder models. If accuracy matters more, use
///     <c>DemaConsulting.WordSense.MiniLM</c> (the default recommendation: 22 MiB, about 2 ms per sentence) or
///     <c>DemaConsulting.WordSense.BertBaseCased</c> (104 MiB, case-aware and the most accurate); see the namespace
///     documentation of <c>DemaConsulting.WordSense</c> for the comparison table.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     using var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;
