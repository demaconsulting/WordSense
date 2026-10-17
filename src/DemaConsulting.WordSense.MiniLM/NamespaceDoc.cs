namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The MiniLM encoder for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from its
///     sentence with an int8 ONNX all-MiniLM-L6-v2 model.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.MiniLM</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it is built on <c>DemaConsulting.WordSense.Onnx</c> (the shared tokenizer, ONNX encoder, verified
///     downloader and abstract <c>OnnxWordSenseModel</c>, which <c>MiniLMWordSenseModel</c> extends) and depends on
///     <c>DemaConsulting.WordSense</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.MiniLM</c>; no native or other setup is needed. The weights
///     trained for the model are embedded in the assembly. The roughly 23 MB encoder (<c>model_int8.onnx</c>, plus a small
///     <c>vocab.txt</c>) is downloaded once on first use, verified against pinned SHA-256 digests and cached per user, or
///     it is read from a directory you supply, in which case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="MiniLMWordSenseModel"/>: create it, call <c>EnsureModelAsync</c>, then <c>Create</c> to get an
///     <see cref="IWordSenseClassifier"/>. The files are described under <c>model/all-MiniLM-L6-v2-int8/</c> in the
///     repository (https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8).
///     </para>
///     <para>
///     Choose MiniLM for a small, fast model, the default recommendation: 23 MiB download, about 2 ms per sentence,
///     about 97% best-sense accuracy on the holdout and about 93% on real text (<c>docs/build_notes/model-table.md</c>).
///     Choose <c>DemaConsulting.WordSense.BertBaseCased</c> when accuracy and case-awareness matter more than size:
///     104 MiB, about 9 ms per sentence, about 99% and 97%. The statistical Basic model
///     (<c>DemaConsulting.WordSense.Basic</c>, no download, about 85% on real text) is enough when nothing may be
///     downloaded. Latency, first-use time and memory are approximate and machine dependent.
///     </para>
///     <para>
///     First use (approximate, machine dependent): <c>EnsureModelAsync</c> hashes the two files; the first <c>Create</c>
///     opens the ONNX session and loads the weights in about 0.4 s, and the word lexicon of the core package loads in
///     about 120 ms (about 43 MiB). Dispose the model once, after every classification has finished; afterwards
///     <c>EnsureModelAsync</c> and <c>Create</c> throw <see cref="ObjectDisposedException"/>. The model and the
///     classifiers it creates are safe for concurrent use.
///     </para>
///     <para>
///     Confidence: the confidence of a decided word is a monotonic mapping of the margin between the best and the
///     runner-up sense onto 0 to 1, relative to the abstain margin trained for the word (0.5 at that margin, clamped); it
///     is a ranking score, not a calibrated probability, so choose your own threshold, and never reuse a threshold from
///     another model (the Basic model reports a probability and BertBaseCased has its own margin scale). 1 means a
///     single-sense word and 0 an unknown word (<c>Details</c> is null); <c>Details</c> is also null for a word with
///     several senses whose lemma has no intact weights, which does not happen for the weights embedded in this
///     package. A low confidence still carries the best sense.
///     </para>
///     <para>
///     Mirrors: a <see cref="MirrorOptions"/> passed to <c>EnsureModelAsync</c> takes precedence over the
///     <c>WORDSENSE_MIRROR_*</c> variables; the mirror is tried before the public source for each file that is missing
///     or invalid and is not contacted when every file is already present. The mirror holds the files at
///     <c>{mirror}/{model-id}/{file}</c>, where the model id is the <c>ModelId</c> of the model, for example
///     <c>https://models.example.com/wordsense/all-MiniLM-L6-v2-int8/model_int8.onnx</c>. Downloaded files are kept in
///     <c>{StoreRoot}/all-MiniLM-L6-v2-int8/</c>; only <see cref="MirrorOptions.StoreRoot"/> relocates the store (no
///     environment variable does, and a <see cref="MirrorOptions"/> needs a mirror base URI).
///     </para>
///     <para>
///     Offline and air-gapped use: fetch <c>model_int8.onnx</c> (SHA-256
///     <c>afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1</c>) and <c>vocab.txt</c> (SHA-256
///     <c>07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3</c>) from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/all-MiniLM-L6-v2-int8/{file}</c>,
///     put both directly in one directory (no <c>{model-id}</c> subfolder) and pass it to the constructor. Both files are
///     verified against these digests by <c>EnsureModelAsync</c>, which you must still call (no network access
///     happens); a missing file throws <see cref="FileNotFoundException"/> and a wrong digest
///     <see cref="InvalidDataException"/>. For a corporate mirror pass a
///     <see cref="MirrorOptions"/> to <c>EnsureModelAsync</c> or set <c>WORDSENSE_MIRROR_URL</c> (with the optional
///     <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> or <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>); files are
///     requested at <c>{mirror}/all-MiniLM-L6-v2-int8/{file}</c>. A file that fails verification is never used.
///     </para>
///     <para>
///     Input: one sentence per call; split punctuation into tokens of their own; keep a contraction such as
///     <c>don't</c> or a hyphenated word as one token. The default maximum sequence length is 128 word pieces
///     including the special tokens (8 to 512 can be set in the constructor); a longer text is encoded per word from a
///     window around it, so nothing fails but distant context is lost.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     using var model = new MiniLMWordSenseModel();
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
///     Offline: the two files are in one flat directory, for example <c>/opt/models/minilm</c>.
///     <code>
///     using var offline = new MiniLMWordSenseModel("/opt/models/minilm");
///     await offline.EnsureModelAsync();   // verifies SHA-256, no network
///     IWordSenseClassifier offlineClassifier = offline.Create();
///     </code>
/// </example>
internal static class NamespaceDoc;
