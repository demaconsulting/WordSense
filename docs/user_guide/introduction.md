# Introduction

## Purpose

This document is the user guide for WordSense, a .NET library that tells, for English words in context and offline,
the part of speech and the pronunciation. It tells, for example, whether "record" is a noun or a verb, and
whether "bow" is said like "bough" (a bend) or like "beau" (a weapon). Every word is looked up in an embedded
word table; a word with several meanings is decided from its sentence by a model that you choose. The library comes as
five NuGet packages: the core `DemaConsulting.WordSense` (API and word table, no model), the statistical model
`DemaConsulting.WordSense.Basic` (no files, no network), and two encoder models, `DemaConsulting.WordSense.MiniLM`
and `DemaConsulting.WordSense.BertBaseCased`, which share the machinery of `DemaConsulting.WordSense.Onnx`.

## Scope

This user guide covers:

- Choosing and installing the packages, requirements and a quick start
- The classification API: words, results, confidence and pronunciation
- The word table of the core package
- Model download, mirrors and offline use
- Limitations
- Licensing and provenance of the data
- Extending the training corpus with the builder
- Troubleshooting
- A glossary of the terms used
<!-- markdownlint-disable MD051 -->
<!-- The appendix is a separate file that pandoc concatenates, so its heading anchor only exists in the PDF. -->
- [Appendix: Supported Senses](#appendix-supported-senses): the catalog of all lemmas with trained weights, their
  parts of speech, pronunciation meanings and forms (generated from the corpus)
<!-- markdownlint-enable MD051 -->

The code samples in this guide use only the public API of the packages. The README sample is compiled and run by the
test suite; the samples in this guide are reviewed with the guide. They assume `ImplicitUsings`,
`using DemaConsulting.WordSense;` and `using DemaConsulting.WordSense.MiniLM;`.

# Glossary

- **Lemma**: the dictionary base form of a word, for example "record" for "records".
- **Word table**: the embedded table of the core package, `WordLexicon.Words`. It maps every known lower-case surface
  form, including the inflected forms that the build materialized, to its possible senses. A word in it is *known*.
- **Sense**: one possible reading of a word, a `WordDetails`: lemma, part of speech, meaning and respelling.
- **Heteronym**: a word that is spelled the same but pronounced differently depending on its use, for example
  "record" (the noun is said REK-urd, the verb rih-KORD) or "bow".
- **Part of speech**: the grammatical role of a word in its sentence. `PartOfSpeech` has eight values: noun, pronoun,
  verb, adjective, adverb, preposition, conjunction and interjection.
- **Meaning**: a short id, for example `bend` or `weapon` for "bow", that names which pronunciation of a heteronym is
  meant; it is `null` when the lemma is not a heteronym.
- **Respelling**: a plain-letter hint of how to say the word, for example `boh`; it is a guide for a person or a
  speech engine and not a phonetic transcription.
- **Confidence**: a number from 0 to 1 in each result. It is 1 for a word with one sense and 0 for an unknown word
  or an abstention. For a word decided by an encoder model it grows with the model's score margin; for the Basic
  model it is the relative probability of the chosen sense. It is not a calibrated probability.
- **Abstain**: an encoder model gives no sense for a known word that has several senses (for example when it has no
  weights for the lemma): `Details` is `null` and `Confidence` is 0. The Basic model never abstains.
- **Mirror**: an HTTP(S) location, usually inside your network, that serves the model files.
- **Model directory**: a folder that holds the exact model files, used instead of a download.

# Choosing a Package

All packages target `net8.0`, `net9.0` and `net10.0`. The core package is a dependency of every other package.

| Package | Decides a multi-sense word by | Needs files or network | Native dependency | Casing |
| ------- | ----------------------------- | ---------------------- | ----------------- | ------ |
| `DemaConsulting.WordSense` | nothing (word table only) | no | no | lower-cased |
| `DemaConsulting.WordSense.Basic` | small statistical tagger and context cues | no | no | lower-cased |
| `DemaConsulting.WordSense.MiniLM` | all-MiniLM-L6-v2 encoder, weights | 22 MiB, once | ONNX Runtime | uncased |
| `DemaConsulting.WordSense.BertBaseCased` | bert-base-cased encoder | 104 MiB, once | ONNX Runtime | cased |

- Use **core** alone to look up the possible senses of a word (`WordLexicon.Words`) and to answer words that have one
  sense, in your own code.
- Use **Basic** when you want a fast, dependency-free answer and can accept less accuracy than the encoder models.
- Use **MiniLM** for the best size-to-quality trade-off: the smallest download and the fastest encoder. It is the
  default recommendation.
- Use **BertBaseCased** when the larger, cased encoder gives better results on your texts. The accuracy of each model
  is measured on the same held-out and real-text sentences; the figures are in the build notes
  (`model-comparison.md` and `basic-evaluation.md`).

The measured comparison (rounded; the exact table is `model-table.md` of the build notes and the README, and latency,
first-use time and memory are approximate and machine dependent). "Real text" is the realistic expectation; the holdout
was written together with the training sentences and is optimistic.

| Model | Best sense, holdout / real text | Download | Latency per sentence | Memory added |
| ----- | ------------------------------: | -------: | -------------------: | -----------: |
| Basic | about 89% / 85% | none (83 KiB embedded) | 0.03 ms (about 30 µs), under 0.1 ms | about 21 MiB |
| MiniLM | about 97% / 93% | 22 MiB | about 2 ms | about 64 MiB |
| BertBaseCased | about 99% / 97% | 104 MiB | about 9 ms | about 150 MiB |

The memory column is measured as the growth of the private memory of the process (after a full garbage collection)
from before the model was created to after it classified warm-up sentences, so it is not a count of the model's own
data: for Basic (an 83 KiB embedded table) the about 21 MiB are the decoded tables and working buffers plus allocator,
JIT-compiled code and runtime effects. The word lexicon (about 43 MiB) was loaded before and is not included.

**Basic is enough** when nothing may be downloaded, startup time and memory must stay small, or ONNX Runtime cannot be
shipped, and you can accept about one wrong answer in six or seven ambiguous words on real text (a confidence threshold
removes some of them, see Confidence). Otherwise start with MiniLM and move to BertBaseCased if its accuracy is not
enough.

`DemaConsulting.WordSense.Onnx` is the shared base of the two encoder packages (`OnnxWordSenseModel`, the downloader,
the tokenizer and the weights reader). It is installed with them and is not used directly.

# Installation

```bash
dotnet add package DemaConsulting.WordSense.MiniLM
```

Installing a model package brings the core with it. Install `DemaConsulting.WordSense.Basic` or
`DemaConsulting.WordSense.BertBaseCased` in the same way, and `DemaConsulting.WordSense` alone for the word table.

## Requirements

- .NET 8, 9 or 10 on Windows, Linux or macOS.
- For an encoder model, disk space in a per-user directory for the model files (see "Model Files") that are downloaded
  on first use, and the ONNX Runtime native libraries for the target platform. The NuGet package contains the trained
  weights but not the model files. A machine that cannot reach the internet needs a mirror or a model directory (see
  "Offline and Air-Gapped Use"). The core and Basic packages need neither.
- Contributors who clone the repository also need [Git LFS](https://git-lfs.com): the files in `model/` are stored
  in Git LFS (`git lfs install` once, then `git lfs pull`).

# Quick Start

```csharp
// Downloads the model files on first use, verifies their SHA-256 and reuses them later
using var model = new MiniLMWordSenseModel();
await model.EnsureModelAsync();

string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "the", "new", "album", "."];
WordSense[] senses = await model.ClassifyAsync(words);

foreach (var sense in senses)
{
    if (sense.Details is { } details)
    {
        Console.WriteLine($"{sense.Word}: {details.PartOfSpeech} {details.Meaning} ({sense.Confidence:F2})");
    }
}
```

The input is a list of words that you have tokenized yourself, in order and including punctuation tokens as elements
of their own; they are read as one running text, so classify independent sentences with one call each.
`ClassifyAsync` returns a non-null array that is parallel to the input: element `i` is the `WordSense` of
`words[i]`, whatever the word is.

## The Flow

Every word follows the same path:

1. The word is lower-cased (a curly apostrophe is read as `'`) and looked up in `WordLexicon.Words`. A word that is
   not there is **unknown**: `Details` is `null` and `Confidence` is 0.
2. A word with one sense is answered with that sense and `Confidence` 1, by every model, without running anything.
3. A word with several senses is decided from its context by the model: the encoder models score the senses of the
   word from the sentence and the Basic model tags the sentence and weighs the context words. Every model answers a
   word with several senses; the confidence says how sure it is.

An encoder model runs the encoder only when a sentence has a word that needs it, so a sentence without such a word
costs no model call. Whatever is in the word table is classified, whatever its capitalization or a trailing
possessive, and the table covers the inflected forms. (The Basic model's tagger does use the capitalization of a
word that is not in the table, as one of its features.)

## The Result

A `WordSense` is a `readonly record struct` with three members:

- `Word`: the word as you gave it.
- `Details`: a `WordDetails` or `null`. `WordDetails` has `Lemma`, `PartOfSpeech`, `Meaning` and `Respelling`.
- `Confidence`: from 0 to 1.

For a heteronym, `Meaning` and `Respelling` carry the pronunciation; they are `null` for every other word. For speech
synthesis, key your own pronunciation lexicon on the pair (`Lemma`, `Meaning`): the meaning is the stable value and the
respelling is a readable hint.

The
<!-- markdownlint-disable-next-line MD051 -->
[appendix](#appendix-supported-senses) is the catalog of the lemmas that have trained weights, with their parts of
speech and pronunciation meanings.

`Details` is `null` in exactly two cases, and `Confidence` is 0 in both. First, the word is unknown: it has no entry in
`WordLexicon.Words` (a determiner, a number, punctuation, most proper nouns, a misspelling, a contraction such as
`don't`); every model does this. Second, an encoder model cannot score a word that has several senses because its lemma
has no intact trained weights, or the encoder produced no vector for the word. The weights shipped in the MiniLM and
BertBaseCased packages cover every lemma (a test asserts that none is dropped), so in practice only unknown words are
`null`. Every other word gets a sense: a word with one sense gets it with `Confidence` 1, and a word with several
senses gets its best sense even when the confidence is low, so a `null` never means "low confidence". The Basic model
never leaves `Details` null for a word that is in the table.

```csharp
// "the" and "." are unknown (null, 0); "record" has several senses: best sense, model-specific confidence
WordSense[] senses = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
```

## Input and Tokenization

You tokenize the text; the models expect what the evaluation used:

- Punctuation is an element of its own (`"race"`, `"."`, never `"race."`).
- Keep a contraction (`don't`) and a hyphenated word (`re-read`) as one token. Neither is a key of the table, so such a
  token is unknown (null, 0), but the words around it are still classified. A curly apostrophe (U+2019) is read as `'`.
- Case does not matter for the lookup (words are lower-cased); the cased BertBaseCased encoder also sees the case you
  give it.
- Classify one sentence per call. The Basic model splits the list into sentences at the tokens `"."`, `"!"` and `"?"`
  and uses a window of five words on each side of the target; it has no length limit. An encoder model joins the words
  with single spaces and encodes them as one text of at most `maxSequenceLength` word pieces (128 by default, including
  the two special tokens; 8 to 512 can be set). A longer list is not rejected: each target is encoded from a window of
  that size around it, so only distant context is lost.

A reference tokenizer (a word with inner apostrophes and hyphens, a number with separators, or one other character per
token); the repository has a unit test that runs this exact pattern:

```csharp
using System.Text.RegularExpressions;

var tokenPattern = new Regex(
    @"\d+(?:[.,]\d+)+|[\p{L}\p{Nd}\p{Mn}\p{Mc}]+(?:['\u2019-][\p{L}\p{Nd}\p{Mn}\p{Mc}]+)*|[\uD800-\uDBFF][\uDC00-\uDFFF]|\S",
    RegexOptions.CultureInvariant);

string[] Tokenize(string sentence) => tokenPattern.Matches(sentence).Select(match => match.Value).ToArray();

// Smith's | well-known | dog | didn't | " | bark | " | at | 3.5 | cats | .
string[] tokens = Tokenize("Smith's well-known dog didn't \"bark\" at 3.5 cats.");
```

What each model returns for typical tokens (the lexicon lookup comes first, so the three models agree on whether a
token is known; "null" means `Details` is null and the confidence is 0):

<!-- markdownlint-disable MD013 -->

| Token | Result |
| --- | --- |
| `record`, `Record`, `RECORD`, `records` | Found (case is ignored by the lookup): best sense from context with a confidence |
| `the`, `a`, a misspelling, a name not in the lexicon | Unknown: null, 0 (a name that is in the lexicon, such as `Smith`, is found) |
| `,` `"` `-` an em dash `;` `)` | Unknown: null, 0; for Basic only `.`, `!` and `?` also end a sentence segment |
| `race.` (period attached) | Unknown: null, 0, and Basic sees no sentence end; always split the period off |
| `don't` (also with U+2019), `it's`, `dog's`, `didn't` | Unknown: null, 0 (no contraction or possessive keys); neighbors are classified normally |
| `do` + `n't` | `do` is found, `n't` is unknown; not the form the models were evaluated on, so avoid it |
| `well-known`, `re-read`, `mother-in-law` | Unknown: null, 0; keep them as one token |
| `3`, `3.5`, `1,000` | Unknown: null, 0; keep `3.5` as one token, because for Basic a split period ends a segment |
| `Mr` + `.`, `U` + `.` + `S` + `.` | The reference tokenizer does not handle abbreviations and splits them like this; each period ends a segment for Basic and cuts its context, so merge `Mr.` into one token yourself or drop the periods |
| empty string, null element, empty list | Empty string: null, 0; null element: `ArgumentException`; empty list: empty array |

<!-- markdownlint-enable MD013 -->

Case: the lookup lower-cases every word for all models. MiniLM is uncased (its tokenizer lower-cases, so case never
changes its answer); BertBaseCased is cased, so keep the case of the original text; Basic uses case only for a word that
is not in the lexicon and starts with an upper-case letter inside a segment (not first), which it tags as a noun.

# The Models

## Core: the Word Table

`WordLexicon.Words` is a `FrozenDictionary<string, WordDetails[]>` of lower-case surface forms to their senses. The
inflected forms (plural, verb forms, comparatives and so on) are in the table, so no morphology runs at use time. The
core package has no model, no ONNX Runtime and no weights: it knows nothing of the model packages.

```csharp
if (WordLexicon.Words.TryGetValue("lead", out WordDetails[]? senses))
{
    foreach (WordDetails sense in senses)
    {
        Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}, {sense.Meaning ?? "-"}, {sense.Respelling ?? "-"}");
    }
}
```

The keys are lower-case and compared ordinally, and the table has no key with a curly apostrophe: lower-case the word and
replace U+2019 with `'` yourself (the models do it for you). The indexer `Words["x"]` throws
`KeyNotFoundException` for a word that is not in the table, so use `TryGetValue`. The first access takes about 120 ms and
about 43 MiB (approximate, machine dependent), once per process.

## Basic

`BasicWordSenseModel` needs no files, no network and no ONNX Runtime. `EnsureModelAsync` is optional: it completes at
once and checks nothing, `ClassifyAsync` works with or without it, a mirror is ignored, and disposing the model (with `using`)
releases nothing but is still honored: afterwards `EnsureModelAsync` and `ClassifyAsync` throw
`ObjectDisposedException`. It
decides a multi-sense word with a small statistical table that is
embedded in the package: a part-of-speech tagger (a hidden Markov model trained on the Universal Dependencies English
web treebank, limited to the parts of speech that the word table allows) and, for lemmas that have enough training
sentences, context cues that separate senses of the same part of speech (for example `bass`). It always answers a
multi-sense word; the confidence is the probability that the model gives to its answer, between 1 divided by the number
of senses and 1. The measured quality is in the build notes (`basic-evaluation.md`). First use costs about 120 ms for
the word table and about 22 ms for the statistical table (approximate, machine dependent); after that a sentence takes
well under a millisecond.

```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

using var model = new BasicWordSenseModel();
await model.EnsureModelAsync();
WordSense[] senses = await model.ClassifyAsync(["to", "record", "the", "record"]);
Console.WriteLine(senses[1].Details?.PartOfSpeech.ToString() ?? "unknown");
```

## MiniLM

`MiniLMWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)` runs the int8 all-MiniLM-L6-v2
encoder (hidden size 384, uncased) with the weights that are embedded in its assembly. The weights were trained with
the sequence length 128; another value (8 to 512) is possible but unvalidated.

## BertBaseCased

`BertBaseCasedWordSenseModel(string? modelDirectory = null, int maxSequenceLength = 128)` runs the int8 bert-base-cased
encoder (hidden size 768, cased) with its own embedded weights. The file `model_int8.onnx` of this model is the encoder
derived from the upstream masked-language-model export, with the language-model head removed (see `model-provenance.md`
in the build notes).

## Working with an Encoder Model

Both encoder models derive from `OnnxWordSenseModel`, which implements `IWordSenseModel` and has the
public properties `HiddenSize` and `ModelId`. `IWordSenseModel` is `IDisposable`: the model owns the ONNX session and the
downloader state, so dispose it (with `using`) when you no longer need it; disposing while calls are running is safe
(see below). The
Basic model has nothing to
release but follows the same rules. The usual sequence is: call `EnsureModelAsync` once, classify as many texts as
you like with `ClassifyAsync` (safe for concurrent use) and dispose the model. `EnsureModelAsync` is optional:
`ClassifyAsync` ensures the model lazily on first need, with the default (or environment variable) mirror settings (and
may throw the same exceptions, such as `FileNotFoundException` or `InvalidDataException` for a supplied directory), so
call `EnsureModelAsync` first to fail early or to pass a mirror (a mirror passed while an ensure is already in flight is
ignored); a model directory is passed to the constructor. Text whose
words the word table alone resolves (no word with several senses) never needs the model: nothing is downloaded, opened
or verified, and a model that is never needed is never downloaded. Concurrent callers share one ensure, and a failed
ensure can be retried by the next call. The ensured state belongs to one model instance: after a successful call the
instance keeps the verified directory (including the `StoreRoot` of the `MirrorOptions` passed to that call), and a new
instance starts again; after a failed call, a lazy ensure uses no `MirrorOptions`. The ONNX session and the weights are
loaded at the first classification that needs
them: about 0.4 s for MiniLM and about 0.5 s for BertBaseCased, on top of the word table (about 120 ms)
and of the hashing of the model files in `EnsureModelAsync` (once per model instance; about 30 ms for MiniLM and about
110 ms for BertBaseCased
with a warm file cache; approximate, machine dependent). Concurrent classifications are not serialized: they share the
one ONNX session and run in parallel, so throughput grows with the cores while the latency of one call stays the same.
The ONNX models run on the CPU only. Disposing is idempotent
and thread-safe. After `Dispose()`, `EnsureModelAsync` and `ClassifyAsync` throw `ObjectDisposedException` (for the
Basic model as well, and even when the token is already cancelled): the Basic model throws every exception from the
call itself, an encoder model throws it from `EnsureModelAsync` but reports it through the task returned by
`ClassifyAsync`, so put the `await` inside the `try`. Disposing while `EnsureModelAsync` (or a lazy ensure) is
downloading cancels the download: the pending task throws `OperationCanceledException` and no partial file remains. The
hashing of files that are already in place is not interrupted: when no download is needed, the pending ensure completes
normally (the model is disposed nevertheless, so every later call throws). A `ClassifyAsync` that is still waiting for
its lazy ensure ends with `OperationCanceledException` (the download was cancelled) or, when the ensure finished first
and the call had not yet opened the ONNX session, with `ObjectDisposedException`; only a classification that already
runs on the session completes normally, and a call that never needs the model (no ambiguous word) is not affected.
Disposal never deletes the downloaded files; they stay in the store and are reused. Disposing while a classification
is running is safe for every model: the model is marked disposed at once (new calls throw), a running classification
completes normally, and an encoder model releases its ONNX session when the last of them finishes (`Dispose` never
waits). With a
model directory, the files are verified against their
SHA-256 without using the network, either by `EnsureModelAsync` or by the lazy ensure.

Threads and cancellation: the Basic model does everything synchronously on the calling thread and returns a task that
is already complete; an encoder model starts on the calling thread (argument checks, the lexicon load), hashes and
downloads on the thread pool, and then opens the ONNX session and runs the inference synchronously on the thread that
continues the call (it is not offloaded with `Task.Run`; do that yourself if the caller must not block). A
cancellation token is checked before the work starts, while the ensure is awaited (it cancels the wait of that call
only) and between the words that are encoded; a single
ONNX run is never interrupted. Cancelling the token of `EnsureModelAsync` does not cancel the shared ensure: it keeps
running even when no caller waits any more (only disposing the model cancels it), the next call joins it while it runs
(its `MirrorOptions` are ignored), returns at once when it has succeeded, and starts a new ensure with its own
`MirrorOptions` only after a failed one. An already cancelled token does not make `EnsureModelAsync` throw from the
call: it returns a cancelled task (awaiting it throws `OperationCanceledException`), for the Basic model as well, except
that a task that has already completed is returned as it is (the Basic model always returns a cancelled task); only a
disposed model throws from the call. An empty
element is an unknown word
(`null`, 0) and an element that contains a space is
looked up as one key, which no lexicon entry has, so it is unknown too: pass one token per element.

## Implementing `IWordSenseModel` Yourself

Anyone may implement `IWordSenseModel` (for example to wrap another engine) and use it wherever a supplied model fits.
An implementation must: throw `ArgumentNullException` for a null word list and `ArgumentException` for a null element;
return a non-null array parallel to the input (empty for an empty list); throw `ObjectDisposedException` from both
methods after `Dispose`; make `Dispose` idempotent and thread-safe; allow concurrent `ClassifyAsync` calls; and honor the
confidence semantics: an unknown word has `Details` null and confidence 0, a word with one sense gets it with confidence
1, and an ambiguous word gets its best sense with a confidence from 0 to 1 whose scale the implementation documents
(only a null `Details` means unknown; a non-null `Details` may carry any value from 0 to 1). `Dispose` must be safe
while calls run; `ClassifyAsync` should ensure lazily and only when a word needs it; enumerate the word list once; and
`EnsureModelAsync` must not throw for an already cancelled token but return a cancelled task (a task that has already
completed may be returned as it is).
It must also: return in `Word` the input element as given; take `Details` from `WordLexicon.Words[key]`, where the key is
the word lower-cased with U+2019 replaced by `'` (the shipped models do this, and an implementation should too, so that
every model finds the same candidates); honor the cancellation token; make `EnsureModelAsync` idempotent, thread-safe
and retryable; and throw its exceptions either synchronously or through the returned task (`BasicWordSenseModel` throws
from the call, except for a cancelled token in `EnsureModelAsync`; `OnnxWordSenseModel` throws `ObjectDisposedException`
from `EnsureModelAsync` and reports everything else through the task), so callers should `await` inside a `try`. The
supplied implementations are `BasicWordSenseModel` and `OnnxWordSenseModel` (used through `MiniLMWordSenseModel` and
`BertBaseCasedWordSenseModel`); `BasicWordSenseModel`, `MiniLMWordSenseModel` and `BertBaseCasedWordSenseModel` are
sealed, and `OnnxWordSenseModel` cannot be subclassed outside the WordSense packages (its constructor is not
accessible), so implement the interface yourself instead.

## Confidence

An encoder model scores the senses of a word, picks the best and reports it even when it is unsure. The confidence is a
monotonic function of the margin between the best and the second-best score, scaled by the lemma's own abstain
margin: it is exactly 0.5 where the model would, by that margin, have declined to answer, so below 0.5 the model's own
abstain rule would not have answered and 0.5 is the lowest meaningful value, not a useful filter. The recommended
starting threshold is **0.8 for each of the three models**, read in that model's own scale (see the table below).
`Details` is still the best candidate, so you can use it when you want an answer anyway. A word with several senses
but no weights for its lemma is `null` with confidence 0
(not the case for the shipped packages).

The test for "no sense chosen" is `Details is null`, not `Confidence == 0` (only a null `Details` means unknown or
unscored; an ambiguous word with non-null `Details` can have any confidence from 0 to 1). In code under a `DemaConsulting.*`
namespace the name `WordSense` resolves to the namespace `DemaConsulting.WordSense`, so add
`using WordSense = DemaConsulting.WordSense.WordSense;` or write the type fully qualified.

```csharp
WordSense[] senses = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
const double threshold = 0.8;   // the recommended start for this model; re-tune it per model
foreach (var sense in senses.Where(s => s.Details is not null))
{
    var use = sense.Confidence >= threshold ? "answer" : "guess";
    Console.WriteLine($"{sense.Word}: {sense.Details!.PartOfSpeech} ({sense.Confidence:P0}, {use})");
}
```

The confidence of the Basic model is a probability among the senses of the word, so it is not on the scale of the
encoder models; compare a confidence only with the same model. **Do not reuse a threshold across models**: a threshold
of 0.8 keeps about 78% of the ambiguous words on real text at about 93% precision for Basic (about one error in
fourteen), about 73% at about 98% for MiniLM (one in fifty) and about 70% at about 99% for BertBaseCased (one in a
hundred) (`basic-evaluation.md`, `model-comparison.md`); the confidence of Basic for a word with n senses is never
below 1/n (0.5 for two senses), so a threshold at or below 1/n is not a filter for Basic. MiniLM and BertBaseCased each
have their own margin scale
too, so tune a threshold per model on your own text. The test for "no sense chosen" is `Details is null`, not
`Confidence == 0`: only `Details is null` means unknown or unscored (confidence 0). A word that an encoder resolved has
non-null `Details` and may carry any confidence from 0 to 1, including exactly 0 or 1; a word with a single sense in the
lexicon gets 1. Below your threshold, treat the word as unresolved or low confidence: fall back to handling that does not
depend on `Meaning` (for example your default pronunciation), or show `Details` as a guess. Measured thresholds
(target words = the evaluation words, one annotated ambiguous word per
evaluation sentence; coverage = share of the target words whose confidence is at or above the threshold, precision =
share of those that are right; real text, holdout in parentheses; rounded):

| Model | at least 0.6 | at least 0.8 | at least 0.9 |
| --- | --- | --- | --- |
| Basic | 90% / 89% (95% / 90%) | 78% / 93% (86% / 94%) | 70% / 96% (78% / 96%) |
| MiniLM | 82% / 97% (88% / 99%) | 73% / 98% (79% / 99%) | 69% / 98% (75% / 99%) |
| BertBaseCased | 85% / 99% (94% / 99.7%) | 70% / 99% (87% / 99.7%) | 64% / 99% (81% / 99.7%) |

The full calibration buckets are in `model-comparison.md` in the build notes. The memory figures of the comparison
table do not include the 43 MiB of the word lexicon.

# Model Files

The encoder files are downloaded once to `<store>/<model-id>/` (default store:
`{LocalApplicationData}/DemaConsulting/WordSense/models`), each verified against its pinned SHA-256 and moved into
place atomically, so a partial download is never used. Valid files in the store are reused without network access.
Each file is requested from a mirror first (when one is configured) and then from the GitHub LFS copy of this
repository (`media.githubusercontent.com`); there is no other source.

| Model | Model id | File | Size (bytes) |
| ----- | -------- | ---- | -----------: |
| MiniLM | `all-MiniLM-L6-v2-int8` | `model_int8.onnx` | 22,972,370 |
| MiniLM | `all-MiniLM-L6-v2-int8` | `vocab.txt` | 231,508 |
| BertBaseCased | `bert-base-cased-int8` | `model_int8.onnx` | 108,477,896 |
| BertBaseCased | `bert-base-cased-int8` | `vocab.txt` | 213,450 |
| BertBaseCased | `bert-base-cased-int8` | `config.json` | 639 |
| BertBaseCased | `bert-base-cased-int8` | `tokenizer.json` | 668,923 |

The SHA-256 values are in `model-provenance.md` of the build notes and below. **All files of a model are verified**,
including `config.json` and `tokenizer.json` of BertBaseCased (the runtime reads only the ONNX file and `vocab.txt`; the
other two are provenance files that must still match).

| Model | File | SHA-256 |
| ----- | ---- | ------- |
| MiniLM | `model_int8.onnx` | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| MiniLM | `vocab.txt` | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |
| BertBaseCased | `model_int8.onnx` | `dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d` |
| BertBaseCased | `vocab.txt` | `eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02` |
| BertBaseCased | `config.json` | `19aa23b85dfc4ce3e199a4ab06d51acd4b7bfc9ae9bae62b78823dd5dfb0c286` |
| BertBaseCased | `tokenizer.json` | `343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1` |

Basic and the core package have no model files. The download of MiniLM is 22 MiB (`model_int8.onnx` 22,972,370 bytes,
`vocab.txt` 231,508 bytes) and that of BertBaseCased
104 MiB (`model_int8.onnx` 108,477,896 bytes plus three small files). All sizes in this guide are in MiB (1 MiB =
1,048,576 bytes). The default store is `%LOCALAPPDATA%\DemaConsulting\WordSense\models` on Windows and
`~/.local/share/DemaConsulting/WordSense/models` on Linux and macOS (`$XDG_DATA_HOME` is honored when set); the
`ModelId` values are `all-MiniLM-L6-v2-int8` and `bert-base-cased-int8`, and they are the sub-folder names in the store
and on a mirror. The memory figures in the comparison table are what a model adds and do not include the 43 MiB of
the word lexicon.

## Offline and Air-Gapped Use

On a machine without internet access, copy the model files of the table above into one folder (from
`model/<model-id>/` of the repository, or from a machine that has run `EnsureModelAsync`) and pass the folder to the
constructor. The folder is flat: the files sit directly in it, with no `<model-id>` subfolder (the store and a mirror
use `<model-id>` folders, a supplied directory does not). Calling `EnsureModelAsync` is optional but recommended: it
verifies every file against its pinned SHA-256 up front and never uses the network (without it, `ClassifyAsync`
verifies lazily and may throw the same `FileNotFoundException` or `InvalidDataException`).

```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

using var model = new MiniLMWordSenseModel(modelDirectory: @"C:\models\all-MiniLM-L6-v2-int8");
await model.EnsureModelAsync();
```

`BertBaseCasedWordSenseModel` works the same way with its four files in the folder.

Alternatively, copy the files to a mirror inside your network (next section).

## Configuring a Mirror

A mirror is a web location that serves `{BaseUri}/{model-id}/{file-name}` for every file of the model. It is tried
before GitHub. A file that does not match its SHA-256 is discarded and the next source is tried. Pass a
`MirrorOptions` to `EnsureModelAsync`; `MirrorOptions` is a sealed record with `BaseUri`, `BearerToken`, `Credentials`
(an `ICredentials`, for HTTP Basic), `StoreRoot` (where the model folders are stored) and `Timeout` (per file, default
5 minutes).

```csharp
var mirror = new MirrorOptions(
    BaseUri: new Uri("https://models.example.com/wordsense"),
    BearerToken: "token",
    Credentials: null,
    StoreRoot: null,
    Timeout: TimeSpan.FromMinutes(2));
using var model = new MiniLMWordSenseModel();
await model.EnsureModelAsync(mirror);
```

Without an argument, the mirror comes from environment variables:

| Variable | Meaning |
| -------- | ------- |
| `WORDSENSE_MIRROR_URL` | The base URI of the mirror; the other variables are used only when it is set |
| `WORDSENSE_MIRROR_USER` | The user name for HTTP Basic authentication (needs the password) |
| `WORDSENSE_MIRROR_PASSWORD` | The password |
| `WORDSENSE_MIRROR_BEARER_TOKEN` | A bearer token (exclusive with user and password) |

The base URI must be absolute `http` or `https` without query, fragment or user-info; a relative URI, another scheme,
a query, a fragment or user-info (in `MirrorOptions.BaseUri` or in `WORDSENSE_MIRROR_URL`) throws `ArgumentException`
through the task of `EnsureModelAsync` (the `MirrorOptions` constructor itself does not validate). A bearer token or
credential is
sent only to the host of the mirror, never to a redirect target on another host, and is rejected for plain `http`
unless the host is a loopback address. `ToString()` of a `MirrorOptions` never prints a secret. A mirror is ignored by
models that need no files and by a model that was given a model directory.

Details of the variables (the settings are read only when a mirror is needed, that is by `EnsureModelAsync` without a
`MirrorOptions` or by the lazy ensure of an ambiguous word): a blank value counts as unset; a user without a password
(or the reverse), a user, password or token without `WORDSENSE_MIRROR_URL`, and a token together with a user all throw
`ArgumentException` (reported through the task); with a model directory the variables are never read, so an invalid
setting is ignored without an exception, and a passed `MirrorOptions` is not consulted either. A `Timeout` that is zero,
negative or longer than about 49 days throws `ArgumentOutOfRangeException` (`Timeout.InfiniteTimeSpan` means no
limit); it
applies only to a `MirrorOptions` you pass, because the variables have no timeout (5 minutes then).

**Relocating the store.** Downloaded files live in `<StoreRoot>/<model-id>/`; the default store root is
`{LocalApplicationData}/DemaConsulting/WordSense/models`. Only `MirrorOptions.StoreRoot` changes it, for example
`StoreRoot: "/var/lib/wordsense"` on Linux and macOS or `StoreRoot: @"C:\ProgramData\WordSense\models"` on Windows. A
relative `StoreRoot` is allowed: it is resolved against the current directory of the process when the ensure runs, so
prefer an absolute path (the directory is created when it does not exist). No environment
variable does: the four `WORDSENSE_MIRROR_*` variables above are the only ones the library reads. `BaseUri` is a required
constructor argument, so a `MirrorOptions` always names a mirror; setting only a store root is not possible today
(without a mirror, the default store is used, or pass a model directory with files you placed yourself).

**Pre-populated store (offline route).** Files that you copy into `<StoreRoot>/<model-id>/` (or into the default store,
with no `MirrorOptions` at all) are hashed by `EnsureModelAsync`: a file that matches its pinned SHA-256 is kept and
never requested, so when every file is present and valid nothing is downloaded and neither the mirror nor GitHub is
contacted; a file that is missing or does not match is downloaded again and replaced. With a `StoreRoot` you must still
pass a valid `BaseUri` (it is not contacted when the files are valid), and on a machine without network a missing or
damaged file ends in `HttpRequestException`; a model directory passed to the constructor never uses the network.

**Network notes.** Downloads use `HttpClient` with the default .NET proxy behavior (the system proxy settings, and the
`HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY` variables on Linux and macOS); no proxy credentials are configured by the
library. Allow-list your mirror host (if any) and, for the public source, `media.githubusercontent.com` (files under
`model/{model-id}/{file}` in the WordSense repository); HTTP redirects are followed by the library itself
(at most 10 hops, http or https only), so the host a redirect points to must be reachable as well. Several processes can
share one store: every download goes to its own uniquely named temporary file, is verified and then moved into place,
so nobody sees a partial file (at worst two processes download the same file). A file in the store that is missing or
does not match its SHA-256 is downloaded again and replaced; it is never used.

## Handling Failures

`EnsureModelAsync` throws:

| Exception | When |
| --------- | ---- |
| `ArgumentException` | The mirror configuration is invalid (a bad timeout: `ArgumentOutOfRangeException`) |
| `HttpRequestException` | No source delivered a verified file (a downloaded file with a wrong SHA-256 is discarded) |
| `FileNotFoundException` | A supplied model directory does not exist or lacks a file |
| `InvalidDataException` | A file of a supplied model directory does not match its SHA-256 |
| `IOException`, `UnauthorizedAccessException` | The store cannot be created or written |
| `ObjectDisposedException` | The model has been disposed (`EnsureModelAsync` and `ClassifyAsync`) |
| `OperationCanceledException` | The token was cancelled (this call's wait only), or disposed mid-download |

The Basic model throws only `ObjectDisposedException` (from the call) and returns a cancelled task for an already
cancelled token. A failed call can
be repeated, and concurrent calls share one
ensure; cancelling does not stop the shared ensure (see "Threads and cancellation").

# Limitations

- **Words are classified by what the table knows.** A word that is not in `WordLexicon.Words` is unknown, and so are
  determiners, numbers and most proper nouns. There is no morphology at use time and no capitalization or possessive
  heuristic for looking a word up: a form that is not in the table is unknown.
- **Only some multi-sense words are decided.** The encoder models decide the lemmas that have trained weights (see the
  appendix); a multi-sense word without weights is `null` with confidence 0. Basic decides by part of speech only.
- **Pronunciation for heteronyms only.** A lemma that is not a heteronym has no `Meaning` or `Respelling`, and the
  library has no phonetic data of its own: the `Respelling` is a plain hint, and the consumer maps (`Lemma`,
  `Meaning`) to its own pronunciation.
- **Confidence is a score, not a probability**, and its scale is specific to each model.
- **English only**, and the context is a window of at most `maxSequenceLength` tokens around the target word.
- **The encoder models need files**: a download on first use (22 MiB or 104 MiB) or a model directory, and ONNX Runtime
  needs native libraries for the target platform. They run on the CPU only.
- **Weights are fixed to the model file.** The weights format (`WSX1`) is dense, and the weights of a model are only
  valid for its exact pinned files; other files are refused by the SHA-256 check.

# Licensing and Provenance

WordSense is licensed under the MIT License. The `all-MiniLM-L6-v2` and `bert-base-cased` models (Apache License 2.0)
and ONNX Runtime (MIT) are used under their own licenses.

The word table is derived from Moby Part-of-Speech II by Grady Ward (<https://www.gutenberg.org/ebooks/3203>), which
the author dedicated to the public domain; no attribution is required, but it is given here with thanks. Only a
filtered list is committed, in `corpus/lexicon/`, with its source, checksum and filter rules in
`corpus/lexicon/PROVENANCE.md`.

The training corpus is not shipped in the NuGet packages; only the derived weights are embedded. Every corpus sentence
has a row in `corpus/provenance/*.tsv` recording its source, license, URL and contributor. The licenses in use are
`CC0 1.0`, `Public domain`, `CC BY 2.0 FR` (Tatoeba, with the contributor recorded), `CC BY-SA 4.0` (Wikipedia and
Universal Dependencies English EWT sentences), `MIT (project-authored)` and, as a last resort for a handful of senses,
`web-excerpt`. Sentences under `CC BY-SA 4.0` keep that license: if you redistribute the corpus (not the package),
those rows must be shared under the same terms with attribution. The shipped weights are statistical summaries of many
sentences and do not contain the sentences; this is the project's reading and not legal advice. The build notes
(`model-provenance.md`) describe how the model files were produced, and `model-comparison.md` and
`basic-evaluation.md` give the measured quality of each model.

# Extending the Corpus

The word table and the model weights are generated by the non-shipped builder `tools/WordSense.Builder` from the files
in `corpus/`; the format of the files is described in `corpus/README.md` and the harvest of real sentences in
`tools/real-text/README.md`. All commands run from the repository root with
`dotnet run --project tools/WordSense.Builder -c Release -- <command> [--name value]`:

| Command | What it does |
| ------- | ------------ |
| `words` | Builds the word table (`words.bin`) of the core package, including the materialized inflections |
| `weights <modelId>` | Trains the weights of an encoder model (`minilm` or `bert-base-cased`) |
| `evaluate <modelId>` | Measures a model (`minilm`, `bert-base-cased` or `basic`) on holdout and real text |
| `ud-counts` | Counts the tag statistics of the pinned UD English-EWT treebank into `corpus/ud` |
| `train-basic` | Retrains the whole Basic model and writes its table (`Resources/basic.bin`) |
| `tune-basic` | Compares hyperparameter variants of Basic on UD development and anchor subsets; writes nothing |
| `appendix` | Regenerates the generated senses appendix of this guide |

The options `--corpus`, `--repository`, `--output` and (for `weights` and `evaluate`) `--model` default to the
repository layout.

## Corpus Format

- `anchors/*.tsv` (training) and `holdout/*.tsv` (evaluation) have three tab-separated columns and no header:
  `lemma`, `label`, `sentence`. The label names one sense of the lemma in `senses/*.tsv`, and the sentence marks
  the target by one pair of braces: `The {record} for the fastest mile was set in 1999.`
- `senses/*.tsv` is the inventory of the senses, with the header `lemma`, `label`, `pos`, `meaning` and
  `respelling`: the part of speech of the label and, for a heteronym only, its pronunciation `meaning` id and plain
  `respelling`.
- `forms/speech.tsv` maps inflected forms to lemmas (`form`, `lemma`, `labels`).
- `provenance/*.tsv` has one row per sentence: `id` (first 12 hex characters of the SHA-256 of `lemma|sentence`),
  `lemma`, `sense`, `split`, `source`, `licence`, `source_key`, `url`, `contributor` and `origin`.
- `realtext/eval-seed.tsv` holds real sentences that are used for evaluation only and never for training.

## Adding a Word

1. Write the senses of the lemma into a `senses/*.tsv` file with their part of speech and, for a heteronym, the
   pronunciation meaning and respelling of each.
2. Add at least four anchor sentences for each label to a new `anchors/my-batch.tsv`, with different holdout sentences
   (ideally from different sources) in `holdout/my-batch.tsv`. The braced target may be any form.
3. Add every braced form to `forms/speech.tsv` with its lemma and the labels it can take, unless the form is the lemma.
4. Add a provenance row for every sentence to `provenance/my-batch.tsv`: source, license from the list above,
   a URL for web sources and the origin (`real`, `authored` or `user-supplied`).
5. Rebuild the word table, retrain each encoder model and measure the result:

   ```text
   dotnet run --project tools/WordSense.Builder -c Release -- words
   dotnet run --project tools/WordSense.Builder -c Release -- weights minilm
   dotnet run --project tools/WordSense.Builder -c Release -- weights bert-base-cased
   dotnet run --project tools/WordSense.Builder -c Release -- train-basic
   dotnet run --project tools/WordSense.Builder -c Release -- evaluate minilm
   dotnet run --project tools/WordSense.Builder -c Release -- evaluate bert-base-cased
   dotnet run --project tools/WordSense.Builder -c Release -- evaluate basic
   dotnet run --project tools/WordSense.Builder -c Release -- appendix
   ```

6. Check the reports, run `pwsh ./build.ps1` and commit the corpus files, the tables, the weights and the appendix
   together.

# Troubleshooting

## The Model Cannot Be Downloaded

`HttpRequestException` from `EnsureModelAsync` means that no source delivered a verified file. Offline or behind a
firewall, configure a mirror or pass a model directory to the constructor.

## Timeouts or a Slow First Run

The model files are 22 MiB (MiniLM) or 104 MiB (BertBaseCased). Raise `MirrorOptions.Timeout` (default 5 minutes per
file), check the proxy settings of the host, or use a mirror close to it.

## A Model File Does Not Match Its SHA-256

`InvalidDataException` from a model directory means that a file is stale, truncated, modified or a Git LFS pointer.
In the store, a bad file is replaced by a new download. Note that a mirror must serve the exact pinned files: the
upstream `onnx/model_int8.onnx` of bert-base-cased is a different file than the one this library uses.

## Git LFS Pointer Instead of the Model

In a clone made without Git LFS, the files in `model/` are short text files that start with `version https://git-lfs`,
and the tests that need the models skip. Run `git lfs install` and `git lfs pull`. Package users are not affected: the
downloader fetches the real files.

## The Mirror Configuration Is Rejected

An `ArgumentException` about the mirror means that `WORDSENSE_MIRROR_URL` is not an absolute http(s) URL, that
only one of user and password is set, that both a user and a bearer token are given, or that credentials are used
with plain http for a host that is not loopback.

## A Word Returns Nothing

`Details` is `null` for a word that is unknown, or for an encoder model that has several senses but no weights for the
lemma (not the case for the shipped packages). Check
`WordLexicon.Words.ContainsKey(word.ToLowerInvariant())`: when it is `false` the word is unknown to the table; a
contraction such as `don't` or a hyphenated word kept as one token is also unknown. When it is `true`, the word has an
answer from every shipped model, with a low confidence if the model is unsure.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
- [REF-3] bert-base-cased (<https://huggingface.co/bert-base-cased>)
