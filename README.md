# WordSense

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

WordSense is a .NET library that identifies a word in context: its lemma, its part of speech and features and, where
the senses allow, its sense: an Open English WordNet (OEWN) fine sense with a readable id such as
`bank#financial-institution` and its WordNet sense key, or the group of closely related senses it belongs to. Every
answer states the level it was made at (sense, group or part of speech), carries a confidence for that level and lists
the candidate analyses with their probabilities, so you know whether "bank" is the money kind, at least a financial
word, or only a noun. Every word is looked up in an embedded dictionary of over 140,000 common words. Only where a word
has several analyses does a model choose from the sentence: the `Basic` model, a small statistical tagger that needs
nothing else and answers with the part of speech, or an ONNX encoder package (`MiniLM` or `BertBaseCased`) that runs on
ONNX Runtime and, in this version, answers through the `Basic` model; scoring fine senses with the encoders follows in a
later version.

## Features

- 📖 **Dictionary** - Parts of speech for over 140,000 common words, with the senses of each lemma
- 🧠 **Context Aware** - Picks the analysis (lemma, part of speech, features) of a word from its sentence
- 🏷️ **Fine Senses and Groups** - Open English WordNet senses with readable ids, WordNet sense keys and coarse groups
- 🎚️ **Answer Levels** - Each answer says whether it is a sense, a group or a part of speech
- 📊 **Confidence** - Every answer carries a confidence from 0 to 1 that applies to its level
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

// Print the part of speech (or unknown), the level, the confidence, the sense id (at the sense level) and the
// runner-up analysis (the second most probable part of speech, when the word has one)
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var sense = result.SenseId ?? "";
    var next = result.Analyses.Length > 1
        ? $"{result.Analyses[1].Details.PartOfSpeech} {result.Analyses[1].Probability:F2}"
        : "";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Level,-12} {result.Confidence,4:F2}  {sense,-14} {next}".TrimEnd());
}
```

Output with the `Basic` model:

<!-- output:basic -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        0.50  deer#noun      Noun 0.50
so        Conjunction  Sense        0.61  so#conjunction Adverb 0.39
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00                 Verb 0.00
```

Output with the `MiniLM` model:

<!-- output:minilm -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        0.50  deer#noun      Noun 0.50
so        Conjunction  Sense        0.61  so#conjunction Adverb 0.39
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00                 Verb 0.00
```

Output with the `BertBaseCased` model:

<!-- output:bertbasecased -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        0.50  deer#noun      Noun 0.50
so        Conjunction  Sense        0.61  so#conjunction Adverb 0.39
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00                 Verb 0.00
```

The heteronyms `bow` and `record` are the interesting words. The `Level` of an answer says how precisely the model
answers: `Sense` for a word whose lemma and part of speech have one sense (the sense id is then given), `PartOfSpeech`
for a word with several meanings, whose fine senses these models do not score yet; `Analyses` lists every possible
analysis (lemma, part of speech and features) with its probability, `Details` is the most probable one, and a low
confidence marks a guess (for example `so`). The probabilities are comparable only within one result, never across
models. In this version the `MiniLM` and `BertBaseCased` packages answer every ambiguous word with the `Basic` model at
the part-of-speech level (their encoders will score fine senses in a later version), so the three models print the same
lines. The modal and helping verbs (`will`, `can`, `is`, `have`) have an `Auxiliary` analysis. A word that is not in the
dictionary has `IsKnown` false, `Details` of `null`, the level `None` and a confidence of 0.
Tokenize the text yourself, one sentence per call: punctuation is a token of its own. See the
[user guide][link-user-guide] for tokenizing, confidence thresholds, mirrors, disposal and threading.

## Choosing a model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Holdout best analysis | Real-text best analysis | Part of speech (holdout / real) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Basic | 91.93% | 89.40% | 92.13% / 90.42% | none | 83 KiB | 13 MiB | 0.08 ms | 24 ms + 55 ms | no |
| MiniLM | 91.93% | 89.40% | 92.13% / 90.42% | 22 MiB | 1,132 KiB | 12 MiB | 0.12 ms | 161 ms + 80 ms | yes |
| BERT base cased | 91.93% | 89.40% | 92.13% / 90.42% | 104 MiB | 2,245 KiB | 13 MiB | 0.08 ms | 211 ms + 58 ms | yes |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

- ⚡ **basic** - No download, fastest; about 89% best analysis on real text.
- ⚖️ **minilm** - The default encoder package: 22 MiB download; today it answers through the `Basic` model.
- 🎯 **bert-base-cased** - Larger case-sensitive encoder, 104 MiB download; today it answers through the `Basic` model.

The encoder packages score fine senses in a later version; until then their rows repeat the `Basic` numbers.
The holdout sentences were written with the training sentences, so the real-text column is the realistic expectation.
The table is generated into `docs/build_notes/model-table.md`; the user guide explains how the numbers were measured
and lists the confidence thresholds of each model. See also the [model comparison][link-model-comparison] and the
[Basic evaluation][link-basic-evaluation].

## Documentation

- **User Guide**: usage, configuration, limitations, licensing and troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its senses, groups, OEWN keys and respellings
  ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`

## Credits and licenses

The packages that embed derived data (`DemaConsulting.WordSense`, `DemaConsulting.WordSense.Basic`,
`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`) carry this section in their package
readme. The corpus itself is not shipped in the packages, only the derived tables and weights; every real sentence is
recorded with its source, license and URL in `corpus/provenance/`.

Package licenses:

- **`DemaConsulting.WordSense`** (core): `MIT AND CC-BY-4.0`. The code and the project-authored data are MIT; the
  embedded word image is derived from Open English WordNet 2025 (CC BY 4.0). The package contains a `NOTICE.md` with the
  attribution and the notices of Open English WordNet, WordNet 3.0, AGID and the Moby list.
- **`DemaConsulting.WordSense.Basic`**: `MIT AND CC-BY-SA-4.0`. The code is MIT; the embedded statistical table is
  derived from the UD English-EWT treebank (CC BY-SA 4.0). The package contains a `NOTICE.md` with the attribution.
- **`DemaConsulting.WordSense.Onnx`, `DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`**:
  `MIT`. The weights are statistical summaries of the corpus and do not contain its sentences.

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
  from it and keeps that license; see `data/README.md` and `data/PROVENANCE.md`. The sense ids, sense keys, lemmas and
  forms of the word image that the core package embeds are derived from it (changed: imported, extended and compiled);
  the entries, glosses and examples are not in the image.
- **WordNet 3.0** (Princeton University, <https://wordnet.princeton.edu>): the sense frequency counts of the word data
  (`data/sense_frequency`, from the WordNet 3.0 sense index `index.sense`) are used under the WordNet license: WordNet 3.0
  Copyright 2006 by Princeton University, all rights reserved; the full notice is in `data/PROVENANCE.md`.
- **SemCor 3.0** (the sense-annotated Brown Corpus text of the WordNet project): an evaluation source only. It is
  downloaded to a cache by the builder to measure the models, never committed, redistributed or used for training.

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
