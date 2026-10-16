# Introduction

## Purpose

This document is the user guide for WordSense, a .NET library that tells, for English words in context and offline,
the part of speech and the pronunciation. It tells, for example, whether "record" is a noun or a verb, and
whether "bow" is said like "bough" (a bend) or like "beau" (a weapon). Every word is looked up in an embedded
dictionary of over 140,000 words; only a word with several meanings that the library has been trained on is decided
by an encoder model from its sentence. The library comes as two NuGet packages: `DemaConsulting.WordSense`, the core
with the API and the dictionary, which works on its own without any model, and `DemaConsulting.WordSense.MiniLM`,
which adds the MiniLM encoder model and the weights trained for it.

## Scope

This user guide covers:

- Installation of the two packages, requirements and a quick start
- The classification API: words, text, outcomes, confidence, pronunciation and listing the candidates
- Using the core package alone, without a model
- Model download, mirrors and offline use
- Accuracy, limitations and abstention
- Licensing and provenance of the data
- Extending the training corpus
- Troubleshooting
- A glossary of the terms used
<!-- markdownlint-disable MD051 -->
<!-- The appendix is a separate file that pandoc concatenates, so its heading anchor only exists in the PDF. -->
- [Appendix: Supported Senses](#appendix-supported-senses): the catalog of all supported lemmas with their parts of
  speech, pronunciation meanings and forms (generated from the corpus)
<!-- markdownlint-enable MD051 -->

The code samples in this guide are compiled by the test suite (`UserGuideSampleTests`): each C# block is preceded by
a `sample` marker comment in the source of this guide and must equal the code in
`test/DemaConsulting.WordSense.MiniLM.Tests/UserGuideSamples.cs`. The samples assume `ImplicitUsings`,
`using DemaConsulting.WordSense;` and `using DemaConsulting.WordSense.MiniLM;`.

# Glossary

- **Lemma**: the dictionary base form of a word, for example "record" for "records".
- **Dictionary**: the embedded word list (derived from Moby Part-of-Speech II) that gives every word its possible
  parts of speech. A word in it is *known*.
- **Dictionary answer**: an answer taken from the dictionary alone, because the word has exactly one part of speech.
  It has `Basis` `Dictionary`, needs no encoder and has a margin of 0.
- **Context answer**: an answer chosen by the encoder from the sentence for a word that has a trained classifier. It
  has `Basis` `Context` and a margin.
- **Basis**: where the result of a word comes from (`AnswerBasis`): `Context`, `Dictionary` or `None` (unknown word).
- **Candidates**: the possible answers of a word's lemma (`WordAnalysis.Candidates`, `GetCandidates`): the senses of its
  entry, one per part of speech and, for a heteronym, per pronunciation.
- **Heteronym**: a word that is spelled the same but pronounced differently depending on its use, for example
  "record" (the noun is said REK-urd, the verb rih-KORD) or "bow".
- **Part of speech**: the grammatical role of a word in its sentence, such as noun, verb, adjective or adverb.
- **Meaning**: a short id, for example `bend` or `weapon` for "bow", that names which of the pronunciations of a
  heteronym is meant; it is only given for heteronyms.
- **Respelling**: a plain-letter hint of how to say the word, for example `boh`; it is a guide for a person or a
  speech engine and not a phonetic transcription.
- **Abstain**: to give no single answer for a word the library knows but cannot decide: its context margin is too low,
  or it has several parts of speech and no trained classifier. `Details` is `null` and `Candidates` lists the options.
- **Margin**: the confidence of an answer, the score of the best candidate minus the score of the runner-up; the
  higher it is, the surer the library is (it is a score difference, not a probability).

# Installation

```bash
dotnet add package DemaConsulting.WordSense
dotnet add package DemaConsulting.WordSense.MiniLM
```

`DemaConsulting.WordSense` is the core: the public API, the embedded dictionary and the `IWordSenseModel`
abstraction. It has no runtime dependencies (no ONNX Runtime and no tokenizer) and works on its own: see "Using the
Core Package Alone". `DemaConsulting.WordSense.MiniLM` depends on the core and on Microsoft.ML.OnnxRuntime; it adds the
`all-MiniLM-L6-v2` encoder, its downloader and the weights table trained for it, which is what decides a word with
several meanings from its sentence. Install both for the full behavior; the examples below use both.

## Requirements

- .NET 8, 9 or 10 (the packages target `net8.0`, `net9.0` and `net10.0`) on Windows, Linux or macOS.
- For the MiniLM package, about 23 MB of disk space in a per-user directory for the encoder model, which is downloaded
  on first use. The NuGet package contains the weights table but not the model. A machine that cannot reach the
  internet needs a mirror or a pre-seeded model directory (see "Model Files and Mirrors"). The core package needs
  neither.
- Contributors who clone the repository also need [Git LFS](https://git-lfs.com): the files in `model/` are stored
  in Git LFS (`git lfs install` once, then `git lfs pull`). See "Troubleshooting".

# Quick Start

<!-- sample:quick-start -->
```csharp
// Downloads the model on first use (mirror, Hugging Face, GitHub LFS), verifies its SHA-256 and reuses it later
using var classifier = await MiniLMWordSense.CreateClassifierAsync();

string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "the", "new", "album"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words);

for (var i = 0; i < words.Length; i++)
{
    if (analyses[i].Details is { } details)
    {
        Console.WriteLine($"{words[i]}: {details.PartOfSpeech} {details.Pronunciation?.Meaning} ({analyses[i].Basis})");
    }
}
```

The input is a list of words that you have tokenized yourself, in order and including punctuation tokens; they are
read as one running text. `ClassifyAsync` returns a non-null array that is parallel to the input: element `i` is the
`WordAnalysis` of `words[i]`, whatever the word is.

## The Flow

Every word follows the same path:

1. The surface form is reduced to a **lemma**: a form map handles irregular and trained forms, and gated morphology
   handles the regular ones. Plural `-s`, `-es` and `-ies` and adverb `-ly` are enabled; `-ed`, `-ing`, `-er`/`-est`
   and the possessive of a dictionary word are disabled, because they did not reach the precision gate.
2. The lemma is looked up as an **entry** with one or more senses (a part of speech and, for a heteronym, a
   pronunciation) and, for 744 lemmas, trained weights.
3. One sense gives a **dictionary answer** without the encoder. Several senses with trained weights are decided by the
   encoder from the sentence (a **context answer**). Several senses without weights give the candidates only.
4. A word that is not in the dictionary, a determiner, a number, a proper noun or a capitalized word inside a sentence
   is **unknown**.

The encoder runs only when a word needs it, so a sentence without such a word costs no model call. Without a model
(core package alone) step 3 gives the candidates only for every word with several senses.

A caller gets two facts per answered word:

- The **part of speech** of the word in its sentence (`WordDetails.PartOfSpeech`), together with the `Lemma`.
- For a **heteronym**, the **pronunciation** (`WordDetails.Pronunciation`): a `Meaning` id and an optional
  `Respelling`. A word that is not a heteronym has a `null` `Pronunciation`.

The `PartOfSpeech` enumeration defines the eight traditional parts of speech. Context answers are a noun, a verb, an
adjective or an adverb; dictionary answers can also be a pronoun or a preposition (conjunctions and interjections only
appear as candidates of a word that has another part of speech).

The
<!-- markdownlint-disable-next-line MD051 -->
[appendix](#appendix-supported-senses) is the catalog of the trained lemmas, their parts of speech and their
pronunciation meanings.

# Usage

## Outcomes

Each `WordAnalysis` is one of five outcomes; `Basis` tells them apart without looking at the margins:

| Outcome             | Details   | Abstained | Basis        | Margin        | Candidates               |
|---------------------|-----------|-----------|--------------|---------------|--------------------------|
| Dictionary answer   | the sense | `false`   | `Dictionary` | 0             | the one sense            |
| Context answer      | the best  | `false`   | `Context`    | the score gap | every sense of the lemma |
| Context abstention  | `null`    | `true`    | `Context`    | the score gap | every sense of the lemma |
| Candidates only     | `null`    | `true`    | `Dictionary` | 0             | the possible senses      |
| Unknown             | `null`    | `false`   | `None`       | 0             | empty                    |

<!-- sample:outcomes -->
```csharp
string[] words = ["The", "hunter", "will", "record", "the", "record"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words);

foreach (var analysis in analyses)
{
    var outcome = analysis switch
    {
        { Basis: AnswerBasis.None } => "unknown",
        { Details: not null, Basis: AnswerBasis.Dictionary } => "dictionary answer",
        { Details: not null } => "context answer",
        { Basis: AnswerBasis.Context } => "context abstention",
        _ => "candidates only",
    };
    var candidates = string.Join("/", analysis.Candidates.Select(c => c.PartOfSpeech));
    Console.WriteLine($"{analysis.Word,-8} {outcome,-18} {candidates}");
}
```

A **dictionary answer** differs from a **context answer** in what it knows. It says what the word is in the
dictionary, not what it is in this sentence, so it is only given when the dictionary has one part of speech for the
word; it carries no confidence (`Margin` 0, `RunnerUp` `null`) and needs no encoder. A context answer is chosen by the
encoder from the sentence, so it has a `Margin` and a `RunnerUp`, and the classifier can abstain from it (`Details`
`null`, `Abstained` `true`, `Basis` `Context`, with `Margin` still set). A word that is in the dictionary with several
senses and has no trained classifier is never decided: it is the candidates-only outcome, and you pick from
`Candidates` yourself.

## Text Input

If you have raw text instead of tokens, pass the string:

<!-- sample:text -->
```csharp
IReadOnlyList<WordSenseToken> tokens = await classifier.ClassifyAsync("They will record the record.");

foreach (var token in tokens)
{
    Console.WriteLine($"{token.Start,3} {token.Text,-8} {token.Analysis.Details?.PartOfSpeech.ToString() ?? "-"}");
}
```

The tokens tile the input: concatenating `Text` reproduces the string exactly, so results map back onto it (`Start`
and `Length` are UTF-16 offsets). Words, runs of whitespace and single punctuation characters are separate tokens;
each token has a `WordAnalysis`, and `Details` is set only for answered words (dictionary or context).

## Confidence and Abstention

Every call returns the full result, so there is no separate detail mode:

<!-- sample:confidence -->
```csharp
string[] words = ["They", "will", "record", "the", "record"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words);

foreach (var analysis in analyses.Where(a => a.RunnerUp is not null))
{
    Console.WriteLine(
        $"{analysis.Word} margin={analysis.Margin:F2} runner-up={analysis.RunnerUp} abstained={analysis.Abstained}");
}
```

A `WordAnalysis` holds the `Word` as given, `Details` (the best answer), the `Margin` (the score of the best candidate
minus the runner-up), the `RunnerUp` candidate, the `RunnerUpMargin` (runner-up minus the third best; 0 for a lemma
with two candidates), `Abstained`, the `Basis` and the `Candidates`. Margin and runner-up exist only for a context
classification (the sample lists exactly those words); the margin and the runner-up are also filled in for a word the
classifier abstained from, so you can apply your own policy. For a dictionary answer, a candidates-only word and an
unknown word both margins are 0 and `RunnerUp` is `null`.

## Pronunciation for Speech

A speech synthesizer or another consumer uses `Pronunciation` to pick how to say a heteronym. The library has no
phonetic data of its own: `Meaning` is a stable id, so the consumer keeps its own pronunciations keyed by
(`Lemma`, `Meaning`), and `Respelling` is a ready-made plain hint to use or to display:

<!-- sample:pronunciation -->
```csharp
string[] words = ["They", "will", "record", "the", "record"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words);

foreach (var analysis in analyses)
{
    if (analysis.Details is { Pronunciation: { } pronunciation } details)
    {
        Console.WriteLine(
            $"{analysis.Word}: {details.Lemma}/{pronunciation.Meaning}, say \"{pronunciation.Respelling}\"");
    }
}
```

A word that is not a heteronym (or an unknown or abstained word) has no pronunciation; the consumer then falls back to
its normal pronunciation of the word. The two uses of `record` have the meanings `noun` (REK-urd) and `verb`
(rih-KORD); `bow` has `bend` (bow, as in "take a bow") and `weapon` (boh).

## Listing the Candidates

<!-- sample:candidates -->
```csharp
Console.WriteLine($"{classifier.ContextLemmas.Count} lemmas with a trained classifier");

foreach (var candidate in classifier.GetCandidates("record"))
{
    Console.WriteLine($"{candidate.Lemma}: {candidate.PartOfSpeech} pronunciation={candidate.Pronunciation?.Meaning}");
}

// Any lemma of the dictionary has candidates, not only the trained ones
Console.WriteLine(string.Join("/", classifier.GetCandidates("will").Select(c => c.PartOfSpeech)));
```

`ContextLemmas` lists only the lemmas that have a trained classifier, the ones decided from their context (the
appendix lists them). `GetCandidates` works for any lemma of the dictionary and returns an empty list for an unknown
lemma, or for a dictionary-only lemma when `UseLexicon` is `false`.

## Abstention

For each trained lemma the table stores a margin threshold. When the best candidate beats the runner-up by less than
that threshold, the classifier abstains: `Details` is `null` and `Abstained` is `true`, while `Margin` and `RunnerUp`
still report the best guess. Abstaining trades coverage for precision (about 91 % of the held-out rows are answered, see
"Accuracy and Performance"). Scale all thresholds with `WordSenseClassifierOptions.AbstainMarginScale`. The model
settings (`IntraOpThreads`, `MaxSequenceLength`) belong to `MiniLMWordSenseOptions`:

<!-- sample:tuning -->
```csharp
var miniLmOptions = new MiniLMWordSenseOptions
{
    IntraOpThreads = 2,       // ONNX Runtime threads; 0 selects the default
    MaxSequenceLength = 128,  // model window, 8 to 512 tokens; longer texts use a window around each target
};
var classifierOptions = new WordSenseClassifierOptions
{
    AbstainMarginScale = 0.5, // below 1 answers more often, above 1 abstains more often; 0 never abstains
};

using var tuned = await MiniLMWordSense.CreateClassifierAsync(miniLmOptions, classifierOptions);
```

## Dictionary Option

`WordSenseClassifierOptions.UseLexicon` is `true` by default. Set it to `false` to answer only the trained lemmas, as
the first versions did: every other word is then unknown (`Basis` `None`) and `GetCandidates` returns nothing for a
dictionary-only lemma. The option never changes a context answer.

<!-- sample:lexicon -->
```csharp
// Answer only the trained lemmas, as before the dictionary existed; every other word is unknown
var options = new WordSenseClassifierOptions { UseLexicon = false };

using var trainedOnly = await MiniLMWordSense.CreateClassifierAsync(null, options);
```

## Using the Core Package Alone

`DemaConsulting.WordSense` works without a model, without ONNX Runtime and without network access.
`WordSenseClassifier.Create()` builds a classifier from the embedded dictionary only:

```csharp
using var lexiconOnly = WordSenseClassifier.Create();
WordAnalysis[] analyses = await lexiconOnly.ClassifyAsync(["The", "hunter", "will", "record"]);
```

A word with one part of speech ("hunter") is answered from the dictionary (`Basis` `Dictionary`). A word with several
senses ("will", "record") cannot be decided from its sentence without a model: it abstains with `Basis` `Dictionary`,
`Details` `null` and its `Candidates`, and `ContextLemmas` is empty. Unknown words are unknown as usual. Adding the
MiniLM package changes only those multi-sense words that have trained weights: they are then decided from the sentence.

## Other Models

A model package implements `IWordSenseModel` (model ID, SHA-256 of the model file, hidden size, encoding) and supplies
the weights table trained for it with the builder; the classifier constructor checks that they match
(`WordSenseModelMismatchException`). The MiniLM package is the only one so far.

# Model Files and Mirrors

`MiniLMWordSense.CreateClassifierAsync` needs two files: `model_int8.onnx` (23 MB) and `vocab.txt`. If
`MiniLMWordSenseOptions.ModelDirectory` is set, the files there are verified against their pinned SHA-256 and
used; nothing is downloaded. Otherwise they are stored in a per-user directory
(`LocalApplicationData/DemaConsulting/WordSense/models/all-MiniLM-L6-v2-int8/`) and downloaded when missing, from
these sources in order:

1. **Your mirror**, when configured: `{mirror}/all-MiniLM-L6-v2-int8/{file}`
2. **Hugging Face**, for files that are identical to the upstream files (only `vocab.txt`)
3. **GitHub LFS**:
   `https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/all-MiniLM-L6-v2-int8/{file}`

The folder name `all-MiniLM-L6-v2-int8` is the model ID; each model has its own folder in the repository (`model/`),
on a mirror and in the per-user store. Every file must match its pinned SHA-256 digest; on a request failure, timeout,
block or digest mismatch the next source is tried. If all fail, `WordSenseModelDownloadException.Failures` lists each
attempt. The weights table trained for the model is embedded in the MiniLM package, so only the two files are
downloaded.

To download (or verify) the model without creating a classifier, with progress and your own settings:

<!-- sample:download -->
```csharp
var downloaderOptions = new WordSenseModelDownloaderOptions
{
    StoreRoot = Path.Combine(Path.GetTempPath(), "wordsense-models"), // default: the per-user directory
    RequestTimeout = TimeSpan.FromMinutes(2),                          // per request; default 5 minutes
    Progress = new Progress<WordSenseModelDownloadProgress>(
        p => Console.WriteLine($"{p.FileName} from {p.Source}: {p.BytesTransferred} bytes")),
};

WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync(downloaderOptions);

// Offline use: copy the two verified files to a machine without internet access and point ModelDirectory at them
using var classifier = await MiniLMWordSense.CreateClassifierAsync(
    new MiniLMWordSenseOptions { ModelDirectory = location.Directory });
```

## Configuring a Mirror

Configure a mirror with environment variables:

| Variable                        | Meaning                                                      |
|---------------------------------|--------------------------------------------------------------|
| `WORDSENSE_MIRROR_URL`          | Absolute http(s) base URL of the mirror                      |
| `WORDSENSE_MIRROR_USER`         | Basic authentication user (supply it together with password) |
| `WORDSENSE_MIRROR_PASSWORD`     | Basic authentication password                                |
| `WORDSENSE_MIRROR_BEARER_TOKEN` | Bearer token for the mirror (instead of user and password)   |

or in code, which takes precedence over the environment:

<!-- sample:mirror -->
```csharp
var options = new MiniLMWordSenseOptions
{
    Downloader = new WordSenseModelDownloaderOptions
    {
        Mirror = new DownloadMirror(new Uri("https://models.example.com/wordsense"), bearerToken: "token"),
    },
};
```

Credentials require https (except for loopback hosts) and are sent only to the mirror. The mirror must serve the two
files, byte-identical to the pinned ones, under `{base}/all-MiniLM-L6-v2-int8/model_int8.onnx` and
`{base}/all-MiniLM-L6-v2-int8/vocab.txt`: one folder per model ID below the base URL. An invalid mirror configuration
in the environment fails fast with an `ArgumentException`.

## Handling Failures

<!-- sample:errors -->
```csharp
try
{
    using var classifier = await MiniLMWordSense.CreateClassifierAsync();
}
catch (WordSenseModelDownloadException ex)
{
    foreach (var failure in ex.Failures)
    {
        await Console.Error.WriteLineAsync(failure); // one line per source that was tried
    }
}
```

# API Reference

Package `DemaConsulting.WordSense` (namespace `DemaConsulting.WordSense`):

| Type                                             | Purpose                                                   |
|--------------------------------------------------|-----------------------------------------------------------|
| `IWordSenseClassifier`                           | `ClassifyAsync` overloads; lemma and candidate lookup     |
| `WordSenseClassifier`                            | Implementation; `Create` (lexicon only)                   |
| `WordSenseClassifierOptions`                     | Abstention scale and dictionary (`UseLexicon`)            |
| `WordAnalysis`, `WordDetails`, `WordSenseToken`  | Result of a word, best answer, text tile                  |
| `AnswerBasis`                                    | Where an answer comes from: context, dictionary, none     |
| `PartOfSpeech`, `Pronunciation`                  | Part of speech; pronunciation meaning and respelling      |
| `IWordSenseModel`                                | Encoder model abstraction (ID, SHA-256, `EncodeAsync`)    |
| `WordSenseTable`, `WordSenseWeightsTable`        | The embedded lexicon table and a model's weights table    |

Package `DemaConsulting.WordSense.MiniLM` (namespace `DemaConsulting.WordSense.MiniLM`):

| Type                                             | Purpose                                                   |
|--------------------------------------------------|-----------------------------------------------------------|
| `MiniLMWordSense`                                | `CreateClassifierAsync`; `LoadWeights`; `ModelId`         |
| `MiniLMWordSenseOptions`                         | Model directory, downloader, threads, window              |
| `MiniLMWordSenseModel`                           | ONNX Runtime encoder, the `IWordSenseModel` for MiniLM    |
| `WordSenseModelDownloader`                       | `EnsureModelAsync`: verify or download the model files    |
| `WordSenseModelDownloaderOptions`                | Mirror, base URIs, store root, timeout, progress          |
| `WordSenseModelLocation`                         | A verified model directory; `FromDirectory`               |
| `DownloadMirror`                                 | Mirror base URL and credentials; `FromEnvironment`        |

The two `ClassifyAsync` members of `IWordSenseClassifier` are asynchronous, and the results are parallel to their input:

| Member                                 | Result                                                              |
|----------------------------------------|---------------------------------------------------------------------|
| `ClassifyAsync(IReadOnlyList<string>)` | `WordAnalysis[]`: the full result of each word of a tokenized text  |
| `ClassifyAsync(string)`                | `IReadOnlyList<WordSenseToken>`: the raw text split into tiles      |

`IWordSenseClassifier` also has the synchronous `ContextLemmas` (the lemmas with a trained classifier) and
`GetCandidates(lemma)` (the possible answers of any lemma, empty when it is unknown).

`WordAnalysis` has `Word`, `Details` (`WordDetails?`), `Margin`, `Abstained`, `RunnerUp`, `RunnerUpMargin`, `Basis`
(`AnswerBasis`) and `Candidates` (never `null`).
`WordDetails` has `Lemma`, `PartOfSpeech` and `Pronunciation` (`Pronunciation?`), and `Pronunciation` has `Meaning` and
`Respelling` (which may be `null`).

Every method accepts a `CancellationToken`. One classifier may be shared by many threads and tasks, and its methods may
be called concurrently. It is `IDisposable` (it owns the model, for MiniLM the ONNX session): dispose it once, after
all calls have
finished. Exceptions:
`WordSenseModelDownloadException` (no source delivered a verified file), `WordSenseModelMismatchException` (table and
model differ) and `WordSenseTableFormatException` (damaged table); `WordSenseModelLocation.FromDirectory` throws
`FileNotFoundException` or `InvalidDataException` for a missing or modified model file.

# Accuracy and Performance

The figures below come from the builder reports in `docs/build_notes/` (the embedded table has 744 trained lemmas and
1,521 senses and a dictionary section of over 140,000 words; the lexicon table is format v6 and the weights table
format v1). Read them with their caveats. The accuracy is unchanged by the split into two packages.

**Dictionary answers.** The dictionary was evaluated against Universal Dependencies English EWT (train, dev and test
pooled; `docs/build_notes/lexicon-evaluation.md`). A word with one part of speech is correct 97.0 % to 99.9 % of the
time depending on the class: noun 97.61 %, verb 97.79 %, adjective 97.26 %, adverb 97.01 %, pronoun 99.89 % and
preposition 99.86 %. For a word with several parts of speech the right one is in `Candidates` 97.32 % of the time
(96.79 % for two candidates). The plural rule is correct 98.98 % of the time and the `-ly` rule 100 % (57 tokens, thin
evidence). Of the 16,622 sentences, 51.8 % need no encoder call. These figures use text from one treebank, so
expect somewhat different numbers on other kinds of text; a parts-of-speech class or rule that scored below 95 % was
not enabled (conjunctions and interjections only appear as candidates; `-ed` scored 94.69 % and is off).

**What the corpus is.** The training and held-out sentences have three kinds of origin: sentences written by a large
language model for the original 34 seed lemmas and the heteronym lemmas of the first batches, real sentences from
openly licensed sources (Tatoeba, Project Gutenberg, Universal Dependencies English EWT, Wikipedia), and, for senses
that real text could not fill, sentences authored by the project. The 329 common noun/verb lemmas added later are
based on real text (plus a few project-supplied sentences). Every sentence has a provenance record.

**Held-out accuracy.** On the 12,789 held-out rows (never used for training) the best answer is correct 96.91 % of the
time, and 98.73 % of the answered rows are correct (90.74 % answered). For noun versus verb (11,963 held-out rows whose
correct answer is exactly one of the two), the part of speech is correct 97.52 % of the time, and 99.22 % of the
answered rows are correct (91.60 % answered). Many of these rows were written by the project, so this is the
optimistic number.

**Real text.** On 689 real sentences of 39 lemmas that were judged blind and never used for training, the best answer
is correct 92.60 % of the time (96.40 % of the answered rows, 84.76 % answered), and the part of speech is correct
97.22 % of the time, against 97.71 % on the project-written held-out sentences of the same lemmas. Expect real-world
accuracy closer to this figure than to the held-out figure, and abstention on roughly one word in six.

**Noun/verb lemmas.** The holdout of the 329 noun/verb lemmas is real text taken from whole source groups, so it
shares no source with the training sentences. A lemma enters the table only when it reaches at least 90 % best-sense
accuracy on that holdout, 80 % answered accuracy per sense and 70 % coverage (the gate). Their holdout figures are
98.26 % best sense and 99.34 % answered accuracy at 91.61 % coverage. Because the same holdout selects the lemmas, these
figures are somewhat optimistic; a blind audit of 400 rows of these lemmas agreed with the label in 99.25 % of the
rows, but no separate real-text evaluation set exists for them, since no source-disjoint sentences were left over.

**Rare senses.** Senses that real text could not supply have sentences authored by the project (two independent
authors, origin `authored`); these have fewer and less varied examples. The `learning-curve` and `error-diagnosis`
reports in `docs/build_notes/learning-curve-and-diagnosis.md` list the weakest lemmas and why (coverage gaps,
overlapping senses or suspect labels).

**Performance.** A typical sentence takes a few milliseconds. These are indicative measurements from a development
workstation with the `WordSense_Benchmark_ClassifyLatency` test, which classifies one sentence with four ambiguous
words: a median of about 5.6 ms with one thread and about 2.5 ms with four threads. They are not derived from the
builder reports and will differ on other hardware; run the `Benchmark` test category (for example
`dotnet test --filter-trait "Category=Benchmark"`) to measure your own.

# Limitations

- **Dictionary words are not decided by context.** A word in the dictionary that has several parts of speech and no
  trained classifier ("will", "so", "yesterday") gives its candidates only: `Details` is `null`, `Abstained` is `true`.
  Only the 744 trained lemmas (see the appendix) are decided from the sentence. WordSense is not a general
  word-sense disambiguator.
- **No inflection of dictionary words beyond plural and `-ly`.** Plural `-s`, `-es`, `-ies` and adverb `-ly` are
  recognized. `-ed`, `-ing`, `-er`/`-est` and the possessive are not, because they did not reach the precision gate:
  such a form is answered only when the dictionary itself lists it as spelled, and is otherwise unknown. The trained
  lemmas have their own form lists.
- **Determiners and some words are unknown.** Determiners (the, a, an, his, ...), numbers and words that are not in
  the dictionary are unknown, and so are proper nouns: a capitalized word inside a sentence is skipped by the case
  policy (the corrections for the pronoun "I" are an exception).
- **Parts of speech.** Dictionary answers can be a noun, verb, adjective, adverb, pronoun or preposition; a word whose
  only part of speech is a conjunction or an interjection is not in the dictionary (those classes did not reach the
  precision gate and only appear as candidates of a word with another part of speech). Context answers are a noun, a
  verb, an adjective or an adverb.
- **Pronunciation for heteronyms only.** A lemma that is not a heteronym has no `Pronunciation`, and the library has no
  phonetic data of its own: the `Respelling` is a plain hint, and the consumer maps (`Lemma`, `Meaning`) to its own
  pronunciation.
- **Collision forms are excluded.** A form that also belongs to another lemma cannot be attributed safely, so these
  forms (for example `saw`, `left`, `found`, `fell`, `rose`, `felt`, `bit` and `ground`) are not targets of the
  noun/verb lemmas. `lie` is excluded because its past tense `lay` is a lemma of its own, which is classified only as
  noun versus verb. The form maps of the corpus decide what is recognized.
- **Abstention.** The classifier abstains when the evidence is weak; about one answer in ten is withheld on held-out
  text and more on real text. `Margin` and `RunnerUp` still give the best guess when you need it anyway.
- **English only**, and the context is a window of at most `MaxSequenceLength` tokens around the target word.
- **The model is a separate download** (MiniLM package, 23 MB, on first use) and ONNX Runtime needs native libraries
  for the target platform.

# Licensing and Provenance

WordSense is licensed under the MIT License. The `all-MiniLM-L6-v2` model (Apache License 2.0) and ONNX Runtime (MIT)
are used under their own licences.

The embedded dictionary is derived from Moby Part-of-Speech II by Grady Ward
(<https://www.gutenberg.org/ebooks/3203>), which the author dedicated to the public domain; no attribution is
required, but it is given here with thanks. Only a filtered list (single lowercase words, determiners removed, a few
corrections and held-back classes) is committed, in `corpus/lexicon/`, with its source, checksum and filter rules in
`corpus/lexicon/PROVENANCE.md`. The accuracy figures are in `docs/build_notes/lexicon-evaluation.md`.

The training corpus is not shipped in the NuGet package; only the derived classifier vectors are embedded in the
assembly. Every corpus sentence has a row in `corpus/provenance/*.tsv` recording its source, licence, URL and
contributor. The licences in use are `CC0 1.0`, `Public domain`, `CC BY 2.0 FR` (Tatoeba, with the contributor
recorded), `CC BY-SA 4.0` (Wikipedia and Universal Dependencies English EWT sentences), `MIT (project-authored)` and,
as a last resort for a handful of senses, `web-excerpt`. Sentences under `CC BY-SA 4.0` keep that licence: if you
redistribute the corpus (not the package), those rows must be shared under the same terms with attribution. The
shipped vectors are statistical summaries of many sentences and do not contain the sentences; this is the project's
reading and not legal advice. `docs/build_notes/model-provenance.md` describes how the model files were produced.

# Extending the Corpus

The table is generated by the non-shipped builder `tools/WordSense.Builder` from the files in `corpus/`; the
format of the files is described in `corpus/README.md`. All commands run from the repository root with
`dotnet run --project tools/WordSense.Builder -c Release -- <command> --name value`:

- `build`: trains the weights of one model for every lemma and writes the lexicon table and the model's weights table,
  with the dictionary from `corpus/lexicon/` (`--corpus`, `--model`, `--model-id`, `--output`, `--weights`, `--report`)
- `import-moby`: filters Moby Part-of-Speech II into `corpus/lexicon/lexicon.tsv`; `lexicon-eval` scores the
  dictionary against Universal Dependencies English EWT
- `validate-provenance` and `validate-pos`: check the provenance rows and the part-of-speech rules of the inventory
- `generate-appendix`: regenerates the senses appendix of this guide (`--corpus`, `--output`)
- `real-eval`: scores the table on the real-text evaluation set
- `learning-curve`, `error-diagnosis` and `weak-lemmas`: accuracy against the number of anchors, the errors
  and the weak lemmas
- `nounverb-import` and `nounverb-gate`: import noun/verb lemmas and apply the quality gate
- `import-batches`, `real-import` and `real-authored-import`: import batches of sentences (see
  `corpus/README.md`)

The `audit-*`, `apply-relabel`, `real-gap*` and `real-judge-*` commands belong to the blind labeling process and are
documented in the build notes. The `tools/real-text` harvester collects candidate real sentences.

## Corpus Format

- `anchors/*.tsv` (training) and `holdout/*.tsv` (evaluation) have three tab-separated columns and no header:
  `lemma`, `label`, `sentence`. The label names one candidate of the lemma in `senses/*.tsv`, and the sentence marks
  the target by one pair of braces: `The {record} for the fastest mile was set in 1999.`
- `senses/*.tsv` is the inventory of the candidates, with the header `lemma`, `label`, `pos`, `meaning` and
  `respelling`: the part of speech (`noun`, `verb`, `adj` or `adv`) of the label and, for a heteronym only, its
  pronunciation `meaning` id and plain `respelling`.
- `forms/speech.tsv` maps inflected forms to lemmas (`form`, `lemma`, `labels`).
- `provenance/*.tsv` has one row per sentence: `id` (first 12 hex characters of the SHA-256 of `lemma|sentence`),
  `lemma`, `sense`, `split`, `source`, `licence`, `source_key`, `url`, `contributor` and `origin`.

## Adding a Word

1. Write the candidates of the lemma into a `senses/*.tsv` file with their part of speech and, for a heteronym, the
   pronunciation meaning and respelling of each.
2. Add at least four anchor sentences for each label to a new `anchors/my-batch.tsv`, with holdout sentences that are
   different (and, ideally, from different sources) in `holdout/my-batch.tsv`. The braced target may be any form.
3. Add every braced form to `forms/speech.tsv` with its lemma and the labels it can take, unless the form is the lemma.
4. Add a provenance row for every sentence to `provenance/my-batch.tsv`: source, licence from the list above,
   a URL for web sources and the origin (`real`, `authored` or `user-supplied`).
5. Run `validate-provenance`, then `build` to write the lexicon table
   `src/DemaConsulting.WordSense/Resources/wordsense-lexicon.bin` and the weights table of the model,
   `src/DemaConsulting.WordSense.MiniLM/Resources/weights.<model-id>.<onnx-sha256>.bin` (the second command is one
   line; `\` marks the continuation):

   ```text
   dotnet run --project tools/WordSense.Builder -c Release -- validate-provenance --corpus corpus
   dotnet run --project tools/WordSense.Builder -c Release -- build --corpus corpus --model model \
     --model-id all-MiniLM-L6-v2-int8 --output src/DemaConsulting.WordSense/Resources/wordsense-lexicon.bin \
     --weights src/DemaConsulting.WordSense.MiniLM/Resources/weights.all-MiniLM-L6-v2-int8.<onnx-sha256>.bin \
     --report build-report.md
   ```

6. Check the per-lemma holdout accuracy in the report; a lemma with fewer than two anchor labels or fewer than four
   anchors for a label is excluded. Regenerate the appendix with `generate-appendix`, run `pwsh ./build.ps1` and
   commit the corpus files, the tables and the appendix together.

# Troubleshooting

## The Model Cannot Be Downloaded

`WordSenseModelDownloadException` on first use means that no source delivered a verified file; read `Failures`
for the reason of each source. Offline or behind a firewall, configure a mirror, or copy the two model files to a
directory and set `ModelDirectory`.

## Timeouts or a Slow First Run

The model is 23 MB. Raise `WordSenseModelDownloaderOptions.RequestTimeout` (default 5 minutes per request), check
the proxy settings of the host, or use a mirror close to it.

## The Model File Does Not Match Its SHA-256

`InvalidDataException` from `ModelDirectory` (or `WordSenseModelLocation.FromDirectory`) means that the file is
stale, truncated, modified or a Git LFS pointer. Delete it and let the downloader fetch it again.

## Git LFS Pointer Instead of the Model

In a clone made without Git LFS, `model/all-MiniLM-L6-v2-int8/model_int8.onnx` is a short text file that starts with
`version https://git-lfs`, and the tests that need the model skip with "missing or is a Git LFS pointer". Run
`git lfs install` and `git lfs pull`. Package users are not affected: the downloader fetches the real file.

## The Weights and the Model Differ

`WordSenseModelMismatchException` means the weights table was trained with another model than the one you supplied
(a custom `IWordSenseModel`), or with another lexicon. Use the pinned model files of the MiniLM package.

## The Mirror Configuration Is Rejected

An `ArgumentException` about the mirror means that `WORDSENSE_MIRROR_URL` is not an absolute http(s) URL, that
only one of user and password is set, that both a user and a bearer token are given, or that credentials are used
with plain http for a host that is not loopback.

## A Word Returns Nothing

`Details` is `null` for a word that is unknown, has no single answer, or was abstained. `Basis` and `Abstained` tell
them apart: `None` means unknown (the word is not in the dictionary, is a determiner, number or proper noun, or is a
capitalized word inside a sentence); `Dictionary` with `Abstained` means a word with several parts of speech and no
trained classifier, so read `Candidates`; `Context` with `Abstained` means the classifier was not sure, so look at
`Margin` and `RunnerUp`, and lower `AbstainMarginScale` to answer more often. With `UseLexicon` set to `false` only the
trained lemmas (`ContextLemmas`) are known.

## Results Differ from the Documentation

Only the exact pinned model files produce the documented output; verify them with
`WordSenseModelLocation.FromDirectory`.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
