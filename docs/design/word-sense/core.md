## Core Subsystem Design

![Core Structure](CoreView.svg)

### Overview

The core subsystem is the package `DemaConsulting.WordSense`. It defines the vocabulary every other package builds
on: the embedded word lexicon (the analyses of every known spelling), the sense catalog (every fine sense with its
readable id, OEWN sense key and group) and the public model interface with the answer values. It contains no vectors,
no weights, no ONNX code and no third-party dependency, so a consumer that only needs to know which senses a word can
have, or that writes its own model, installs nothing else.

The subsystem contains two units:

- **WordLexicon**: the embedded lexicon of known words and the sense catalog, with its internal blob reader.
- **WordSenseContracts**: `IWordSenseModel`, `MirrorOptions`, `WordSense`, `SenseLevel`, `WordDetails`,
  `PartOfSpeech`, `SenseInfo` and `SenseGroup`.

Boundaries: the subsystem does not classify ambiguous words in context; that is the work of the model packages. It
does not download anything; `MirrorOptions` only carries the configuration that a model package interprets.

### Interfaces

The subsystem exposes:

- `WordLexicon`: the lookup of a word in the embedded word image (`Lookup`, `TryLookup`, `Contains`, `FormsOf`) and
  the sense catalog (`SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup`);
- `IWordSenseModel`: the contract that model packages implement;
- `WordSense`, `SenseLevel`, `WordDetails` and `PartOfSpeech`: the answer values;
- `SenseInfo` and `SenseGroup`: the records of the catalog;
- `MirrorOptions`: the download mirror configuration passed to `EnsureModelAsync`.

It consumes the embedded resource `wordsense.img` and nothing else. The internals (the image reader) are visible to the
builder and to the core test project only.

### Design

`WordLexicon` opens `wordsense.img` lazily and exactly once through `WordImage`, which reads the image in place (no
dictionary is built). A lookup decodes only the analyses of the word asked for; the catalog sections (the sense ids,
the OEWN key suffixes, the group table and the key index) are decoded the first time a catalog method needs them, so a
caller that only classifies words pays nothing for the 128 thousand senses of the catalog until a model asks for the
senses of a lemma. The contracts unit has no behavior of its own beyond the value semantics of its records; it depends
on `WordLexicon` only through the documentation of the classifier contract, which defines the lookup of every word as
the common first step of every model.

The two kinds of question are kept apart. `Lookup` answers a morphological question (which analyses can this spelling
have) and never expands an analysis into its senses; `SensesOf` answers a semantic question (which senses does this
lemma and part of speech have, most frequent first). A model combines them: it looks the spelling up, then asks for
the senses of each analysis.

The units do not call each other at run time. A model package calls both: it looks up the analyses of a word, asks for
their senses, and returns `WordSense` values built from a `WordDetails`, a `SenseLevel` and the ids of the sense or
group that it chose.
