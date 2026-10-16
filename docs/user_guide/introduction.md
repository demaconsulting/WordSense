# Introduction

## Purpose

This document is the user guide for WordSense, a .NET library that classifies the sense of ambiguous
English words in context, offline. It tells, for example, whether "record" is a noun or a verb, or whether
"bow" is a bend or a weapon.

## Scope

This user guide covers:

- Installation, requirements and a quick start
- The classification API: words, text, confidence details, tag queries
- Model download, mirrors and offline use
- Accuracy, limitations and abstention
- Licensing and provenance of the data
- Extending the training corpus
- Troubleshooting
<!-- markdownlint-disable MD051 -->
<!-- The appendix is a separate file that pandoc concatenates, so its heading anchor only exists in the PDF. -->
- [Appendix: Supported Senses and Tags](#appendix-supported-senses-and-tags): how tags and groups work and the
  full list of supported lemmas (generated from the corpus)
<!-- markdownlint-enable MD051 -->

The code samples in this guide are compiled by the test suite (`UserGuideSampleTests`): each C# block is preceded by
a `sample` marker comment in the source of this guide and must equal the code in
`test/DemaConsulting.WordSense.Tests/UserGuideSamples.cs`. The samples assume `ImplicitUsings` and
`using DemaConsulting.WordSense;`.

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
WordSense?[] senses = await classifier.ClassifyAsync(words);

for (var i = 0; i < words.Length; i++)
{
    if (senses[i] is { } sense)
    {
        Console.WriteLine($"{words[i]}: {sense.Sense} [{string.Join(", ", sense.Tags)}]");
    }
}
```

The input is a list of words that you have tokenized yourself, in order and including punctuation tokens; they are
read as one running text. The result is parallel to the input: element `i` is the `WordSense` of `words[i]`, or
`null`. `null` means the word is not one of the supported ambiguous words (see the appendix), or that the classifier
abstained because the evidence was weak. Inflected forms are recognized (`presents` is a form of `present`).

A `WordSense` is an immutable record with `Lemma`, `Sense`, `Group`, `Tags` and `HasTag(tag)`. `Tags` is a frozen
set of true parts of speech (`noun`, `verb`, `adj`, `adv`) and qualifiers (`past`, `present`, `comparative`,
`superlative`; `participle` is also a valid qualifier but no sense uses it yet); every sense has exactly one part of
speech. `Group` is the equivalence class of the sense: senses of
one lemma that share a group are interchangeable for the consumer because they share a pronunciation (the senses
`bend-noun` and `bend-verb` of `bow` share the group `bend`). It equals the sense label when the sense is not
grouped. The library has no pronunciation data; a consumer such as a speech synthesizer keys its own pronunciations
by (`Lemma`, `Group`). The same instance is returned for the same lemma and
sense. The
<!-- markdownlint-disable-next-line MD051 -->
[appendix](#appendix-supported-senses-and-tags) explains tags and groups and lists every supported lemma with its
senses.

# Usage

## Text Input

If you have raw text instead of tokens, pass the string:

<!-- sample:text -->
```csharp
IReadOnlyList<WordSenseToken> tokens = await classifier.ClassifyAsync("They will record the record.");

foreach (var token in tokens)
{
    Console.WriteLine($"{token.Start,3} {token.Text,-8} {token.Sense?.ToString() ?? "-"}");
}
```

The tokens tile the input: concatenating `Text` reproduces the string exactly, so results map back onto it (`Start`
and `Length` are UTF-16 offsets). Words, runs of whitespace and single punctuation characters are separate tokens;
`Sense` is set only for answered words.

## Confidence Details

<!-- sample:detail -->
```csharp
string[] words = ["They", "will", "record", "the", "record"];
WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(words);

foreach (var detail in details.OfType<WordSenseDetail>())
{
    Console.WriteLine(
        $"{detail.Sense} margin={detail.Margin:F2} runner-up={detail.RunnerUp} abstained={detail.Abstained}");
}
```

A `WordSenseDetail` holds the best `Sense`, its `Margin` (the score of the best sense minus the runner-up), the
`RunnerUp` sense, the `RunnerUpMargin` (runner-up minus the third best; 0 for a lemma with two senses) and
`Abstained`. The detail is also returned for words the classifier abstained from, so callers can apply their own
policy; it is `null` only for words that have no classifier.

## Tag Queries

<!-- sample:tags -->
```csharp
string[] words = ["They", "will", "record", "the", "record"];
string?[] pos = await classifier.ClassifyTagAsync(words, ["noun", "verb"]); // "noun", "verb" or null
bool?[] isVerb = await classifier.IsTagAsync(words, "verb");                // true, false or null

for (var i = 0; i < words.Length; i++)
{
    Console.WriteLine($"{words[i]}: pos={pos[i] ?? "-"} isVerb={isVerb[i]?.ToString() ?? "-"}");
}
```

A tag is scored by the best sense that carries it, so senses that agree on the tag do not weaken each other. The
result is `null` when the word is unknown, when none of the tags exists on the lemma (a tag is never guessed), or
when the margin is below the abstention threshold of the lemma.

## Listing the Supported Words

<!-- sample:senses -->
```csharp
Console.WriteLine($"{classifier.KnownLemmas.Count} lemmas");

foreach (var sense in classifier.GetSenses("record"))
{
    var tags = string.Join(",", sense.Tags.Order());
    Console.WriteLine($"{sense.Lemma}: {sense.Sense} (group {sense.Group}) tags={tags}");
}
```

`GetSenses` returns an empty list for an unknown lemma. The appendix lists the same data for every lemma.

## Abstention

For each lemma the table stores a margin threshold. When the best sense beats the runner-up by less than that
threshold, the classifier abstains: the word-list API returns `null`, the tag queries return `null`, and
`ClassifyDetailAsync` still reports the best guess with `Abstained` set to `true`. Abstaining trades coverage for
precision (about 91 % of the held-out rows are answered, see "Accuracy and Performance"). Scale all thresholds with
`WordSenseClassifierOptions.AbstainMarginScale`:

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
| `IWordSenseClassifier`                           | Word and text `ClassifyAsync`, detail and tag queries     |
| `WordSenseClassifier`                            | Implementation; `CreateAsync`, `KnownLemmas`, `GetSenses` |
| `WordSenseClassifierOptions`                     | Model directory, downloader, threads, window, abstention  |
| `WordSense`, `WordSenseDetail`, `WordSenseToken` | Immutable sense with tags, detail, text tile              |
| `WordSenseModelDownloader`                       | `EnsureModelAsync`: verify or download the model files    |
| `WordSenseModelDownloaderOptions`                | Mirror, base URIs, store root, timeout, progress          |
| `WordSenseModelLocation`                         | A verified model directory; `FromDirectory`               |
| `DownloadMirror`                                 | Mirror base URL and credentials; `FromEnvironment`        |
| `IWordSenseEncoder`, `OnnxWordSenseEncoder`      | Encoder abstraction and ONNX Runtime encoder              |
| `WordSenseTable`                                 | The embedded classifier table (`LoadEmbedded`)            |

The members of `IWordSenseClassifier`, all asynchronous and parallel to their input (`null` = unknown or unsure):

| Member                                      | Result                                                          |
|---------------------------------------------|-----------------------------------------------------------------|
| `ClassifyAsync(IReadOnlyList<string>)`      | `WordSense?[]`: the sense of each word of a tokenized text      |
| `ClassifyAsync(string)`                     | `IReadOnlyList<WordSenseToken>`: the raw text split into tiles  |
| `ClassifyDetailAsync(words)`                | `WordSenseDetail?[]`: best sense, margins, `Abstained`          |
| `ClassifyTagAsync(words, tags)`             | `string?[]`: the winning tag of the given tags                  |
| `IsTagAsync(words, tag)`                    | `bool?[]`: whether the word has the tag                         |
| `KnownLemmas`, `GetSenses(lemma)`           | The supported lemmas and the senses of one lemma                |

Every method accepts a `CancellationToken`; the classifier is `IDisposable` (it owns the ONNX session). Exceptions:
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

**Held-out accuracy.** On the 12,789 held-out rows (never used for training) the best sense is correct 96.91 % of the
time (97.08 % at group level), and 98.73 % of the answered rows are correct (90.74 % answered). For noun versus verb
(11,963 held-out rows whose correct sense is exactly one of the two), the best tag is correct 97.52 % of the time, and
99.22 % of the answered rows are correct (91.60 % answered). Many of these rows were written by the project, so this
is the optimistic number.

**Real text.** On 689 real sentences of 39 lemmas that were judged blind and never used for training, the best sense
is correct 92.60 % of the time (96.40 % of the answered rows, 84.76 % answered), against 97.71 % on the
project-written held-out sentences of the same lemmas. Expect real-world accuracy closer to this figure than to the
held-out figure, and abstention on roughly one word in six.

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

**Performance.** A typical sentence takes a few milliseconds (median 5.6 ms with one thread, 2.5 ms with four threads
on a development workstation; run the `Benchmark` test category to measure your hardware).

# Limitations

- **Only trained words are answered.** A word that is not in the table (see the appendix) yields no sense: `null` in
  the API and `-` in the examples. WordSense is not a general word-sense disambiguator.
- **One part of speech per sense.** A sense is either a noun, a verb, an adjective or an adverb sense, never a mix.
  Lemmas are split into several senses when the part of speech differs, and `Group` rejoins senses that
  share a pronunciation (the library itself has no pronunciation data).
- **Collision forms are excluded.** A form that also belongs to another lemma cannot be attributed safely, so these
  forms (for example `saw`, `left`, `found`, `fell`, `rose`, `felt`, `bit` and `ground`) are not targets of the
  noun/verb lemmas. `lie` is excluded because its past tense `lay` is a lemma of its own, which is classified only as
  noun versus verb. The form maps of the corpus decide what is recognized.
- **Abstention.** The classifier abstains when the evidence is weak; about one answer in ten is withheld on held-out
  text and more on real text. Use `ClassifyDetailAsync` when you need the best guess anyway.
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
- `validate-provenance` and `validate-pos`: check the provenance rows and the tag rules (one part of speech per
  sense)
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
  `lemma`, `sense`, `sentence`. The sentence marks the target by one pair of braces:
  `The {record} for the fastest mile was set in 1999.`
- `senses/*.tsv` is the sense inventory: `lemma`, `sense`, `tags` (`|`-separated) and an optional `group`.
- `forms/speech.tsv` maps inflected forms to lemmas (`form`, `lemma`, `labels`).
- `provenance/*.tsv` has one row per sentence: `id` (first 12 hex characters of the SHA-256 of `lemma|sentence`),
  `lemma`, `sense`, `split`, `source`, `licence`, `source_key`, `url`, `contributor` and `origin`.

## Adding a Word

1. Write the senses of the lemma into a `senses/*.tsv` file with true part-of-speech tags and a group when you split a
   sense.
2. Add at least four anchor sentences for each sense to a new `anchors/my-batch.tsv`, with holdout sentences that are
   different (and, ideally, from different sources) in `holdout/my-batch.tsv`. The braced target may be any form.
3. Add every braced form to `forms/speech.tsv` with its lemma and the senses it can take, unless the form is the lemma.
4. Add a provenance row for every sentence to `provenance/my-batch.tsv`: source, licence from the list above,
   a URL for web sources and the origin (`real`, `authored` or `user-supplied`).
5. Run `validate-provenance`, then `build` to write `src/DemaConsulting.WordSense/Resources/wordsense-table.bin`
   (the second command is one line; `\` marks the continuation):

   ```text
   dotnet run --project tools/WordSense.Builder -c Release -- validate-provenance --corpus corpus
   dotnet run --project tools/WordSense.Builder -c Release -- build --corpus corpus --model model \
     --output src/DemaConsulting.WordSense/Resources/wordsense-table.bin --report build-report.md
   ```

6. Check the per-lemma holdout accuracy in the report; a lemma with fewer than two anchor senses or fewer than four
   anchors for a sense is excluded. Regenerate the appendix with `generate-appendix`, run `pwsh ./build.ps1` and
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

The word is not in the table (check the appendix or `KnownLemmas`), or the classifier abstained. Use
`ClassifyDetailAsync` to see the margin, and lower `AbstainMarginScale` to answer more often.

## Results Differ from the Documentation

Only the exact pinned model files produce the documented output; verify them with
`WordSenseModelLocation.FromDirectory`.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
