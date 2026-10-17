## Basic Subsystem Design

![Basic Structure](BasicView.svg)

### Overview

The Basic subsystem is the package `DemaConsulting.WordSense.Basic`. It is a word-sense model for callers that want
no model files, no download, no trained weights and no ONNX Runtime. It looks every word up in the core lexicon and
chooses the part of speech of an ambiguous word with a short ordered list of hand-written rules over its neighboring
words. It is small and fast and less accurate than the encoder models; its measured accuracy is reported in the
repository's build notes.

The subsystem contains three units:

- **BasicWordSenseModel**: the only public type; implements `IWordSenseModel`.
- **BasicClassifier**: the classifier the model creates.
- **Rules**: the rules, their word lists and their confidences.

The subsystem references the core package only.

### Interfaces

It exposes `BasicWordSenseModel` with a public parameterless constructor. `EnsureModelAsync` returns immediately and
ignores the mirror; `Create` returns a classifier. It consumes `WordLexicon.Words` and the contracts of the core
package. The classifier and rules are internal and visible to the builder (rule precision measurement) and to the
Basic test project.

### Design

`BasicWordSenseModel.Create` returns a new `BasicClassifier`. For each word the classifier normalizes the word
(lower case, typographic apostrophe straightened, tokens without a letter become empty) and applies this decision:

- No lexicon entry (or an empty key): `(word, null, 0)`.
- One entry: `(word, entry, 1.0)`.
- Several entries: the first rule of `Rules` that fires selects a part of speech; the result is the entry of that part
  of speech with the rule's confidence.

When no rule fires, or more than one entry has the selected part of speech (same-part-of-speech ambiguity, for
example two pronunciation meanings of a verb), the result is `(word, null, 0)`: the rules decide by part of speech
only and cannot choose between meanings. The classifier performs no file, network or weights access, keeps no state
and is safe for concurrent use.
