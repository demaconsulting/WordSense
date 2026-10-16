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
"record" is a noun or a verb, and how to say "bow" or "read". It combines the int8-quantized `all-MiniLM-L6-v2`
encoder, run with ONNX Runtime, with a compact classifier table embedded in the assembly.

## Features

- 🧠 **Context Aware** - Decides the part of speech of a word from its sentence
- 🔊 **Pronunciation** - Heteronyms like "bow" and "read" get a meaning id and a respelling
- 📖 **744 Lemmas** - Curated heteronyms plus common noun/verb words
- 🤔 **Abstains** - Returns no answer instead of guessing when unsure
- 📊 **Confidence** - Every answer carries a margin and the runner-up
- 📦 **Single Package** - Only runtime dependency is Microsoft.ML.OnnxRuntime
- 🔒 **Verified Model** - Downloaded on first use and checked against pinned SHA-256 digests
- ⚡ **Fast** - A few milliseconds per sentence on a developer workstation
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence generated on every CI run, following the
  [Continuous Compliance][link-continuous-compliance] methodology

## Installation

```bash
dotnet add package DemaConsulting.WordSense
```

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and internet access on first use, or a mirror, to download the
23 MB encoder model (it is not part of the package). Contributors who clone this repository also need
[Git LFS](https://git-lfs.com) (`git lfs install`, then `git lfs pull`), because the files in `model/` are stored in
Git LFS.

## Usage

The example below is checked by the test suite (`ReadmeSampleTests`): the code block must equal the compiled sample
code in `test/DemaConsulting.WordSense.Tests/ReadmeSample.cs`, and the output block must equal what that code prints
with the real model. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and
troubleshooting.

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense;

// Ensure the model is present, downloading and verifying it on first use
WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync();

// Create a classifier that runs the model from that location
using var classifier = await WordSenseClassifier.CreateAsync(
    new WordSenseClassifierOptions { ModelDirectory = location.Directory });

// Classify the words of a sentence whose "record", "read" and "bow" are heteronyms
var words = "The hunter fired an arrow from his bow so I will record a record and read the book yesterday".Split(' ');
var results = await classifier.ClassifyAsync(words);

// Print each word's part of speech, pronunciation (heteronyms only) and confidence (margin)
foreach (var a in results)
{
    if (a.Details is not { } d)
    {
        Console.WriteLine($"{a.Word,-9} {(a.Abstained ? "abstained" : "unknown")}");
        continue;
    }

    var p = d.Pronunciation;
    Console.WriteLine($"{a.Word,-9} {d.PartOfSpeech,-9} {p?.Meaning,-8} {p?.Respelling,-9} margin={a.Margin:F1}");
}
```

<!-- output:readme -->
```text
The       unknown
hunter    unknown
fired     unknown
an        unknown
arrow     unknown
from      unknown
his       unknown
bow       Noun      weapon   boh       margin=4.2
so        unknown
I         unknown
will      unknown
record    Verb      verb     rih-KORD  margin=4.8
a         unknown
record    Noun      noun     REK-urd   margin=7.7
and       unknown
read      abstained
the       unknown
book      Noun                         margin=7.4
yesterday unknown
```

Each word gets its part of speech and, for a heteronym, a pronunciation: a `Meaning` id and a plain `Respelling` that a
speech synthesizer can use. `Margin` is the confidence; words that are not in the table are `unknown`, and a word the
classifier is unsure about is `abstained`. Both have no `Details`. The model is stored in a per-user directory and
reused on later runs. To use your own mirror, set `WORDSENSE_MIRROR_URL` (optionally with `WORDSENSE_MIRROR_USER` and
`WORDSENSE_MIRROR_PASSWORD`, or `WORDSENSE_MIRROR_BEARER_TOKEN`) or pass a `DownloadMirror`; it is tried first, and the
files can be copied to a machine without internet access. See the [user guide][link-user-guide] for details and the
[senses appendix][link-appendix] for the list of lemmas.

## Accuracy and Limitations

On 12,789 held-out rows the best sense is correct 96.91 % of the time. With abstention 98.73 % of the answered rows
are correct (90.74 % answered). That corpus is partly project-written (LLM-generated) text, so it is the optimistic
figure: on 689 real, blind-judged sentences of 39 lemmas the best sense is correct 92.60 % of the time, and the part of
speech is correct 97.22 % of the time. Words that are not in the table are never answered, forms that collide with
other words (`saw`, `left`, `found`, ...) are excluded, and the classifier abstains instead of guessing. The
[user guide][link-user-guide] has the details, the caveats and troubleshooting.

## Documentation

- **User Guide**: usage, configuration, API reference, accuracy, limitations, licensing, extending the corpus and
  troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its parts of speech, pronunciation meanings and forms
  ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`
- **Trace Matrix**: requirements to test traceability
- **Model provenance**: `docs/build_notes/model-provenance.md`

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details. The
`all-MiniLM-L6-v2` model is licensed under the Apache License 2.0; ONNX Runtime under the MIT License. The training
corpus (not shipped in the NuGet package, only the derived vectors) contains openly licensed sentences recorded with
their source and licence in `corpus/provenance/`; rows under `CC BY-SA 4.0` keep that licence if the corpus itself is
redistributed (see the user guide).

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
