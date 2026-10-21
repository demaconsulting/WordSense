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
has several analyses does a model choose from the sentence: the `Basic` model, which is part of the core package (a
small statistical tagger that needs nothing else, no download and no ONNX Runtime, and answers with the part of speech),
or an ONNX encoder package (`MiniLM` or `BertBaseCased`) that runs on ONNX Runtime and scores the fine senses of the
words it was trained for. In this version the encoders know 775 lemmas (the senses appendix lists them); every other
ambiguous word is answered by the `Basic` model at the part-of-speech level, and more words follow in later versions.

## Features

- 📖 **Dictionary** - Parts of speech for over 140,000 common words, with the senses of each lemma
- 🧠 **Context Aware** - Picks the analysis (lemma, part of speech, features) of a word from its sentence
- 🏷️ **Fine Senses and Groups** - Open English WordNet senses with readable ids, WordNet sense keys and coarse groups
- 🎚️ **Answer Levels** - Each answer says whether it is a sense, a group or a part of speech
- 📊 **Confidence** - Every answer carries a confidence from 0 to 1 that applies to its level
- 📦 **Layered Packages** - Core with a dependency-free Basic model, optional ONNX encoder models
- 🔒 **Verified Models** - Downloads are checked against pinned SHA-256 digests
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence on every CI run ([method][link-continuous-compliance])

## Installation

The core package already answers every word in context with its `Basic` model; the encoder packages add the fine senses
of the common words and bring the core package with them.

```bash
dotnet add package DemaConsulting.WordSense                # dictionary, senses and the Basic model; no download
dotnet add package DemaConsulting.WordSense.MiniLM         # small and fast encoder (22 MiB model files)
dotnet add package DemaConsulting.WordSense.BertBaseCased  # larger, case-sensitive encoder
```

| Package | Contents | Needs |
| :--- | :--- | :--- |
| `DemaConsulting.WordSense` | Dictionary (`WordLexicon`), model interface and the `Basic` statistical model | Nothing |
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

// Basic is part of the core package and needs no download; MiniLM and BertBaseCased need their package and using,
// and download on first use
using var model = new BasicWordSenseModel();
// using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
// using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;
await model.EnsureModelAsync();

// Classify the words of a sentence whose "bow" and "record" are heteronyms and whose "go", "back" and "home"
// are among the trained words of the encoder models
var words = "The hunter fired an arrow from his bow at a deer so we will record a record then go back home".Split(' ');
var results = await model.ClassifyAsync(words);

