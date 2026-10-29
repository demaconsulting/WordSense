# WordSense

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

WordSense is a .NET library that identifies an English word in context, offline. For each word it returns the lemma,
the part of speech with its features and, where it can tell, the sense: an Open English WordNet (OEWN) fine sense
with a readable id such as `bank#financial-institution`, or the group of closely related senses it belongs to. Every
answer states its level (sense, group or part of speech), carries a confidence for that level and lists the candidate
analyses with their probabilities.

Every word is looked up in an embedded dictionary of over 140,000 common words. A word with several analyses is
decided from its sentence by `Basic`, a small statistical tagger in the core package, or by an ONNX encoder (`MiniLM` or
`BertBaseCased`) that scores the fine senses of the 1,404 lemmas that have a sense classifier and leaves every other
word to `Basic`.

## Features

- 📖 **Dictionary** - Parts of speech and senses for over 140,000 common words
- 🏷️ **Levels and Confidence** - Every answer says whether it is a sense, a group or a part of speech
- 🔑 **Stable Keys** - One string per answer, and the list of every key the library can return
- 📦 **Layered Packages** - A dependency-free core, optional ONNX encoder models
- 🔒 **Verified Models** - Downloads are checked against pinned SHA-256 digests
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence on every CI run ([method][link-continuous-compliance])

## Installation

```bash
dotnet add package DemaConsulting.WordSense                # dictionary, senses and the Basic model; no download
dotnet add package DemaConsulting.WordSense.MiniLM         # small and fast encoder
dotnet add package DemaConsulting.WordSense.BertBaseCased  # larger, case-sensitive encoder
```

The encoder packages bring the core package and `DemaConsulting.WordSense.Onnx`. They need ONNX Runtime and download
their model files on first use, or from a mirror. Requirements: .NET 8, 9 or 10. Contributors who clone this repository
also need [Git LFS](https://git-lfs.com).

## Quick Start

The code is compiled and its output checked by the test suite.

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense;

// Basic is in the core package and needs no download. MiniLM and BertBaseCased need their package and
// download their model files on first use.
using var model = new BasicWordSenseModel();
// using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
// using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;

var words = "The hunter fired an arrow from his bow at a deer so we will record a record then go back home".Split(' ');
var results = await model.ClassifyAsync(words);

// Print the part of speech, level, confidence and sense or group id
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var sense = result.SenseId ?? result.GroupId ?? "";
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Level,-12} {result.Confidence,4:F2}  {sense}".TrimEnd());
}
```

Output with the `Basic` model:

<!-- output:basic -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00
at        Preposition  Sense        1.00  at#preposition
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary
record    Verb         PartOfSpeech 1.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00
then      Adverb       PartOfSpeech 1.00
go        Verb         PartOfSpeech 1.00
back      Adverb       PartOfSpeech 0.99
home      Adverb       PartOfSpeech 0.51
```

The `Level` says how precisely the model answered. `Sense` is a fine sense, and also the answer for a word whose lemma
and part of speech have one sense, such as `the`. `Group` is a group of closely related senses. `PartOfSpeech` means
the model told the analysis but not the sense, and a low confidence marks a guess (`so`). `Basic` answers ambiguous
words at the part-of-speech level. The encoder models answer the words they were trained for at the sense level when
they are sure enough. The [user guide][link-user-guide] shows their output for this sentence.

## Choosing a Model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Real-text sense (share @ precision) | Running-text part of speech (open corpus test) | UD-EWT test part of speech (convention-mapped) | Independent sense evaluation (SemCor) | STE challenge set (part of speech) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Basic | 4.33% @ 71.19% | 94.62% (ambiguous 92.78%) | 92.42% (ambiguous 89.82%) | 6.59% @ 33.37% (top-1 53.95%) | 98.65% (ambiguous 97.83%) | none | 272 KiB | 12 MiB | 0.11 ms | 43 ms + 62 ms | no |
| MiniLM | 15.70% @ 95.80% | 94.61% (ambiguous 92.77%) | 92.84% (ambiguous 90.47%) | 16.03% @ 82.89% (top-1 54.71%) | 98.65% (ambiguous 97.83%) | 22 MiB | 8,785 KiB | 154 MiB | 1.73 ms | 568 ms + 82 ms | yes |
| BERT base cased | 42.42% @ 95.84% | 94.67% (ambiguous 92.86%) | 92.63% (ambiguous 90.15%) | 36.76% @ 81.28% (top-1 58.50%) | 98.20% (ambiguous 97.10%) | 104 MiB | 17,144 KiB | 307 MiB | 10.64 ms | 864 ms + 60 ms | yes |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

All three models give the same part-of-speech quality, so choose by what else you need:

- ⚡ **Basic**: no download. The part of speech of every word, the tense of verbs such as `put` and `read`, and the sense
  of a word that has one. Use it when the part of speech is enough, and as the fallback of the other two.
- 🎯 **BertBaseCased**: the model for fine senses. It commits to a sense most often. It is the largest and slowest.
- ⚖️ **MiniLM**: a small download and fast answers, mostly at the part-of-speech and group levels.

The part of speech is the dependable part of an answer; treat a sense as a candidate to check. The
[accuracy chapter][link-accuracy] explains each column and its caveats.

## Documentation

- [User guide][link-user-guide]: results, keys, model choice, configuration, limitations and troubleshooting
- [Accuracy and model choice][link-accuracy]: the measurements behind the table
- [Senses appendix][link-appendix]: every lemma with anchor sentences (1,404 of them with a sense classifier), with its
  senses, groups and OEWN keys, and the heteronyms

## Credits and Licenses

WordSense is MIT licensed. The core, `MiniLM` and `BertBaseCased` packages embed derived data and are
`MIT AND CC-BY-4.0`; each carries a `NOTICE.md` with the credits. `DemaConsulting.WordSense.Onnx` is `MIT`. No package
contains CC BY-SA material, and the corpus is not shipped.

The word data derives from **Open English WordNet 2025** (CC BY 4.0), which carries the Princeton WordNet notice. The
sense frequency counts come from **WordNet 3.0** (Princeton University) under the WordNet license. The dictionary derives
from the public-domain **Moby Part-of-Speech List**. The inflected forms, annotations and open corpus are CC0 1.0, and
the real sentences are CC0 or public domain. **SemCor 3.0** and the UD English-EWT treebank (CC BY-SA 4.0) are
evaluation sources only: never committed, redistributed or trained on. The encoder models are Apache License 2.0. The
[user guide][link-user-guide] has the full list with notices.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding standards and the
pull request process. The maintainer tools in `tools/` are described in [tools/README.md][link-tools].

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
[link-accuracy]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/accuracy.md
[link-tools]: https://github.com/demaconsulting/WordSense/blob/main/tools/README.md
