### WordLexicon Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordLexicon` provides the embedded lexicon of known English words: for every known surface form, the list of its
possible senses. Every model starts from it, and a word with one sense never needs context.

#### Data Model

- `WordLexicon.Words`: a `FrozenDictionary<string, ImmutableArray<WordDetails>>`. The arrays are immutable and are the
  very instances the reader built (no copy on lookup), so a caller can neither change nor corrupt them; none is ever
  the default array.
- Keys are lower-case surface forms, including the inflected forms of every dictionary word generated at build
  time. The senses under one key
  all belong to one lemma. Callers lower-case the word and map the typographic apostrophe (U+2019) to `'` before the
  lookup.
- `WordDetails` is non-null in `Meaning` and `Respelling` only for heteronyms (words whose pronunciation depends on
  the meaning); other words have one entry per part of speech without them.
- The internal `WordsBlob` reader parses the embedded `words.bin`: a Brotli-compressed stream with the magic `WSW1`,
  a string table, a table of distinct `WordDetails`, and the entry table that maps each key to its details, all
  integers as 7-bit varints. Equal details share one instance. Every part-of-speech value is checked against the range
  of the `PartOfSpeech` enum (derived from the enum, not a literal).

#### Key Methods

- `WordLexicon.Words` (property): returns the dictionary. The dictionary is built on first access, thread-safely and
  exactly once (`Lazy` with execution-and-publication), and is immutable afterwards.
- `WordsBlob.Read(ReadOnlySpan<byte>)` (internal): decodes the blob into the dictionary and validates every reference.

#### Error Handling

If the embedded resource is missing or the blob is corrupt (unknown magic, bad string or details reference, trailing
data, truncated stream), the first access throws `InvalidDataException`. Because the lazy value caches the failure,
every later access throws as well.

#### Dependencies

The .NET base class library and the embedded resource `words.bin`. No other WordSense unit.

#### Callers

`BasicClassifier`, `OnnxClassifier` and `WeightedLemmaTable` (all lookups), the builder (`WordsBuilder` writes the
blob; `Evaluator` and `AppendixGenerator` read it) and application code that wants the senses of a word.
