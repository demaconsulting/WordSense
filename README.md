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
dotnet add package DemaConsulting.WordSense.MiniLM         # small and fast encoder (23 MB model files)
dotnet add package DemaConsulting.WordSense.BertBaseCased  # larger, case-sensitive encoder
```

| Package | Contents | Needs |
| :--- | :--- | :--- |
| `DemaConsulting.WordSense` | Contracts and the dictionary (`WordLexicon`) | Nothing |
| `DemaConsulting.WordSense.Basic` | Statistical model (part-of-speech tagger and context cues) | Nothing |
| `DemaConsulting.WordSense.Onnx` | Shared ONNX implementation (brought in by the model packages) | ONNX Runtime |
| `DemaConsulting.WordSense.MiniLM` | MiniLM model and its weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.BertBaseCased` | BERT base cased model and weights | Model files, downloaded on first use |

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and, for the ONNX models, internet access on first use, or a
mirror, to download the model files (they are not part of the package). Contributors who clone this repository also
need [Git LFS](https://git-lfs.com) (`git lfs install`, then `git lfs pull`), because the files in `model/` are stored
in Git LFS.

## Usage

The examples below are checked by the test suite (`ReadmeSampleTests`): each code block must equal the compiled sample
code in `test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSample.cs`, and each output block must equal what that code
prints. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and troubleshooting.

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
var results = await model.Create().ClassifyAsync(words);

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
var model = new BasicWordSenseModel();
await model.EnsureModelAsync();

var words = "They will record the record".Split(' ');
var results = await model.Create().ClassifyAsync(words);

// A word the model cannot decide has no details and a confidence of 0
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}");
}
```

<!-- output:basic -->
```text
They      Pronoun      1.00
will      Verb         0.99
record    Verb         0.99
the       unknown      0.00
record    Noun         0.95
```

A word that is not in the dictionary has no details and a confidence of 0. A word with one meaning is answered with
confidence 1. A word with several meanings is resolved from its sentence by the model; its confidence is defined by the
model package (the `Basic` model reports the probability of its answer among the senses of the word; the ONNX models derive it from the margin of the
trained classifier, and the best candidate is still returned when the confidence is low). The encoder runs only for a
sentence that has a word that needs it. Encoder model files are stored in a per-user directory under their model ID
(for example `all-MiniLM-L6-v2-int8`) and reused on later runs. To use your own mirror, set `WORDSENSE_MIRROR_URL`
(optionally with `WORDSENSE_MIRROR_USER` and `WORDSENSE_MIRROR_PASSWORD`, or `WORDSENSE_MIRROR_BEARER_TOKEN`) or pass a
`MirrorOptions` to `EnsureModelAsync`; it is tried first, and it serves the files at `{mirror}/{model-id}/{file}`. See
the [user guide][link-user-guide] for details and the [senses appendix][link-appendix] for the list of lemmas.

## Accuracy and Limitations

Accuracy depends on the model and is measured by the repository's build tool on held-out and real-text sentences; the
results of all models are collected in the [model comparison][link-model-comparison] and the Basic model's in the
[Basic evaluation][link-basic-evaluation]. Words that are in neither the dictionary nor the trained lemmas are not
decided by a model, and a model that cannot decide a word leaves its details empty instead of guessing. The
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
