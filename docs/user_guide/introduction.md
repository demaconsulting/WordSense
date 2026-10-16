# Introduction

## Purpose

This document is the user guide for WordSense, a .NET library that tells, for English words in context and offline,
the part of speech and the pronunciation. It tells, for example, whether "record" is a noun or a verb, and
whether "bow" is said like "bough" (a bend) or like "beau" (a weapon).

## Scope

This user guide covers:

- Installation, requirements and a quick start
- The classification API: words, text, confidence, pronunciation and listing the supported words
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
`test/DemaConsulting.WordSense.Tests/UserGuideSamples.cs`. The samples assume `ImplicitUsings` and
`using DemaConsulting.WordSense;`.

# Glossary

- **Lemma**: the dictionary base form of a word, for example "record" for "records".
- **Heteronym**: a word that is spelled the same but pronounced differently depending on its use, for example
  "record" (the noun is said REK-urd, the verb rih-KORD) or "bow".
- **Part of speech**: the grammatical role of a word in its sentence, such as noun, verb, adjective or adverb.
- **Meaning**: a short id, for example `bend` or `weapon` for "bow", that names which of the pronunciations of a
  heteronym is meant; it is only given for heteronyms.
- **Respelling**: a plain-letter hint of how to say the word, for example `boh`; it is a guide for a person or a
  speech engine and not a phonetic transcription.
- **Abstain**: to give no answer for a word the library knows but is not sure about in its context.
- **Margin**: the confidence of an answer, the score of the best candidate minus the score of the runner-up; the
  higher it is, the surer the library is (it is a score difference, not a probability).

# Installation

```bash
dotnet add package DemaConsulting.WordSense
```

The package id is `DemaConsulting.WordSense` and the package depends only on Microsoft.ML.OnnxRuntime.

## Requirements

- .NET 8, 9 or 10 (the package targets `net8.0`, `net9.0` and `net10.0`) on Windows, Linux or macOS.
- About 23 MB of disk space in a per-user directory for the encoder model, which is downloaded on first use. The
  NuGet package contains the classifier table but not the model. A machine that cannot reach the internet needs a
  mirror or a pre-seeded model directory (see "Model Files and Mirrors").
