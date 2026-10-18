### WordLexicon Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordLexicon` provides the embedded lexicon of known English words: for every known spelling, the list of its
possible analyses. Every model starts from it, and a word with one sense never needs context.

#### Data Model

- The lexicon is the embedded flat word image `wordsense.img`, read in place by the internal `WordImage`. Nothing is
  expanded into a dictionary: a lookup is a binary search over a UTF-8-byte-sorted spelling index followed by a decode
  of the 8-byte analysis rows of that spelling.
- Image layout: an 80-byte header (magic `WSI1`, version, SHA-256 of the data it was built from, SHA-256 of its
  content), a section directory and 21 sections (spelling index and bytes, analysis rows, scope lists, entries, classes,
  class meanings, lemma-to-forms CSR and the fold index). Each section is Brotli-compressed after a byte-plane shuffle,
  or stored when that is not smaller.
- `WordDetails(SenseId, Lemma, PartOfSpeech, Features)`: `SenseId` is non-null only for an analysis tied to a sense
  class (heteronyms); `Features` is the inflection as `Key=Value` pairs joined by `+` (for example
  `Person=3+Tense=Pres`), or empty.
- Lookup tries the exact spelling first, then the lower-cased spelling (U+2019 maps to `'`) through the fold index,
  so case-sensitive entries win over the folded form.
- The legacy accessors for the shipped model weights (`View`, `MeaningOf`, `TrainedLemmaOf`, `ClassesOf`) are removed: the
  weights are keyed by class id and resolved against the image at load.

#### Key Methods

- `WordLexicon.Lookup(string)` and `TryLookup(string, out ImmutableArray<WordDetails>)`: the analyses of a word.
- `WordLexicon.Contains(string)`, `FormsOf(string lemma)` and `Count`.
- The image is opened lazily, thread-safely and exactly once (`Lazy` with execution-and-publication). A small
  direct-mapped cache of recent answers backs `TryLookup`, which is what the models call.
- Spelling order decision: the sort order of the spelling index (UTF-8 byte order) is not re-checked at load. Checking
  338K spellings would add work to every process start, and the content SHA-256 already verified by `WordImage.Open`
  covers the index bytes, so a committed image that opens cannot be mis-ordered unless the builder wrote it so. The
  full order check at every index therefore runs as a test (`ImageTests`) against the committed image.

#### Error Handling

If the embedded resource is missing or the image is corrupt (wrong magic or version, content hash mismatch, invalid
section or reference), the first access throws `InvalidDataException`. Because the lazy value caches the failure,
every later access throws as well.

#### Dependencies

The .NET base class library and the embedded resource `wordsense.img`. No other WordSense unit.

#### Callers

`BasicClassifier`, `OnnxClassifier` and `WeightedLemmaTable` (all lookups), the builder (`ImageCompiler` writes the
image; `Evaluator` and `AppendixGenerator` read it) and application code that wants the analyses of a word.
