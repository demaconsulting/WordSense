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
var tokens = await classifier.ClassifyAsync("The record was broken, so they will record the new album.");
foreach (var token in tokens.Where(t => t.Sense is not null))
{
    Console.WriteLine($"{token.Text}: {token.Sense!.Label} (margin {token.Sense.Margin:0.00})");
}
```

The tokens tile the input: concatenating `Token.Text` reproduces the text exactly, so results can be mapped back
to the original string. Words that are not known, and words for which the evidence is weak, have no `Sense`.

## Classifying one word

```csharp
var result = await classifier.ClassifyAsync(text, targetStart, targetLength);
// null when the span is not a known word or the classifier abstains
```

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

| Type                                        | Purpose                                                        |
|---------------------------------------------|----------------------------------------------------------------|
| `IWordSenseClassifier`                      | `ClassifyAsync(text)` and `ClassifyAsync(text, start, length)` |
| `WordSenseClassifier`                       | Implementation; `CreateAsync`, `KnownLemmas`, `GetSenseLabels` |
| `WordSenseClassifierOptions`                | Model directory, downloader, threads, window, abstention       |
| `WordSenseToken`, `WordSenseResult`         | Result types: tile text and optional sense                     |
| `WordSenseModelDownloader`                  | `EnsureModelAsync`: verify or download the model files         |
| `WordSenseModelDownloaderOptions`           | Mirror, base URIs, store root, timeout, progress               |
| `DownloadMirror`                            | Mirror base URL and credentials; `FromEnvironment`             |
| `IWordSenseEncoder`, `OnnxWordSenseEncoder` | Encoder abstraction and ONNX Runtime encoder                   |
| `WordSenseTable`                            | The embedded classifier table (`LoadEmbedded`)                 |

Exceptions: `WordSenseModelDownloadException`, `WordSenseModelMismatchException` (table and model differ) and
`WordSenseTableFormatException` (damaged table).

# Accuracy and Performance

Accuracy is measured on held-out sentences that are never used for training; see the build notes for current
numbers per lemma. A typical sentence takes a few milliseconds (median 5.6 ms with one thread, 2.5 ms with four
threads on a development workstation; run the `Benchmark` test category to measure your hardware).

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
