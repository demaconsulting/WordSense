# WordSense

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

WordSense is a .NET library that tells, for words in context, the part of speech and the pronunciation: whether
"record" is a noun or a verb, and how to say "bow" or "read". Every word is looked up in an embedded dictionary of
over 140,000 common words. Only where a word has several meanings does a model choose from the sentence: either the
`Basic` model, which is a small statistical tagger that needs nothing else, or an ONNX encoder model (`MiniLM` or
`BertBaseCased`) that is more accurate and runs on ONNX Runtime.

## Features

- 📖 **Dictionary** - Parts of speech for over 140,000 common words, with no model call for most of them
- 🧠 **Context Aware** - Decides the part of speech of a word from its sentence
- 🔊 **Pronunciation** - Heteronyms like "bow" and "read" get a meaning id and a respelling
- 📊 **Confidence** - Every answer carries a confidence from 0 to 1; 0 means no answer
- 📦 **Layered Packages** - A small core, a dependency-free `Basic` model, and ONNX models you add only if you need them
- 🔒 **Verified Models** - Encoder files are downloaded on first use and checked against pinned SHA-256 digests
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence generated on every CI run, following the
  [Continuous Compliance][link-continuous-compliance] methodology

## Installation

Pick the model package that fits; each one brings the core package `DemaConsulting.WordSense` with it.

```bash
dotnet add package DemaConsulting.WordSense.Basic          # small statistical model: no model files, no ONNX Runtime
dotnet add package DemaConsulting.WordSense.MiniLM         # small and fast encoder (22 MiB model files)
dotnet add package DemaConsulting.WordSense.BertBaseCased  # larger, case-sensitive encoder
```

| Package | Contents | Needs |
| :--- | :--- | :--- |
| `DemaConsulting.WordSense` | The model interface and the dictionary (`WordLexicon`) | Nothing |
| `DemaConsulting.WordSense.Basic` | Statistical model (part-of-speech tagger and context cues) | Nothing |
| `DemaConsulting.WordSense.Onnx` | Shared ONNX implementation (brought in by the model packages) | ONNX Runtime |
| `DemaConsulting.WordSense.MiniLM` | MiniLM model and its weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.BertBaseCased` | BERT base cased model and weights | Model files, downloaded on first use |

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and, for the ONNX models, internet access on first use, or a
mirror, to download the model files (they are not part of the package). Contributors who clone this repository also
need [Git LFS](https://git-lfs.com) (`git lfs install`, then `git lfs pull`), because the files in `model/` are stored
in Git LFS.

## Usage

The examples below are checked by the test suite (`ReadmeSampleTests`, `BasicReadmeSampleTests` and
`ModelTableReadmeTests`): each code block must equal the compiled sample
code in `test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSample.cs` (MiniLM) or
`test/DemaConsulting.WordSense.Basic.Tests/BasicReadmeSample.cs` (Basic), and each output block must equal what that
code prints. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and troubleshooting.

### MiniLM

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

// Create the model and make sure its files are available (downloaded and verified on first use)
using var model = new MiniLMWordSenseModel();
await model.EnsureModelAsync();

// Classify the words of a sentence whose "bow" and "record" are heteronyms
var words = "The hunter fired an arrow from his bow so I will record a record".Split(' ');
var results = await model.ClassifyAsync(words);

// Print the part of speech (or unknown), the confidence and, for a heteronym, the respelling
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var say = result.Details?.Respelling ?? "";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}  {say}".TrimEnd());
}
```

<!-- output:readme -->
```text
The       unknown      0.00
hunter    Noun         1.00
fired     Verb         1.00
an        unknown      0.00
arrow     Noun         1.00
from      Preposition  1.00
his       unknown      0.00
bow       Noun         1.00  boh
so        unknown      0.00
I         Pronoun      1.00
will      unknown      0.00
record    Verb         1.00  rih-KORD
a         unknown      0.00
record    Noun         1.00  REK-urd
```

### Basic

<!-- sample:basic -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

// The basic model uses the embedded dictionary and a small statistical table, so there is nothing to download
using var model = new BasicWordSenseModel();
await model.EnsureModelAsync();

var words = "They record the record".Split(' ');
var results = await model.ClassifyAsync(words);

