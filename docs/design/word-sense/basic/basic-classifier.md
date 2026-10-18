### BasicClassifier Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicClassifier` is the internal helper of the Basic model with the lexicon and the statistical units: it answers unambiguous
words directly and, for ambiguous words, drives the tagger, the cue model and the fusion to pick the most probable
sense with its probability.

#### Data Model

An internal sealed class holding two read-only references: the statistics (`BasicTables`, or null for the embedded
table loaded on first use) and the lexicon (`WordLexicon.View` by default). The second constructor is internal so the
tests can use a toy table and lexicon. Per call it derives, once per word, the lower-case token (typographic
apostrophe U+2019 mapped to `'`) and, per segment, the tagger tokens. A tagger token is fixed to the punctuation tag
for a token without a letter (the other tag when the token contains a digit); otherwise it carries the lower-case key,
the bit mask of the parts of speech the lexicon lists for the word and whether the word starts with a capital letter.
The helpers `Normalize` (the lexicon key, empty for a token without a letter), `Lower`, `MakeToken` and `SegmentOf` are
internal; the builder uses them so training and inference segment text identically.

#### Key Methods

`ClassifyAsync(words, cancellationToken)` validates the arguments, checks cancellation and then works synchronously
and returns a completed task. An empty list gives an empty array. Otherwise it rents the token and posterior buffers
from `ArrayPool` (returned in a `finally`) and walks the list segment by segment; a segment ends after a `.`, `!` or `?`
token and the cancellation token is checked per segment. For each segment it builds the tagger tokens, calls
`PosTagger.Posteriors` and then, per word:

1. no lexicon entry or no letter: `(word, null, 0)`;
2. one entry: `(word, entry, 1.0)`;
3. several entries: `CueModel.Extract` gets the features of the word in its segment; the cue table of the lemma of
   the first candidate (if the table has one) gives the cue scores, otherwise all cue scores are zero;
   `SenseFusion.Fuse` gives the probabilities, `SenseFusion.Winner` the index of the most probable sense (the lowest
   index on a tie), and the result is that entry with its probability. The tagger posterior of the Auxiliary tag
   selects the Auxiliary sense of a word, except directly after an article or possessive (the, a, an, my, your, his,
   its, our, their), where that posterior is ignored so that `the can` and `his will` stay nouns.

`SegmentOf(words, index)` returns the start and length of the segment around a position.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`. Words unknown to the lexicon are not errors. A missing or corrupt embedded table
surfaces as the `InvalidDataException` of `BasicTables.Default` on first use.

#### Determinism and Thread Safety

The classifier keeps no mutable state: the results depend only on the input words, and every scratch buffer is local
to the call. One instance may be used from many threads at once, and the same words always give the same results.

#### Dependencies

`WordLexicon`, `BasicTables`, `PosTagger`, `CueModel`, `SenseFusion` and the core contracts.

#### Callers

`BasicWordSenseModel.ClassifyAsync` and the builder (`BasicTrainer`, `BasicTuner` and the evaluation of `basic`).