// Print the part of speech (or unknown), the level, the confidence, the sense id (at the sense level) or the
// group id (at the group level) and the runner-up part of speech with its probability, when the word has one
// (the analyses that share a part of speech, such as the singular and the plural of "deer", share its probability)
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var sense = result.SenseId ?? result.GroupId ?? "";
    var next = result.Analyses
        .Where(a => a.Details.PartOfSpeech != result.Details?.PartOfSpeech)
        .GroupBy(a => a.Details.PartOfSpeech)
        .Select(g => (Pos: g.Key, Probability: g.Sum(a => a.Probability)))
        .OrderByDescending(g => g.Probability)
        .Select(g => $"{g.Pos} {g.Probability:F2}")
        .FirstOrDefault("");
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
deer      Noun         Sense        1.00  deer#noun
so        Conjunction  Sense        0.54  so#conjunction Adverb 0.46
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Verb 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00                 Verb 0.00
then      Adverb       PartOfSpeech 0.99                 Noun 0.01
go        Verb         PartOfSpeech 1.00                 Noun 0.00
back      Adverb       PartOfSpeech 0.96                 Noun 0.02
home      Noun         PartOfSpeech 0.64                 Adverb 0.30
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
deer      Noun         Sense        1.00  deer#noun
so        Conjunction  Sense        0.54  so#conjunction Adverb 0.46
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Verb 0.00
record    Verb         PartOfSpeech 0.95                 Noun 0.05
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.96                 Verb 0.04
then      Adverb       PartOfSpeech 0.99                 Noun 0.01
go        Verb         PartOfSpeech 0.94                 Noun 0.05
back      Adverb       Group        0.99  back#group:return Noun 0.00
home      Noun         PartOfSpeech 0.71                 Adverb 0.28
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
deer      Noun         Sense        1.00  deer#noun
so        Conjunction  Sense        0.54  so#conjunction Adverb 0.46
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Verb 0.00
record    Verb         PartOfSpeech 0.99                 Noun 0.01
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.90                 Verb 0.10
then      Adverb       PartOfSpeech 0.99                 Noun 0.01
go        Verb         PartOfSpeech 0.99                 Noun 0.01
back      Adverb       Group        1.00  back#group:return Noun 0.00
home      Adverb       PartOfSpeech 0.59                 Noun 0.16
```

The words `bow`, `record`, `go`, `back` and `home` are trained by the encoder models in this version (the senses
appendix lists them), while `Basic` answers every word at the part-of-speech level (and a word with one sense at the
sense level). The `Level` of an answer says how precisely the model answers. `Sense` is a fine sense
(`back#to-former-place`,
with its id and sense key in the catalog, and also for a word whose lemma and part of speech have only one sense, such
as `the`), `Group` is a group of closely related senses (`GroupId`; MiniLM and BertBaseCased answer `back` at that level),
and `PartOfSpeech` is the analysis only: the model could not tell the senses apart with the precision its thresholds ask
for (the thresholds are calibrated on real text, so the encoder models answer most ambiguous words at this level: MiniLM
answers `bow`, `record`, `go` and `home` at that level and BertBaseCased `bow`, `record`, `go` and `home`), and the
ranked
senses of each analysis in `Analyses` still show how the
evidence spreads. `Analyses` lists every possible analysis (lemma, part of speech and features) with its probability,
`Details` is the best analysis of the most probable lemma and part of speech, and a low confidence marks a guess (for
example `so`). The senses belong to a lemma and a part of speech, not to its feature variants: `deer` is a singular
and a plural of one noun, so its two analyses share the probability of the noun and the confidence is 1.00, not split
in two.
The probabilities are
comparable only within one result, never across models. A model never answers an untrained word at the sense level
unless its lemma and part of speech have a single sense. The modal and helping verbs (`will`, `can`, `is`, `have`) have
an `Auxiliary` analysis. A word that is not in the dictionary has `IsKnown` false, `Details` of `null`, the level
`None` and a confidence of 0.
Tokenize the text yourself, one sentence per call: punctuation is a token of its own. See the
[user guide][link-user-guide] for tokenizing, confidence thresholds, mirrors, disposal and threading.

## Choosing a model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Real-text best analysis (test split) | Real-text part of speech (test split) | Written-holdout best analysis (secondary) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Basic | 92.24% | 92.74% | 93.12% | none | 145 KiB | 4 MiB | 0.15 ms | 45 ms + 83 ms | no |
| MiniLM | 94.87% | 95.05% | 96.71% | 22 MiB | 4,161 KiB | 99 MiB | 2.05 ms | 583 ms + 55 ms | yes |
| BERT base cased | 97.19% | 97.46% | 99.10% | 104 MiB | 8,152 KiB | 206 MiB | 10.15 ms | 713 ms + 56 ms | yes |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

- ⚡ **basic** - No download, fastest; answers at the part-of-speech level (and the sense of a word with one sense).
- ⚖️ **minilm** - The default encoder package: 22 MiB download; fine senses for the 775 trained lemmas, `Basic` for the
  rest.
- 🎯 **bert-base-cased** - Larger case-sensitive encoder, 104 MiB download; the most precise on the trained lemmas.

**The headline figures are measured on real text**: the 2,204 real held-out sentences (Project Gutenberg, VOA, USGS, NPS
and NWS text, and Tatoeba sentences under CC0; public domain in the United States or CC0) of the *test documents*, a half
of the real
held-out sentences that is split off by document and that no weight or threshold has seen (the other half, the
calibration documents, chooses the per-lemma thresholds). The written held-out sentences (12,261, written by a language
model) are easier and are shown only as a secondary column.
Neither is representative running text: the held-out sentences were found or written for specific senses with the quotas
of the tiers, so the figures measure the classifier on a balanced corpus, they say nothing about words that no model was
trained for, and an independent evaluation (SemCor for senses, the English EWT treebank for parts of speech) is still to
come. The levels of the encoder answers on the real test sentences (the thresholds aim at 95% precision of the sense
answers; `docs/build_notes/quota-fill.md` has the full tables and the sets that `evaluate` reports):

