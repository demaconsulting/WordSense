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

- `WordLexicon`: the lookup of a word in the embedded word image (`Lookup`, `TryLookup`, `Contains`, `FormsOf`);
- `IWordSenseModel`: the contract that model packages implement;
- `WordSense`, `WordDetails` and `PartOfSpeech`: the answer values;
- `MirrorOptions`: the download mirror configuration passed to `EnsureModelAsync`.

It consumes the embedded resource `wordsense.img` and nothing else. The internals (the image reader) are visible to the
builder and to the core test project only.

### Design

`WordLexicon` opens `wordsense.img` lazily and exactly once through `WordImage`, which reads the image in place (no
dictionary is built) and decodes only the analyses of the word asked for. The contracts unit has no behavior of its own
beyond the value semantics of its records; it depends on `WordLexicon` only through the documentation of the
classifier contract, which defines the lookup of every word as the common first step of every model.

The units do not call each other at run time. A model package calls both: it looks up the
analyses of a word and returns `WordSense` values built from `WordDetails`.
