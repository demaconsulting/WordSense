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
"record" is a noun or a verb, and how to say "bow" or "read". It looks every word up in an embedded dictionary of over
140,000 common words and, only where a word has several meanings, asks the int8-quantized `all-MiniLM-L6-v2` encoder
(run with ONNX Runtime) to choose from the sentence, using a compact per-model weights table embedded in the assembly.
The library is split in two packages: the core `DemaConsulting.WordSense` holds the API and the dictionary and needs no
model, and `DemaConsulting.WordSense.MiniLM` adds the encoder and its weights.

## Features

- 📖 **Dictionary** - Parts of speech for over 140,000 common words, with no model call for most of them
- 🧠 **Context Aware** - Decides the part of speech of a word from its sentence
- 🔊 **Pronunciation** - Heteronyms like "bow" and "read" get a meaning id and a respelling
- 🎯 **744 Trained Lemmas** - Curated heteronyms plus common noun/verb words, decided by context
- 🤔 **Abstains** - Lists the candidates instead of guessing when unsure
- 📊 **Confidence** - Every context answer carries a margin and the runner-up
- 📦 **Two Packages** - The core has no dependencies and works without a model; the MiniLM package adds ONNX Runtime
- 🔒 **Verified Model** - Downloaded on first use and checked against pinned SHA-256 digests
- ⚡ **Fast** - A few milliseconds per sentence on a developer workstation
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence generated on every CI run, following the
  [Continuous Compliance][link-continuous-compliance] methodology

## Installation

```bash
dotnet add package DemaConsulting.WordSense
dotnet add package DemaConsulting.WordSense.MiniLM
```

`DemaConsulting.WordSense` is the core (API, dictionary, model abstraction) and works on its own, without a model or
ONNX Runtime: single-sense words are answered and every ambiguous word lists its candidates
(`WordSenseClassifier.Create()`). `DemaConsulting.WordSense.MiniLM` depends on the core and ONNX Runtime and decides
the ambiguous trained words from their sentence.

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and, for the MiniLM package, internet access on first use, or a
mirror, to download the 23 MB encoder model (it is not part of the package). Contributors who clone this repository also
need [Git LFS](https://git-lfs.com) (`git lfs install`, then `git lfs pull`), because the files in `model/` are stored
in Git LFS.

## Usage

The example below is checked by the test suite (`ReadmeSampleTests`): the code block must equal the compiled sample
code in `test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSample.cs`, and the output block must equal what that code
prints with the real model. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and
troubleshooting.

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense.MiniLM;

// Create a classifier that runs the MiniLM model, downloading and verifying the model on first use
using var classifier = await MiniLMWordSense.CreateClassifierAsync();

// Classify the words of a sentence whose "record", "read" and "bow" are heteronyms
var words = "The hunter fired an arrow from his bow so I will record a record and read the book yesterday".Split(' ');
var results = await classifier.ClassifyAsync(words);

// Print the part of speech (or the candidates, or unknown), where the answer comes from, the margin and,
// for a heteronym, the meaning and the respelling
foreach (var a in results)
{
    var pos = a.Details?.PartOfSpeech.ToString()
        ?? (a.Candidates.Count > 0
            ? string.Join("/", a.Candidates.Select(c => c.PartOfSpeech.ToString()[..3]).Distinct())
            : "unknown");
    var say = a.Details?.Pronunciation is { } p ? $"{p.Meaning} {p.Respelling}" : "";
    Console.WriteLine($"{a.Word,-9} {pos,-24} {a.Basis,-10} {a.Margin,4:F1}  {say}".TrimEnd());
}
```

<!-- output:readme -->
```text
The       unknown                  None        0.0
hunter    Noun                     Dictionary  0.0
fired     Verb                     Dictionary  0.0
an        unknown                  None        0.0
arrow     Noun                     Dictionary  0.0
from      Preposition              Dictionary  0.0
his       unknown                  None        0.0
bow       Noun                     Context     4.2  weapon boh
so        Nou/Pro/Adj/Adv/Con/Int  Dictionary  0.0
I         Pronoun                  Dictionary  0.0
will      Nou/Ver                  Dictionary  0.0
record    Verb                     Context     4.8  verb rih-KORD
a         unknown                  None        0.0
record    Noun                     Context     7.7  noun REK-urd
and       Nou/Con                  Dictionary  0.0
read      Ver                      Context     0.8
the       unknown                  None        0.0
book      Noun                     Context     7.4
yesterday Nou/Adv                  Dictionary  0.0
```

Every word is looked up in the embedded dictionary. A word with one part of speech ("hunter") is answered from the
dictionary alone (`Basis` is `Dictionary`, `Margin` 0). A word with a trained classifier ("bow", "record", "book") is
decided by the encoder from its sentence (`Basis` is `Context`) and carries a `Margin`, the confidence, and for a
heteronym a pronunciation: a `Meaning` id and a plain `Respelling` that a speech synthesizer can use. A word with
several parts of speech and no classifier ("will", "so") is not answered: `Details` is `null`, `Abstained` is `true`
and `Candidates` lists the possibilities (shortened above). The classifier also abstains from a word whose margin is
too low ("read": both of its pronunciations are verbs, so only the verb shows). Determiners such as "the" are
`unknown` (`Basis` is `None`), like numbers and capitalized words inside a sentence. The encoder runs only for a
sentence that has a word that needs it. The model is stored in a per-user directory under its model ID
(`all-MiniLM-L6-v2-int8`) and reused on later runs. To use your own mirror, set `WORDSENSE_MIRROR_URL` (optionally with
`WORDSENSE_MIRROR_USER` and `WORDSENSE_MIRROR_PASSWORD`, or `WORDSENSE_MIRROR_BEARER_TOKEN`) or pass a `DownloadMirror`;
it is tried first, and it serves the files at `{mirror}/all-MiniLM-L6-v2-int8/{file}`. The files can be copied to a
machine without internet access. See the [user guide][link-user-guide] for details and the
[senses appendix][link-appendix] for the list of lemmas.

## Accuracy and Limitations

On 12,789 held-out rows the best sense is correct 96.91 % of the time. With abstention 98.73 % of the answered rows
are correct (90.74 % answered). That corpus is partly project-written (LLM-generated) text, so it is the optimistic
figure: on 689 real, blind-judged sentences of 39 lemmas the best sense is correct 92.60 % of the time, and the part of
speech is correct 97.22 % of the time. Words that are in neither the dictionary nor the table are never answered,
forms that collide with other words (`saw`, `left`, `found`, ...) are excluded from the trained lemmas, and the
classifier abstains instead of guessing. The dictionary answers (a word with one part of speech) were measured on
Universal Dependencies English EWT: 97 % to 99.9 % precision per part of speech, and the right part of speech is among the
candidates of a multi-meaning word 97.3 % of the time; 51.8 % of the sentences there need no encoder call. The
dictionary knows plural `-s`/`-es`/`-ies` and `-ly` forms but not `-ed`, `-ing` or `-er`/`-est`, and determiners,
numbers and proper names are unknown. The
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
`all-MiniLM-L6-v2` model is licensed under the Apache License 2.0; ONNX Runtime under the MIT License. The embedded
dictionary is derived from Moby Part-of-Speech II by Grady Ward, which is in the public domain. The training
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