// A word that is not in the dictionary has no details and a confidence of 0
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}");
}
```

<!-- output:basic -->
```text
They      Pronoun      1.00
record    Verb         0.99
the       unknown      0.00
record    Noun         1.00
```

A word that is not in the dictionary (`will` and `the` above, punctuation, a contraction such as `don't`) has no details
and a confidence of 0. A word with one meaning is answered with confidence 1. A word with several meanings is resolved
from its sentence by the model and always gets its best sense, even when the confidence is low; `Details` is `null` only
for an unknown word (or, for an encoder model, a multi-sense word without trained weights, which does not occur in the
shipped packages). Its confidence is defined by the model package: the `Basic` model reports the probability of its
answer among the senses of the word, the ONNX models derive it from the margin of the trained classifier (0.5 is the
abstain point), so a confidence threshold must not be reused across models. The encoder runs only for a
sentence that has a word that needs it.

Tokenize the text yourself: punctuation is a token of its own, a contraction (`don't`) or hyphenated word stays one
token, and one sentence per call is recommended (Basic splits at `.` `!` `?`; the encoders read the list as one text of
at most 128 word pieces by default and use a window around the word for longer texts).

The model API: `EnsureModelAsync` is optional for every model. Call it to fail early (for an encoder model it verifies
the SHA-256 of the files, with no network access when you pass a model directory) and to pass a `MirrorOptions`.
`ClassifyAsync` also ensures the model lazily
on first use, but only when a word needs it (a list without an ambiguous word is answered from the dictionary and
never touches model files, so a model that is never needed is never downloaded); the lazy ensure uses the default and
environment mirror settings and may throw the same exceptions as `EnsureModelAsync` (for example
`FileNotFoundException` or `InvalidDataException` for a supplied directory), so call `EnsureModelAsync` yourself to pass
a `MirrorOptions` (ignored while an ensure is already in flight). The ensured state belongs to one model instance. Dispose
a model when you no longer need it; disposing while calls are running is safe: new calls then throw
`ObjectDisposedException`, the running
calls complete, and an encoder model releases its ONNX session after the last one. Cancelling the token of
`EnsureModelAsync` only ends that call's wait: the shared ensure keeps running, the next call joins it (its
`MirrorOptions` are ignored) or, after a failure, starts a new one; an already cancelled token returns a cancelled task
(or the already completed task) instead of throwing. The Basic model runs synchronously on
the calling thread; an encoder model hashes and downloads on the thread pool but runs inference synchronously, and a
cancellation token is checked between steps (a single ONNX run is never interrupted). The
ONNX models run on the CPU only, and concurrent classifications share one ONNX session and run in parallel. Model
files are
stored in a per-user directory under their model ID (for example `all-MiniLM-L6-v2-int8`) and reused on later runs; a
directory you supply must hold the files directly (flat, no model-ID subfolder). To use your own mirror, set
`WORDSENSE_MIRROR_URL`
(optionally with `WORDSENSE_MIRROR_USER` and `WORDSENSE_MIRROR_PASSWORD`, or `WORDSENSE_MIRROR_BEARER_TOKEN`) or pass a
`MirrorOptions` to `EnsureModelAsync`; it is tried first, and it serves the files at `{mirror}/{model-id}/{file}`. A
user without a password (or the reverse), or a user, password or token without the URL, throws `ArgumentException`; the
variables are ignored when you pass a model directory, a relative, non-http(s), query, fragment or user-info `BaseUri` or
`WORDSENSE_MIRROR_URL` throws `ArgumentException`, and a `MirrorOptions.Timeout` that is zero, negative or longer than
about 49 days throws `ArgumentOutOfRangeException`. Files you pre-populate in `{StoreRoot}/{model-id}/` are verified
and reused without network access. Downloads use the default .NET proxy settings; allow-list your mirror and
`media.githubusercontent.com`. No
environment variable sets the store location; only `MirrorOptions.StoreRoot` does (for example `/var/lib/wordsense` or
`C:\ProgramData\WordSense\models`; a relative path is resolved against the current directory). See
the [user guide][link-user-guide] for details and the [senses appendix][link-appendix] for the list of lemmas.

