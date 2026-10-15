# Introduction

## Purpose

This document is the user guide for WordSense, a .NET library that classifies the sense of ambiguous
English words in context.

## Scope

This user guide covers:

- Installation and first use
- Model download, mirrors and offline use
- API reference
- Accuracy, abstention and performance notes

# Installation

```bash
dotnet add package DemaConsulting.WordSense
```

The package targets net8.0, net9.0 and net10.0 and depends only on Microsoft.ML.OnnxRuntime.

# Usage

## Basic Usage

```csharp
using DemaConsulting.WordSense;

using var classifier = await WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions());
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

The result is parallel to the input: element `i` is the `WordSense` of `words[i]`, or `null`. `null` means the word is
not a known ambiguous word, or the classifier abstained because the evidence was weak. A `WordSense` is an immutable
record with `Lemma`, `Sense`, `Group`, `Tags` (a frozen set of true parts of speech and qualifiers such as `noun`,
`verb`, `adj`, `adv`, `past`) and `HasTag(tag)`; `Group` is the coarser group of the sense (the sense label unless
senses are grouped, so a consumer that does not care about a split reads `Group`); the same
instance is returned for the same (lemma, sense). A sense has exactly one part of speech.

## Text input

```csharp
IReadOnlyList<WordSenseToken> tokens = await classifier.ClassifyAsync("They will record the record.");
```

The tokens tile the input: concatenating `Text` reproduces the string exactly, so results map back onto it. Words,
runs of whitespace and single punctuation characters are separate tokens; `Sense` is set only for answered words.

## Confidence details

```csharp
WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(words);
// Sense, Margin (best minus runner-up), RunnerUp, RunnerUpMargin, Abstained
```

The detail is also returned for words the classifier abstained from (`Abstained` is true), so callers can apply
their own policy.

## Tag queries

```csharp
string?[] pos = await classifier.ClassifyTagAsync(words, ["noun", "verb"]); // "noun", "verb" or null
bool?[] isVerb = await classifier.IsTagAsync(words, "verb");               // true, false or null
```

A tag is scored by the best sense that carries it, so senses that agree on the tag do not weaken each other. The
result is `null` when the word is unknown, when none of the tags exists on the lemma (a tag is never guessed),
or when the evidence is too weak.

## Model files and mirrors

`WordSenseClassifier.CreateAsync` needs two files: `model_int8.onnx` (23 MB) and `vocab.txt`. If
`WordSenseClassifierOptions.ModelDirectory` is set, the files there are verified and used. Otherwise they are
stored in a per-user directory (`LocalApplicationData/DemaConsulting/WordSense/models/...`) and downloaded when
missing, from these sources in order:

1. **Your mirror**, when configured: `{mirror}/all-MiniLM-L6-v2/{file}`
2. **Hugging Face**, for files that are identical to the upstream files (only `vocab.txt`)
3. **GitHub LFS**: `https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/{file}`

Every file must match its pinned SHA-256 digest; on a request failure, timeout, block or digest mismatch the next
source is tried. If all fail, `WordSenseModelDownloadException.Failures` lists each attempt.

Configure a mirror with environment variables:

| Variable                        | Meaning                                       |
|---------------------------------|-----------------------------------------------|
| `WORDSENSE_MIRROR_URL`          | Absolute http(s) base URL of the mirror       |
| `WORDSENSE_MIRROR_USER`         | Basic authentication user (with the password) |
| `WORDSENSE_MIRROR_PASSWORD`     | Basic authentication password                 |
| `WORDSENSE_MIRROR_BEARER_TOKEN` | Bearer token (instead of user and password)   |

or in code, which takes precedence:

```csharp
var options = new WordSenseClassifierOptions
{
    Downloader = new WordSenseModelDownloaderOptions
    {
        Mirror = new DownloadMirror(new Uri("https://models.example.com/wordsense"), bearerToken: token),
    },
};
```

Credentials require https (except for loopback hosts) and are sent only to the mirror.

## Tuning

- `AbstainMarginScale`: multiplies every lemma's abstention threshold (above 1 abstains more often).
- `IntraOpThreads`: ONNX Runtime threads (0 selects the default).
- `MaxSequenceLength`: model window; longer texts are classified with a window around each target.

# API Reference

| Type                                             | Purpose                                                   |
|--------------------------------------------------|-----------------------------------------------------------|
| `IWordSenseClassifier`                           | Word and text `ClassifyAsync`, detail and tag queries     |
| `WordSenseClassifier`                            | Implementation; `CreateAsync`, `KnownLemmas`, `GetSenses` |
| `WordSenseClassifierOptions`                     | Model directory, downloader, threads, window, abstention  |
| `WordSense`, `WordSenseDetail`, `WordSenseToken` | Immutable sense with tags, detail, text tile              |
| `WordSenseModelDownloader`                       | `EnsureModelAsync`: verify or download the model files    |
| `WordSenseModelDownloaderOptions`                | Mirror, base URIs, store root, timeout, progress          |
| `DownloadMirror`                                 | Mirror base URL and credentials; `FromEnvironment`        |
| `IWordSenseEncoder`, `OnnxWordSenseEncoder`      | Encoder abstraction and ONNX Runtime encoder              |
| `WordSenseTable`                                 | The embedded classifier table (`LoadEmbedded`)            |

Exceptions: `WordSenseModelDownloadException`, `WordSenseModelMismatchException` (table and model differ) and
`WordSenseTableFormatException` (damaged table).

# Accuracy and Performance

Accuracy is measured on held-out sentences that are never used for training; see the build notes for current
numbers per lemma. On the 6,010 held-out rows the best sense is correct 96.36 % of the time (96.47 % at the
sense-group level), and 98.53 % of the answered rows are correct (90.77 % answered). For noun versus verb (5,383
held-out rows whose correct sense is exactly one of the two), the best tag is correct 97.12 % of the time, and
99.15 % of the answered rows are correct (91.90 % answered). On real text (public-domain and openly licensed
sentences that were judged blind and never used for training) the best sense is correct 94.92 % of the time
on 551 sentences, against 97.74 % on the project-written held-out sentences of the same lemmas. The lemmas `bow`,
`lead`, `tear`, `closer`, `upsetting`, `polish` and `orient` are supported with split senses (for example `bow`
bend-noun and bend-verb share the group `bend`).
A typical sentence takes a few milliseconds (median 5.6 ms with one thread, 2.5 ms with four
threads on a development workstation; run the `Benchmark` test category to measure your hardware).

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
