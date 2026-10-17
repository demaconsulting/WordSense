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
| Basic | about 89% / 85% | none (83 KiB table inside the package) | under 0.1 ms | about 21 MiB |
| MiniLM | about 97% / 93% | 22 MiB | about 2 ms | about 64 MiB |
| BertBaseCased | about 99% / 97% | 104 MiB | about 9 ms | about 150 MiB |

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
costs no model call. The capitalization of a word and a trailing possessive are no longer used to skip a word:
whatever is in the word table is classified, and the table covers the inflected forms.

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
| `Mr` + `.`, `U` + `.` + `S` + `.` | Each period ends a segment for Basic and cuts its context; write `Mr.` as one token or drop the periods |
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
downloader state, so dispose it with `using`, after every classification has finished. The Basic model has nothing to
release but follows the same contract. The usual sequence is: call `EnsureModelAsync` once, classify as many texts as
you like with `ClassifyAsync` (safe for concurrent use) and dispose the model. `EnsureModelAsync` is optional:
`ClassifyAsync` ensures the model lazily on first need, with the default (or environment variable) mirror settings, so
pass a mirror or a model directory through `EnsureModelAsync` (or the constructor) first if you need them. Text whose
words the word table alone resolves (no word with several senses) never needs the model: nothing is downloaded, opened
or verified, and a model that is never needed is never downloaded. Concurrent callers share one ensure, and a failed
ensure can be retried by the next call. The ONNX session and the weights are loaded at the first classification that needs
them: about 0.4 s for MiniLM and about 0.5 s for BertBaseCased, on top of the word table (about 120 ms)
and of the hashing of the model files in `EnsureModelAsync` (approximate, machine dependent). Disposing is idempotent
and thread-safe. After `Dispose()`, `EnsureModelAsync` and `ClassifyAsync` throw `ObjectDisposedException` (for the
Basic model as well). Disposing while `EnsureModelAsync` (or a lazy ensure) is
downloading cancels the download: the pending task throws `OperationCanceledException` and no partial file remains.
Do not dispose a model while a classification is running. With a model directory, the files are verified against their
SHA-256 without using the network, either by `EnsureModelAsync` or by the lazy ensure.

## Confidence

An encoder model scores the senses of a word, picks the best and reports it even when it is unsure. The confidence is a
monotonic function of the margin between the best and the second-best score, scaled by the lemma's own abstain
margin: it is exactly 0.5 where the model would, by that margin, have declined to answer. Treat a confidence of at
least 0.5 as an answer you can use and a lower one as a guess; `Details` is still the best candidate, so you can use it
when you want an answer anyway. A word with several senses but no weights for its lemma is `null` with confidence 0
(not the case for the shipped packages).

```csharp
WordSense[] senses = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
foreach (var sense in senses.Where(s => s.Details is not null))
{
    var use = sense.Confidence >= 0.5 ? "answer" : "guess";
    Console.WriteLine($"{sense.Word}: {sense.Details!.PartOfSpeech} ({sense.Confidence:P0}, {use})");
}
```

The confidence of the Basic model is a probability among the senses of the word, so it is not on the scale of the
encoder models; compare a confidence only with the same model. **Do not reuse a threshold across models**: 0.5 is the
natural point for the encoder models, while for Basic a threshold such as 0.8 keeps about 78% of the ambiguous words
on real text at about 93% precision (`basic-evaluation.md`). MiniLM and BertBaseCased each have their own margin scale
too, so tune a threshold per model on your own text. Measured thresholds (coverage = share of the ambiguous target
words kept, precision = share of those that are right; real text, holdout in parentheses; rounded):

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
use `<model-id>` folders, a supplied directory does not). You must still call `EnsureModelAsync`: it verifies every
file against its pinned SHA-256 and never uses the network.

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

The base URI must be absolute `http` or `https` without query, fragment or user-info. A bearer token or credential is
sent only to the host of the mirror, never to a redirect target on another host, and is rejected for plain `http`
unless the host is a loopback address. `ToString()` of a `MirrorOptions` never prints a secret. A mirror is ignored by
models that need no files and by a model that was given a model directory.

**Relocating the store.** Downloaded files live in `<StoreRoot>/<model-id>/`; the default store root is
`{LocalApplicationData}/DemaConsulting/WordSense/models`. Only `MirrorOptions.StoreRoot` changes it. No environment
variable does: the four `WORDSENSE_MIRROR_*` variables above are the only ones the library reads. `BaseUri` is a required
constructor argument, so a `MirrorOptions` always names a mirror; setting only a store root is not possible today
(without a mirror, the default store is used, or pass a model directory with files you placed yourself).

## Handling Failures

`EnsureModelAsync` throws:

| Exception | When |
| --------- | ---- |
| `ArgumentException` | The mirror configuration is invalid (a bad timeout: `ArgumentOutOfRangeException`) |
| `HttpRequestException` | No source delivered a verified file (store and download) |
| `FileNotFoundException` | A supplied model directory does not exist or lacks a file |
| `InvalidDataException` | A file of a supplied model directory does not match its SHA-256 |
| `IOException`, `UnauthorizedAccessException` | The store cannot be created or written |
| `ObjectDisposedException` | The model has been disposed (`EnsureModelAsync` and `ClassifyAsync`) |
| `OperationCanceledException` | The token was cancelled, or the model was disposed while the call was downloading |

The Basic model throws only the cancellation exception and, after disposal, `ObjectDisposedException`. A failed call can
be repeated, and concurrent calls share one
download.

# Limitations

- **Words are classified by what the table knows.** A word that is not in `WordLexicon.Words` is unknown, and so are
  determiners, numbers and most proper nouns. There is no morphology at use time and no capitalization or possessive
  heuristic: a form that is not in the table is unknown.
- **Only some multi-sense words are decided.** The encoder models decide the lemmas that have trained weights (see the
  appendix); a multi-sense word without weights is `null` with confidence 0. Basic decides by part of speech only.
- **Pronunciation for heteronyms only.** A lemma that is not a heteronym has no `Meaning` or `Respelling`, and the
  library has no phonetic data of its own: the `Respelling` is a plain hint, and the consumer maps (`Lemma`,
  `Meaning`) to its own pronunciation.
- **Confidence is a score, not a probability**, and its scale is specific to each model.
- **English only**, and the context is a window of at most `maxSequenceLength` tokens around the target word.
- **The encoder models need files**: a download on first use (22 MiB or 104 MiB) or a model directory, and ONNX Runtime
  needs native libraries for the target platform.
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