- Contributors who clone the repository also need [Git LFS](https://git-lfs.com): the files in `model/` are stored
  in Git LFS (`git lfs install` once, then `git lfs pull`). See "Troubleshooting".

# Quick Start

<!-- sample:quick-start -->
```csharp
// Downloads the model on first use (mirror, Hugging Face, GitHub LFS), verifies its SHA-256 and reuses it later
using var classifier = await WordSenseClassifier.CreateAsync();

string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "the", "new", "album"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words);

for (var i = 0; i < words.Length; i++)
{
    if (analyses[i].Details is { } details)
    {
        Console.WriteLine($"{words[i]}: {details.PartOfSpeech} {details.Pronunciation?.Meaning}");
    }
}
```

The input is a list of words that you have tokenized yourself, in order and including punctuation tokens; they are
read as one running text. `ClassifyAsync` returns a non-null array that is parallel to the input: element `i` is the
`WordAnalysis` of `words[i]`, whatever the word is. Inflected forms are recognized (`presents` is a form of
`present`).

A caller gets two facts per word:

- The **part of speech** of the word in its sentence (`WordDetails.PartOfSpeech`), together with the `Lemma`.
- For a **heteronym**, the **pronunciation** (`WordDetails.Pronunciation`): a `Meaning` id and an optional
  `Respelling`. A word that is not a heteronym has a `null` `Pronunciation`; most noun/verb lemmas are not
  heteronyms.

`WordAnalysis.Details` is `null` when the word is **unknown** (it is not in the table, see the appendix) or when the
classifier **abstained**. `WordAnalysis.Abstained` tells the two cases apart: it is `true` only for a word the library
knows but is not sure about. The `PartOfSpeech` enumeration defines all eight traditional parts of speech, but the
table only produces `Noun`, `Verb`, `Adjective` and `Adverb`.

The
<!-- markdownlint-disable-next-line MD051 -->
[appendix](#appendix-supported-senses) is the catalog of the supported lemmas, their parts of speech and their
pronunciation meanings.

# Usage

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
each token has a `WordAnalysis`, and `Details` is set only for answered words.

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
with two candidates) and `Abstained`. The margin and the runner-up are also filled in for a word the classifier
abstained from, so you can apply your own policy; for an unknown word `Details` and `RunnerUp` are `null` and both
margins are 0.

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

## Listing the Supported Words

<!-- sample:candidates -->
```csharp
Console.WriteLine($"{classifier.KnownLemmas.Count} lemmas");

foreach (var candidate in classifier.GetCandidates("record"))
{
    Console.WriteLine($"{candidate.Lemma}: {candidate.PartOfSpeech} pronunciation={candidate.Pronunciation?.Meaning}");
}
```

`GetCandidates` returns an empty list for an unknown lemma. The appendix lists the same data for every lemma.

## Abstention

For each lemma the table stores a margin threshold. When the best candidate beats the runner-up by less than that
threshold, the classifier abstains: `Details` is `null` and `Abstained` is `true`, while `Margin` and `RunnerUp` still
report the best guess. Abstaining trades coverage for precision (about 91 % of the held-out rows are answered, see
"Accuracy and Performance"). Scale all thresholds with `WordSenseClassifierOptions.AbstainMarginScale`:

<!-- sample:tuning -->
```csharp
var options = new WordSenseClassifierOptions
{
    AbstainMarginScale = 0.5, // below 1 answers more often, above 1 abstains more often; 0 never abstains
    IntraOpThreads = 2,       // ONNX Runtime threads; 0 selects the default
    MaxSequenceLength = 128,  // model window, 8 to 512 tokens; longer texts use a window around each target
};

using var tuned = await WordSenseClassifier.CreateAsync(options);
```

# Model Files and Mirrors

`WordSenseClassifier.CreateAsync` needs two files: `model_int8.onnx` (23 MB) and `vocab.txt`. If
`WordSenseClassifierOptions.ModelDirectory` is set, the files there are verified against their pinned SHA-256 and
used; nothing is downloaded. Otherwise they are stored in a per-user directory
(`LocalApplicationData/DemaConsulting/WordSense/models/...`) and downloaded when missing, from these sources in
order:

1. **Your mirror**, when configured: `{mirror}/all-MiniLM-L6-v2/{file}`
2. **Hugging Face**, for files that are identical to the upstream files (only `vocab.txt`)
3. **GitHub LFS**: `https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/{file}`

Every file must match its pinned SHA-256 digest; on a request failure, timeout, block or digest mismatch the next
source is tried. If all fail, `WordSenseModelDownloadException.Failures` lists each attempt.

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
using var classifier = await WordSenseClassifier.CreateAsync(
    new WordSenseClassifierOptions { ModelDirectory = location.Directory });
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
var options = new WordSenseClassifierOptions
{
    Downloader = new WordSenseModelDownloaderOptions
    {
        Mirror = new DownloadMirror(new Uri("https://models.example.com/wordsense"), bearerToken: "token"),
    },
};
```

Credentials require https (except for loopback hosts) and are sent only to the mirror. The mirror must serve the two
files, byte-identical to the pinned ones, under `{base}/all-MiniLM-L6-v2/model_int8.onnx` and
`{base}/all-MiniLM-L6-v2/vocab.txt`. An invalid mirror configuration in the environment fails fast with an
`ArgumentException`.

## Handling Failures

<!-- sample:errors -->
```csharp
try
{
    using var classifier = await WordSenseClassifier.CreateAsync();
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

| Type                                             | Purpose                                                   |
|--------------------------------------------------|-----------------------------------------------------------|
| `IWordSenseClassifier`                           | Word-list and text `ClassifyAsync`                        |
| `WordSenseClassifier`                            | Implementation; `CreateAsync`, `GetCandidates`            |
| `WordSenseClassifierOptions`                     | Model directory, downloader, threads, window, abstention  |
| `WordAnalysis`, `WordDetails`, `WordSenseToken`  | Result of a word, best answer, text tile                  |
| `PartOfSpeech`, `Pronunciation`                  | Part of speech; pronunciation meaning and respelling      |
| `WordSenseModelDownloader`                       | `EnsureModelAsync`: verify or download the model files    |
| `WordSenseModelDownloaderOptions`                | Mirror, base URIs, store root, timeout, progress          |
| `WordSenseModelLocation`                         | A verified model directory; `FromDirectory`               |
| `DownloadMirror`                                 | Mirror base URL and credentials; `FromEnvironment`        |
| `IWordSenseEncoder`, `OnnxWordSenseEncoder`      | Encoder abstraction and ONNX Runtime encoder              |
| `WordSenseTable`                                 | The embedded classifier table (`LoadEmbedded`)            |

The members of `IWordSenseClassifier` are asynchronous, and the results are parallel to their input:

| Member                                 | Result                                                              |
|----------------------------------------|---------------------------------------------------------------------|
| `ClassifyAsync(IReadOnlyList<string>)` | `WordAnalysis[]`: the full result of each word of a tokenized text  |
| `ClassifyAsync(string)`                | `IReadOnlyList<WordSenseToken>`: the raw text split into tiles      |

`WordSenseClassifier` also has `KnownLemmas` (the supported lemmas) and `GetCandidates(lemma)` (the possible answers
of one lemma, empty when it is unknown).

`WordAnalysis` has `Word`, `Details` (`WordDetails?`), `Margin`, `Abstained`, `RunnerUp` and `RunnerUpMargin`.
`WordDetails` has `Lemma`, `PartOfSpeech` and `Pronunciation` (`Pronunciation?`), and `Pronunciation` has `Meaning` and
`Respelling` (which may be `null`).

Every method accepts a `CancellationToken`. One classifier may be shared by many threads and tasks, and its methods may
be called concurrently. It is `IDisposable` (it owns the ONNX session): dispose it once, after all calls have
finished. Exceptions:
`WordSenseModelDownloadException` (no source delivered a verified file), `WordSenseModelMismatchException` (table and
model differ) and `WordSenseTableFormatException` (damaged table); `WordSenseModelLocation.FromDirectory` throws
`FileNotFoundException` or `InvalidDataException` for a missing or modified model file.

# Accuracy and Performance

The figures below come from the builder reports in `docs/build_notes/` (the embedded table has 744 lemmas and 1,521
senses, format v3). Read them with their caveats.

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

- **Only trained words are answered.** A word that is not in the table (see the appendix) is unknown: it has no
  `Details`. WordSense is not a general word-sense disambiguator.
- **Four parts of speech.** The answer is a noun, a verb, an adjective or an adverb; pronouns, prepositions,
  conjunctions and interjections are never answered, although `PartOfSpeech` defines them.
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
- **The model is a separate download** (23 MB, on first use) and ONNX Runtime needs native libraries for the target
  platform.

# Licensing and Provenance

WordSense is licensed under the MIT License. The `all-MiniLM-L6-v2` model (Apache License 2.0) and ONNX Runtime (MIT)
are used under their own licences.

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

- `build`: trains every lemma and writes the table (`--corpus`, `--model`, `--output`, `--report`)
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
  `lemma`, `label`, `split`, `source`, `licence`, `source_key`, `url`, `contributor` and `origin`.

## Adding a Word

1. Write the candidates of the lemma into a `senses/*.tsv` file with their part of speech and, for a heteronym, the
   pronunciation meaning and respelling of each.
2. Add at least four anchor sentences for each label to a new `anchors/my-batch.tsv`, with holdout sentences that are
   different (and, ideally, from different sources) in `holdout/my-batch.tsv`. The braced target may be any form.
3. Add every braced form to `forms/speech.tsv` with its lemma and the labels it can take, unless the form is the lemma.
4. Add a provenance row for every sentence to `provenance/my-batch.tsv`: source, licence from the list above,
   a URL for web sources and the origin (`real`, `authored` or `user-supplied`).
5. Run `validate-provenance`, then `build` to write `src/DemaConsulting.WordSense/Resources/wordsense-table.bin`
   (the second command is one line; `\` marks the continuation):

   ```text
   dotnet run --project tools/WordSense.Builder -c Release -- validate-provenance --corpus corpus
   dotnet run --project tools/WordSense.Builder -c Release -- build --corpus corpus --model model \
     --output src/DemaConsulting.WordSense/Resources/wordsense-table.bin --report build-report.md
   ```

6. Check the per-lemma holdout accuracy in the report; a lemma with fewer than two anchor labels or fewer than four
   anchors for a label is excluded. Regenerate the appendix with `generate-appendix`, run `pwsh ./build.ps1` and
   commit the corpus files, the table and the appendix together.

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

In a clone made without Git LFS, `model/model_int8.onnx` is a short text file that starts with
`version https://git-lfs`, and the tests that need the model skip with "missing or is a Git LFS pointer". Run
`git lfs install` and `git lfs pull`. Package users are not affected: the downloader fetches the real file.

## The Table and the Model Differ

`WordSenseModelMismatchException` means the table was trained with another model than the encoder you supplied
(with a custom `IWordSenseEncoder`). Use the pinned model files.

## The Mirror Configuration Is Rejected

An `ArgumentException` about the mirror means that `WORDSENSE_MIRROR_URL` is not an absolute http(s) URL, that
only one of user and password is set, that both a user and a bearer token are given, or that credentials are used
with plain http for a host that is not loopback.

## A Word Returns Nothing

`Details` is `null` for a word that is unknown or abstained. The word is unknown when it is not in the table (check the
appendix or `KnownLemmas`); `Abstained` is `true` when the classifier was not sure. Look at `Margin` and `RunnerUp`, and
lower `AbstainMarginScale` to answer more often.

## Results Differ from the Documentation

Only the exact pinned model files produce the documented output; verify them with
`WordSenseModelLocation.FromDirectory`.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