## Choosing a model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Holdout best sense | Real-text best sense | Part of speech (holdout / real) | Pronunciation (holdout / real) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| MiniLM | 96.87% | 92.60% | 97.22% / 95.36% | 96.41% / 94.45% | 22 MiB | 1,129 KiB | 64 MiB | 1.84 ms | 388 ms + 122 ms | yes |
| BERT base cased | 99.05% | 97.39% | 99.22% / 98.40% | 99.25% / 98.20% | 104 MiB | 2,241 KiB | 150 MiB | 8.55 ms | 521 ms + 118 ms | yes |
| Basic | 88.65% | 84.76% | 89.73% / 90.13% | 89.62% / 87.41% | none | 83 KiB | 21 MiB | 0.03 ms | 22 ms + 122 ms | no |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

- ⚡ **basic** - Enough when you want no download and the fastest start; about 85% best-sense accuracy on real text.
- ⚖️ **minilm** - The default recommendation: a 22 MiB download, a few milliseconds per sentence and about 93% on real text.
- 🎯 **bert-base-cased** - Choose it when accuracy matters most and a 104 MiB download and about 150 MiB of RAM are fine.

"Download needed: yes" means the files are downloaded the first time a classification needs the encoder (lazily, by
`ClassifyAsync`, or earlier by `EnsureModelAsync`); a model whose encoder is never needed is never downloaded.

The numbers come from the repository's build tool (`evaluate <model> --table`), run once per model on one machine with
at most two ONNX threads. Holdout sentences were written together with the training sentences, so the holdout accuracy
is optimistic; the real-text column is the realistic expectation. Absolute times and memory depend on the machine; use
them as relative figures. The RAM column is what the model adds, measured after the word lexicon (about 43 MiB) was
loaded, so it excludes the lexicon; it is the growth of the process's private memory after a full garbage collection,
so it includes allocator and JIT effects. The table is generated into `docs/build_notes/model-table.md` and a test
keeps the README
equal to it.

Confidence thresholds (coverage of the ambiguous target words / precision on real text, rounded; holdout is higher).
A threshold belongs to one model: read it from that model's row, and never reuse it across models. The recommended
starting value is 0.8 for each model (about one error in fourteen answers for Basic, one in fifty for MiniLM and one in
a hundred for BERT base cased). Basic's confidence for a word with n senses is never below 1/n (0.5 for two senses);
so a threshold at or below 1/n is not a filter for Basic. Coverage is the share of the target words (one annotated
ambiguous word per evaluation sentence) whose confidence is at or above the threshold; precision is the share of those
that are right. For an encoder, 0.5 is where the margin equals the trained abstain margin, so it is the lowest
meaningful value and
not a useful filter:

| Model | at least 0.6 | at least 0.8 | at least 0.9 |
| --- | --- | --- | --- |
| Basic | 90% / 89% | 78% / 93% | 70% / 96% |
| MiniLM | 82% / 97% | 73% / 98% | 69% / 98% |
| BERT base cased | 85% / 99% | 70% / 99% | 64% / 99% |

The `ModelId` values are `all-MiniLM-L6-v2-int8` and `bert-base-cased-int8`. The default model store is
`%LOCALAPPDATA%\DemaConsulting\WordSense\models` on Windows and `~/.local/share/DemaConsulting/WordSense/models` on
Linux and macOS. All sizes are in MiB. Basic takes 0.03 ms (about 30 µs) per sentence, under 0.1 ms.

## Tokenizing text

Split the text into words yourself, one sentence per call: punctuation is a token of its own (`"race"`, `"."`), while a
contraction (`don't`), a possessive (`dog's`) and a hyphenated word (`well-known`) stay one token (they are unknown to
the lexicon, so they return null with confidence 0, and their neighbors are classified normally). Keep decimals such as
`3.5` as one token, because for Basic a lone period ends the sentence; the tokenizer below does not handle
abbreviations (it splits `Mr.` into `Mr` and `.`), so merge such an abbreviation into one token yourself or drop its
periods. The lookup ignores case (the models lower-case for you; the dictionary itself is case-sensitive); MiniLM is
uncased, BERT base cased is cased, and Basic uses case only to tag unknown capitalized words as
nouns.

```csharp
using System.Text.RegularExpressions;

var tokenPattern = new Regex(
    @"\d+(?:[.,]\d+)+|[\p{L}\p{Nd}\p{Mn}\p{Mc}]+(?:['\u2019-][\p{L}\p{Nd}\p{Mn}\p{Mc}]+)*|[\uD800-\uDBFF][\uDC00-\uDFFF]|\S",
    RegexOptions.CultureInvariant);

string[] Tokenize(string sentence) => tokenPattern.Matches(sentence).Select(match => match.Value).ToArray();

// Smith's | well-known | dog | didn't | " | bark | " | at | 3.5 | cats | .
string[] tokens = Tokenize("Smith's well-known dog didn't \"bark\" at 3.5 cats.");
```