| Model | Sense (share, precision) | Group (share, precision) | Part of speech (share, precision) | Fine top-1 |
| --- | ---: | ---: | ---: | ---: |
| MiniLM | 8.1%, 92.7% | 0.8%, 88.2% | 91.1%, 95.0% | 61.7% |
| BERT base cased | 18.7%, 94.9% | 0.7%, 100% | 80.6%, 97.1% | 71.6% |

The table is generated into `docs/build_notes/model-table.md`; the user guide explains how the numbers were measured
and lists the confidence thresholds of each model. See also the [model comparison][link-model-comparison] and the
[Basic evaluation][link-basic-evaluation] (both measure the earlier part-of-speech classes and are historical).

## Documentation

- **User Guide**: usage, configuration, limitations, licensing and troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its senses, groups, OEWN keys and respellings
  ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`

## Credits and licenses

The packages that embed derived data (`DemaConsulting.WordSense`, `DemaConsulting.WordSense.MiniLM` and
`DemaConsulting.WordSense.BertBaseCased`) carry this section in their package
readme. The corpus itself is not shipped in the packages, only the derived tables and weights; every real sentence is
recorded with its source, license and URL in `corpus/provenance/`.

Package licenses:

- **`DemaConsulting.WordSense`** (core): `MIT AND CC-BY-4.0`. The code and the project-authored word data are MIT; the
  embedded word image is derived from Open English WordNet 2025 (CC BY 4.0), with the WordNet 3.0 sense counts under the
  Princeton license; the embedded statistical table of the `Basic` model is derived from our own CC0 1.0 corpora (the
  open part-of-speech corpus and the sentence corpus below). The package contains a `NOTICE.md` with the attribution
  and the notices of Open English WordNet, WordNet 3.0, AGID and the Moby list. There is no CC BY-SA material in any
  package. The former `DemaConsulting.WordSense.Basic` package is retired (before version 1.0): its model, `Basic`,
  moved into the core package.
- **`DemaConsulting.WordSense.Onnx`, `DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`**:
  `MIT`. The weights are statistical summaries of the corpus and do not contain its sentences.

- **Moby Part-of-Speech List** (Grady Ward): public domain; the embedded dictionary is derived from it.
- **Automatically Generated Inflection Database (AGID)** (Kevin Atkinson, 2000-2016,
  <http://wordlist.aspell.net/other-dicts/>): permission notice (use, copy, modify and distribute without fee with the
  notice kept). It is read at build time as one source of irregular inflected forms; the derived rows are embedded.
  AGID carries the **Princeton WordNet 1.6** notice (Copyright 1997 by Princeton University) and the **UK Advanced
  Cryptics Dictionary** notice (Copyright (c) J Ross Beresford 1993-1999); the full texts are in
  `corpus/lexicon/LICENSE-AGID.txt` and apply to the embedded forms.
- **Project-authored words, senses, meanings and respellings**: MIT License.
- **Project-authored sentences and annotations** (the sentences written by language models for this project, our
  labels, the selection of the real sentences and the open part-of-speech corpus `corpus/open`): dedicated to the public
  domain under CC0 1.0 by the project owner. The underlying real sentences are not ours; their sources and licenses
  are below.
- **Tatoeba sentences** (<https://tatoeba.org>): only the sentences of the official CC0 1.0 export are used. Tatoeba
  sentences that are licensed CC BY are not used anywhere.
- **Project Gutenberg works** (<https://www.gutenberg.org>): public domain in the United States (published 1930 or
  earlier; the Project Gutenberg boilerplate and trademark are removed).
- **Works of US federal agencies** (Voice of America Learning English staff-written articles, USGS, National Park
  Service, NOAA National Weather Service and FAA publications): public domain in the United States (17 U.S.C. §105)
  and by the policy statement of each agency; third-party passages are excluded.
- **Universal Dependencies English-EWT treebank** (<https://github.com/UniversalDependencies/UD_English-EWT>, CC BY-SA
  4.0): used to evaluate the part-of-speech tagger only. It is downloaded to a git-ignored cache, never committed or
  redistributed, and no model, table or corpus here is trained from it.
- **Wikipedia and Wiktionary text**: not used.
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
