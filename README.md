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

- **Contextual sense decisions**: per-word senses with a coarser `Group`, true part-of-speech tags (noun, verb, ...),
  a confidence margin and tag-level queries such as noun versus verb, for a curated set of homographs (embedded
  table format v3)
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

## Usage

```csharp
using DemaConsulting.WordSense;

using var classifier = await WordSenseClassifier.CreateAsync(new WordSenseClassifierOptions());
string[] words = ["They", "will", "record", "the", "record"];
WordSense?[] senses = await classifier.ClassifyAsync(words); // parallel to words; null = unknown or unsure
string?[] tags = await classifier.ClassifyTagAsync(words, ["noun", "verb"]); // verb, noun for the two records
```

The first call downloads the 23 MB model into a per-user store. Set `WORDSENSE_MIRROR_URL` (and optionally
`WORDSENSE_MIRROR_USER`/`WORDSENSE_MIRROR_PASSWORD` or `WORDSENSE_MIRROR_BEARER_TOKEN`) to use your own mirror. See
the user guide for details.

## Documentation

- **User Guide**: usage, configuration and API reference
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
their source and licence in `corpus/provenance/`.

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
