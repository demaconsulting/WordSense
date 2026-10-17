## Core Subsystem Design

![Core Structure](CoreView.svg)

### Overview

The core subsystem is the package `DemaConsulting.WordSense`. It defines the vocabulary every other package builds
on: the embedded word lexicon and the public model interface. It contains no vectors, no weights,
no ONNX code and no third-party dependency, so a consumer that only needs to know which senses a word can have, or
that writes its own model, installs nothing else.

The subsystem contains two units:

- **WordLexicon**: the embedded lexicon of known words, with its internal blob reader.
- **WordSenseContracts**: `IWordSenseModel`, `MirrorOptions`, `WordSense`, `WordDetails` and
  `PartOfSpeech`.

Boundaries: the subsystem does not classify ambiguous words in context; that is the work of the model packages. It
does not download anything; `MirrorOptions` only carries the configuration that a model package interprets.

### Interfaces

The subsystem exposes:

- `WordLexicon.Words`: the read-only dictionary from lower-case surface word to its possible senses, each an immutable array;
- `IWordSenseModel`: the contract that model packages implement;
- `WordSense`, `WordDetails` and `PartOfSpeech`: the answer values;
- `MirrorOptions`: the download mirror configuration passed to `EnsureModelAsync`.

It consumes the embedded resource `words.bin` and nothing else. The internals (the blob reader) are visible to the
builder and to the core test project only.

### Design

`WordLexicon` loads `words.bin` lazily and exactly once through `WordsBlob`, which reads the compressed blob into a
frozen dictionary whose values share `WordDetails` instances. The contracts unit has no behavior of its own beyond
the value semantics of its records; it depends on `WordLexicon` only through the documentation of the classifier
contract, which defines the lookup of every word in `WordLexicon.Words` as the common first step of every model.

The units do not call each other at run time. A model package calls both: it reads `WordLexicon.Words` for the
senses of a word and returns `WordSense` values built from `WordDetails`.
