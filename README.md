# WordSense

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

WordSense is a .NET library that tells which sense an ambiguous English word carries in context, for example
whether "record" is a noun or a verb, or whether "bass" is a fish or an instrument. It combines the int8-quantized
`all-MiniLM-L6-v2` encoder, run with ONNX Runtime, with a compact classifier table embedded in the assembly.

## Features

- **Contextual sense decisions**: per-word senses with a `Group` (the pronunciation equivalence class of senses),
  true part-of-speech tags (noun, verb, ...),
  a confidence margin and tag-level queries such as noun versus verb, for 744 lemmas: a curated set of homographs
  and several hundred common words that are both a noun and a verb (embedded table format v3)
- **Abstention**: returns no sense instead of guessing when the evidence is weak
- **Single package**: one NuGet package, `DemaConsulting.WordSense`, whose only runtime dependency is
  Microsoft.ML.OnnxRuntime
- **Verified model files**: the model is downloaded on first use from a user mirror, Hugging Face or GitHub LFS, in
  that order, and every file is verified against a pinned SHA-256 digest
- **Fast**: a few milliseconds per sentence on a developer workstation
- **Multi-Platform and Multi-Runtime**: Windows, Linux and macOS; .NET 8, 9 and 10
- **Continuous Compliance**: compliance evidence generated on every CI run, following the
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

The examples below are checked by the test suite (`ReadmeSampleTests`): the code blocks marked as samples must equal
the compiled sample code in `test/DemaConsulting.WordSense.Tests/ReadmeSample.cs`, and the output blocks must equal
what that code prints with the real model. The samples assume `using DemaConsulting.WordSense;`. The
[user guide](docs/user_guide/introduction.md) covers the rest of the API, mirrors, limitations and troubleshooting.

First, download the model (or locate it, when it is already in the per-user store) and verify it:

<!-- sample:download -->
```csharp
// Downloads missing files (mirror, Hugging Face, GitHub LFS) and verifies each against its pinned SHA-256
WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync();
Console.WriteLine(location.Directory); // holds model_int8.onnx and vocab.txt
```

The model is stored in a per-user directory and reused on later runs. To use your own mirror, set
`WORDSENSE_MIRROR_URL` (and optionally `WORDSENSE_MIRROR_USER` with `WORDSENSE_MIRROR_PASSWORD`, or
`WORDSENSE_MIRROR_BEARER_TOKEN`), or pass a `DownloadMirror` in `WordSenseModelDownloaderOptions.Mirror`. The mirror
is tried first. `WordSenseClassifier.CreateAsync` does the same download on its own when you do not call
`EnsureModelAsync` yourself.

Next, classify a sentence (given as a list of words) and print every tag of each word; words that are unknown or that
the classifier abstains from print `-`:

<!-- sample:classify -->
```csharp
using var classifier = await WordSenseClassifier.CreateAsync(
    new WordSenseClassifierOptions { ModelDirectory = location.Directory });

string[] words = ["I", "read", "that", "the", "lead", "singer", "will", "bow", "and", "a", "tear", "will",
    "fall", "when", "she", "presents", "the", "present"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words); // parallel to words; Details is null = unknown or unsure

for (var i = 0; i < words.Length; i++)
{
    Console.WriteLine(analyses[i].Details is { } d
        ? $"{words[i],-8} lemma={d.Lemma} pos={d.PartOfSpeech} pronunciation={d.Pronunciation?.Meaning ?? "-"}"
        : $"{words[i],-8} -");
}
```

The output, as produced by the committed model:

<!-- output:classify -->
```text
I        -
read     lemma=read pos=Verb pronunciation=past
that     -
the      -
lead     lemma=lead pos=Noun pronunciation=guide
singer   -
will     -
bow      lemma=bow pos=Verb pronunciation=bend
and      -
a        -
tear     lemma=tear pos=Noun pronunciation=drop
will     -
fall     -
when     -
she      -
presents lemma=present pos=Verb pronunciation=verb
the      -
present  lemma=present pos=Noun pronunciation=noun
```

Here `lead` (guiding), `tear` (a drop) and `present` (a gift) are nouns, while `bow` (to bend) and `presents` are
verbs. `read` is the past tense. `read`, `lead`, `bow`, `tear` and `present` are heteronyms whose pronunciation
depends on the context. Each answer carries the `lemma`, the `pos` (part of speech) and, for heteronyms, the
pronunciation meaning; the [senses appendix](docs/user_guide/appendix-senses.md) lists every supported lemma.

To see the confidence behind each decision, reusing the classifier from above:

<!-- sample:tags -->
```csharp
string[] words = ["They", "will", "record", "the", "record"];
WordAnalysis[] analyses = await classifier.ClassifyAsync(words); // the full result for every word

for (var i = 0; i < words.Length; i++)
{
    var a = analyses[i];
    Console.WriteLine($"{words[i],-8} best={a.Details?.PartOfSpeech.ToString() ?? "-"} margin={a.Margin:F2} abstained={a.Abstained} runner-up={a.RunnerUp?.PartOfSpeech.ToString() ?? "-"}");
}
```

<!-- output:tags -->
```text
They     best=- margin=0.00 abstained=False runner-up=-
will     best=- margin=0.00 abstained=False runner-up=-
record   best=Verb margin=5.99 abstained=False runner-up=Noun
the      best=- margin=0.00 abstained=False runner-up=-
record   best=Noun margin=7.16 abstained=False runner-up=Verb
```

`Margin` is the score of the best sense minus the runner-up; the classifier abstains when it is below the threshold
of the lemma. A raw string can be passed to `ClassifyAsync(string)` instead of a word list.

## Accuracy and Limitations

On 12,789 held-out rows the best sense is correct 96.91 % of the time, and 98.73 % of the answered rows are correct
(90.74 % answered). That corpus is partly project-written (LLM-generated) text, so it is the optimistic figure: on 689
real, blind-judged sentences of 39 lemmas the best sense is correct 92.60 % of the time (96.40 % of the answered rows,
84.76 % answered). The noun/verb lemmas are based on real text and enter the table only when they reach 90 % on a
source-disjoint holdout; rare senses use sentences authored by the project. Words that are not in the table are never
answered (they print `-`), a sense has exactly one part of speech, forms that collide with other words (`saw`, `left`,
`found`, ...) are excluded, and the classifier abstains instead of guessing. The
[user guide](docs/user_guide/introduction.md) has the details, the caveats and troubleshooting.

## Documentation

- **User Guide**: usage, configuration, API reference, accuracy, limitations, licensing, extending the corpus and
  troubleshooting ([docs/user_guide/introduction.md](docs/user_guide/introduction.md))
- **Senses appendix**: every supported lemma with its senses, tags, groups and forms, generated from the corpus
  ([docs/user_guide/appendix-senses.md](docs/user_guide/appendix-senses.md))
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