The edge-case table is in the [user guide][link-user-guide]. `IWordSenseModel` is `IDisposable`; dispose any model with
`using`. After disposal the model's `EnsureModelAsync` and `ClassifyAsync` throw `ObjectDisposedException`.
In code under a
`DemaConsulting.*` namespace add `using WordSense = DemaConsulting.WordSense.WordSense;` (the type name clashes with the
namespace), and test for no answer with `Details is null`, not `Confidence == 0` (only a null `Details` means unknown;
an ambiguous word with non-null `Details` can have any confidence from 0 to 1, so below your threshold treat it as
unresolved or show `Details` as a guess).

## Accuracy and Limitations

Accuracy depends on the model and is measured by the repository's build tool on held-out and real-text sentences; the
results of all models are collected in the [model comparison][link-model-comparison] and the Basic model's in the
[Basic evaluation][link-basic-evaluation]. Words that are in neither the dictionary nor the trained lemmas are not
decided by a model; an encoder model could in principle leave a word without trained weights undecided, but the shipped
packages have weights for every lemma. The
dictionary knows the inflected forms that were materialized when it was built; determiners, numbers and most proper nouns
are unknown. The [user guide][link-user-guide] has the details, the caveats and troubleshooting.

## Documentation

- **User Guide**: choosing a package, usage, configuration, limitations, licensing, extending the corpus and
  troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its parts of speech, pronunciation meanings and forms
  ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`
- **Trace Matrix**: requirements to test traceability
- **Model provenance**: `docs/build_notes/model-provenance.md`
- **Package split**: `docs/build_notes/package-split.md`

## Credits and licenses

The packages that embed derived data (`DemaConsulting.WordSense`, `DemaConsulting.WordSense.Basic`,
`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`) carry this section in their package
readme. The corpus itself is not shipped in the packages, only the derived tables and weights; every real sentence is
recorded with its source, license and URL in `corpus/provenance/`.

- **Moby Part-of-Speech List** (Grady Ward): public domain; the embedded dictionary is derived from it.
- **Project-authored words, senses, meanings, respellings and sentences**: MIT License.
- **Tatoeba sentences** (<https://tatoeba.org>): CC0 1.0, or CC BY 2.0 FR with the contributor recorded.
- **Wikipedia and Wiktionary example sentences** (<https://www.wikipedia.org>, <https://www.wiktionary.org>):
  CC BY-SA 4.0, with the article URL recorded.
- **Universal Dependencies English corpora** (EWT and others, <https://universaldependencies.org>): CC BY-SA 4.0.
  The tag statistics of the UD English-EWT treebank (<https://github.com/UniversalDependencies/UD_English-EWT>,
  CC BY-SA 4.0) train the statistical model of the `Basic` package; see `corpus/ud/PROVENANCE.md`.
- **Project Gutenberg works** (<https://www.gutenberg.org>): US public domain.
- **Corpus redistribution**: rows under CC BY-SA 4.0 keep that license if the corpus itself is redistributed.
- **sentence-transformers/all-MiniLM-L6-v2** (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>): Apache
  License 2.0; the int8 ONNX export is redistributed under the same license.
- **bert-base-cased** (Google, <https://huggingface.co/google-bert/bert-base-cased>): Apache License 2.0; the int8 ONNX
  export is redistributed under the same license.
- **ONNX Runtime** (Microsoft Corporation): MIT License; installed as a separate NuGet dependency.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/WordSense?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/WordSense?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/WordSense?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/WordSense?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/WordSense/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_WordSense&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_WordSense&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.WordSense?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/WordSense/network/members
[link-stars]: https://github.com/demaconsulting/WordSense/stargazers
[link-contributors]: https://github.com/demaconsulting/WordSense/graphs/contributors
[link-license]: https://github.com/demaconsulting/WordSense/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/WordSense/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_WordSense
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_WordSense
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.WordSense
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/WordSense/blob/main/CONTRIBUTING.md
[link-user-guide]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/introduction.md
[link-appendix]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md
[link-model-comparison]: https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-comparison.md
[link-basic-evaluation]: https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md
