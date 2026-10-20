## Core Subsystem Design

![Core Structure](CoreView.svg)

### Overview

The core subsystem is the package `DemaConsulting.WordSense`. It defines the vocabulary every other package builds
on: the embedded word lexicon (the analyses of every known spelling), the sense catalog (every fine sense with its
readable id, OEWN sense key and, where curated, group) and the public model interface with the answer values. It
contains no vectors,
no weights, no ONNX code and no third-party dependency, so a consumer that only needs to know which senses a word can
have, or that writes its own model, installs nothing else.

The subsystem contains two units:

- **WordLexicon**: the embedded lexicon of known words and the sense catalog, with its internal blob reader.
- **WordSenseContracts**: `IWordSenseModel`, the composite `FallbackWordSenseModel`, `MirrorOptions`,
  `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`, `PartOfSpeech`, `SenseInfo` and
  `SenseGroup`.

Boundaries: the subsystem does not classify ambiguous words in context; that is the work of the model packages. It
does not download anything; `MirrorOptions` only carries the configuration that a model package interprets. The
composite `FallbackWordSenseModel` adds no knowledge of its own: it only chooses, word by word, between the results of
the two models it is given.

### Interfaces

The subsystem exposes:

- `WordLexicon`: the lookup of a word in the embedded word image (`Lookup`, `TryLookup`, `Contains`, `FormsOf`), the
  sense catalog (`SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup`) and `Version`, a string that identifies
  the sense inventory of the image;
- `IWordSenseModel`: the contract that model packages implement, and `FallbackWordSenseModel`, a composite of two models;
- `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails` and `PartOfSpeech`: the answer
  values;
- `SenseInfo` and `SenseGroup`: the records of the catalog;
- `MirrorOptions`: the download mirror configuration passed to `EnsureModelAsync`.

It consumes the embedded resource `wordsense.img` and nothing else. The internals (the image reader) are visible to the
builder and to the core test project only.

### Design

`WordLexicon` opens `wordsense.img` lazily and exactly once through `WordImage`, which reads the image in place (no
dictionary is built). A lookup decodes only the analyses of the word asked for; the analysis sections are decoded when
the image is opened, and the catalog sections (the sense ids, the OEWN key suffixes, the group tables, the
lemma and part-of-speech blocks and the key index) are decoded the first time a catalog method needs them, so a
caller that only classifies words pays nothing for the 128 thousand senses of the catalog until a model asks for the
senses of a lemma. The contracts unit has no behavior of its own beyond the value semantics of its records; it depends
on `WordLexicon` only through the documentation of the classifier contract, which defines the lookup of every word as
the common first step of every model.

The two kinds of question are kept apart. `Lookup` answers a morphological question (which analyses can this spelling
have) and never expands an analysis into its senses; `SensesOf` answers a semantic question (which senses does this
lemma and part of speech have, most frequent first). A model combines them: it looks the spelling up, then asks for
the senses of each analysis.

The units do not call each other at run time. A model package calls both: it looks up the analyses of a word, asks for
their senses, and returns `ClassifyResult` values built from the `AnalysisCandidate` list of the word (analyses with
their probabilities and, where scored, their senses), the selected `WordDetails`, a `SenseLevel`, the confidence at that
level and the ids of the sense or group that it chose.

The catalog gives group ids a namespace of their own. A sense id is `lemma#label` and is frozen; a group id is
`lemma#group:label`, so it can never equal a sense id, and the image reader and the builder reject a group id without
the `#group:` marker. Group ids are versioned per data release and are not frozen. `WordLexicon.Version` is the first
16 lower-case hexadecimal digits of the inventory hash of the image (the hash that the encoder weights and the Basic
table are bound to), so it changes whenever an analysis, a sense or a group changes; consumers compare it for equality
only, and a consumer that stores group ids saves the version with them. A group answer always leads to its members:
`ClassifyResult.GroupId` with `TryGetGroup` gives `SenseGroup.SenseIds` in inventory order.
