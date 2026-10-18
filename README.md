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

- 📖 **Dictionary** - Parts of speech for over 140,000 common words
- 🧠 **Context Aware** - Picks the part of speech of a word from its sentence
- 🔊 **Pronunciation** - Sense ids for heteronyms like "bow" and "read"
- 📊 **Confidence** - Every answer carries a confidence from 0 to 1
- 📦 **Layered Packages** - Small core, dependency-free Basic, optional ONNX models
- 🔒 **Verified Models** - Downloads are checked against pinned SHA-256 digests
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence on every CI run ([method][link-continuous-compliance])

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
| `DemaConsulting.WordSense.MiniLM` | MiniLM model and its weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.BertBaseCased` | BERT base cased model and weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.Onnx` | Shared ONNX implementation (brought in by the encoder packages) | ONNX Runtime |

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and, for the ONNX models, internet access on first use, or a
mirror, to download the model files. Contributors who clone this repository also need [Git LFS](https://git-lfs.com).

## Quick Start

The sample below is checked by the test suite: its code is compiled, and its output is what it prints with each of the
three models. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and troubleshooting.

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

// Basic needs no download; MiniLM and BertBaseCased need their package and using, and download on first use
using var model = new BasicWordSenseModel();
// using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
// using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;
await model.EnsureModelAsync();

// Classify the words of a sentence whose "bow" and "record" are heteronyms
var words = "The hunter fired an arrow from his bow at a deer so we will record a record".Split(' ');
var results = await model.ClassifyAsync(words);

// Print the part of speech (or unknown), the confidence and, for a heteronym, the sense id
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var say = result.Details?.SenseId ?? "";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Confidence,4:F2}  {say}".TrimEnd());
}
```

Output with the `Basic` model:

<!-- output:basic -->
```text
The       Determiner   1.00
hunter    Noun         1.00
fired     Verb         1.00
an        Determiner   1.00
arrow     Noun         1.00
from      Preposition  1.00
his       Determiner   1.00
bow       Noun         0.78  bow#weapon
at        Preposition  1.00
a         Determiner   1.00
deer      Noun         0.50
so        Conjunction  0.61
we        Pronoun      1.00
will      Auxiliary    1.00
record    Verb         1.00  record#verb
a         Determiner   1.00
record    Noun         1.00  record#noun
```

Output with the `MiniLM` model:

<!-- output:minilm -->
```text
The       Determiner   1.00
hunter    Noun         1.00
fired     Verb         0.50
an        Determiner   1.00
arrow     Noun         1.00
from      Preposition  1.00
his       Determiner   1.00
bow       Noun         1.00  bow#weapon
at        Preposition  0.50
a         Determiner   1.00
deer      Noun         0.50
so        Adverb       0.17
we        Pronoun      1.00
will      Verb         0.50
record    Verb         1.00  record#verb
a         Determiner   1.00
record    Noun         1.00  record#noun
```

Output with the `BertBaseCased` model:

<!-- output:bertbasecased -->
```text
The       Determiner   1.00
hunter    Noun         1.00
fired     Verb         0.50
an        Determiner   1.00
arrow     Noun         1.00
from      Preposition  1.00
his       Determiner   1.00
bow       Noun         1.00  bow#weapon
at        Preposition  0.50
a         Determiner   1.00
deer      Noun         0.50
so        Adverb       0.17
we        Pronoun      1.00
will      Verb         0.50
record    Verb         0.85  record#verb
a         Determiner   1.00
record    Noun         0.35  record#noun
```

The heteronyms `bow` and `record` get their sense id. A word with one meaning is answered with confidence 1; a word
with several meanings gets its best sense, and a low confidence marks a guess (for example `so`), so read the
confidence of the model you use. The modal and helping verbs (`will`, `can`, `is`, `have`) have an `Auxiliary` sense that
only the `Basic` model answers. The confidences shown for the encoder models are approximate: they differ slightly
between CPUs and operating systems. A word that is not in the dictionary has `Details` of `null` and a confidence of 0.
Tokenize the text yourself, one sentence per call: punctuation is a token of its own. See the
[user guide][link-user-guide] for tokenizing, confidence thresholds, mirrors, disposal and threading.

## Choosing a model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Holdout best sense | Real-text best sense | Part of speech (holdout / real) | Pronunciation (holdout / real) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Basic | 91.23% | 85.20% | 92.31% / 90.13% | 90.26% / 87.41% | none | 87 KiB | 21 MiB | 0.05 ms | 36 ms + 64 ms | no |
| MiniLM | 96.91% | 92.60% | 97.25% / 95.36% | 96.46% / 94.45% | 22 MiB | 1,129 KiB | 64 MiB | 1.84 ms | 388 ms + 201 ms | yes |
| BERT base cased | 99.05% | 97.39% | 99.23% / 98.40% | 99.25% / 98.20% | 104 MiB | 2,241 KiB | 150 MiB | 8.55 ms | 521 ms + 173 ms | yes |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

- ⚡ **basic** - No download, fastest; about 85% on real text.
- ⚖️ **minilm** - The default: 22 MiB download, about 2 ms a sentence, about 93%.
- 🎯 **bert-base-cased** - Most accurate, about 97%; 104 MiB download, about 150 MiB RAM.

The holdout sentences were written with the training sentences, so the real-text column is the realistic expectation.
The table is generated into `docs/build_notes/model-table.md`; the user guide explains how the numbers were measured
and lists the confidence thresholds of each model. See also the [model comparison][link-model-comparison] and the
[Basic evaluation][link-basic-evaluation].

## Documentation

- **User Guide**: usage, configuration, limitations, licensing and troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its parts of speech and pronunciations ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`

## Credits and licenses

The packages that embed derived data (`DemaConsulting.WordSense`, `DemaConsulting.WordSense.Basic`,
`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`) carry this section in their package
readme. The corpus itself is not shipped in the packages, only the derived tables and weights; every real sentence is
recorded with its source, license and URL in `corpus/provenance/`.

- **Moby Part-of-Speech List** (Grady Ward): public domain; the embedded dictionary is derived from it.
- **Automatically Generated Inflection Database (AGID)** (Kevin Atkinson, 2000-2016,
  <http://wordlist.aspell.net/other-dicts/>): permission notice (use, copy, modify and distribute without fee with the
  notice kept). It is read at build time as one source of irregular inflected forms; the derived rows are embedded.
  AGID carries the **Princeton WordNet 1.6** notice (Copyright 1997 by Princeton University) and the **UK Advanced
  Cryptics Dictionary** notice (Copyright (c) J Ross Beresford 1993-1999); the full texts are in
  `corpus/lexicon/LICENSE-AGID.txt` and apply to the embedded forms.
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
- **Open English WordNet 2025** (<https://en-word.net>, <https://github.com/globalwordnet/english-wordnet>): CC BY 4.0.
  The word data under `data/` (entries, senses, synsets, glosses, examples, relations and pronunciations) is imported
  from it and keeps that license; see `data/README.md` and `data/PROVENANCE.md`. It is not yet shipped in a package.

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
