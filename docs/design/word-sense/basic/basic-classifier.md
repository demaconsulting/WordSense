### BasicClassifier Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicClassifier` is the internal helper of the Basic model with the lexicon and the statistical units: it decides
the answer level of every word, answers unambiguous words directly and, for words with several analyses, drives the
tagger, the cue model and the fusion to pick the most probable analysis with its probability.

#### Data Model

An internal sealed class holding two read-only references: the statistics (`BasicTables`, or null for the embedded
table loaded on first use) and the lexicon (`WordLexicon` by default, used for `Lookup` and `SensesOf`). The second
constructor is internal so the tests can use a toy table and lexicon. Per call it derives, once per word, the
lower-case token (typographic apostrophe U+2019 mapped to `'`) and, per segment, the tagger tokens. A tagger token is
fixed to the punctuation tag for a token without a letter (the other tag when the token contains a digit); otherwise
it carries the lower-case key, the bit mask of the parts of speech of the lexicon analyses of the word and whether the
word starts with a capital letter. The helpers `Normalize` (the lexicon key, empty for a token without a letter),
`Lower`, `MakeToken` and `SegmentOf` are internal; the builder uses them so training and inference segment text
identically.

The answer is a core `WordSense` value: the word as written, the analysis as `WordDetails` (lemma, part of speech and
features), the `SenseLevel`, the group id and sense id (only at the sense level) and the confidence. The classifier
produces three of the four levels:

| Case | Level | Details | Group and sense id | Confidence |
| --- | --- | --- | --- | --- |
| no analysis, or a token without a letter | none | null | null | 0 |
| one analysis, one sense for its lemma and part of speech | sense | the analysis | of that sense | 1 |
| one analysis, several senses | part of speech | the analysis | null | 1 |
| several analyses | part of speech | the chosen analysis | null | probability of the analysis |

#### Key Methods

`ClassifyAsync(words, cancellationToken)` validates the arguments, checks cancellation and then works synchronously
and returns a completed task. An empty list gives an empty array. Otherwise it rents the token and posterior buffers
from `ArrayPool` (returned in a `finally`) and walks the list segment by segment; a segment ends after a `.`, `!` or `?`
token and the cancellation token is checked per segment. For each segment it builds the tagger tokens, calls
`PosTagger.Posteriors` and then, per word, decides the answer from `WordLexicon.Lookup` of the normalized form, which
returns the analyses of the form (one per lemma, part of speech and features, never one per sense):

1. no analysis or no letter: level none, `(word, null, none, null, null, 0)`. The test is the presence of an analysis,
   not the kind of token, so a number word such as `one` or `twenty` is answered with details; only a token without a
   letter (a digit string, punctuation) is unknown;
2. one analysis: `WordLexicon.SensesOf` of its lemma and part of speech decides between the single-sense rule and the
   single-analysis rule. With exactly one sense the answer is at the sense level with that sense id, its group id and
   confidence 1, whatever the case of the word. With several senses the answer is the analysis at the part of speech
   level with confidence 1, because the statistics cannot choose a sense;
3. several analyses: `CueModel.Extract` gets the features of the word in its segment; the cue table of the lemma of the
   first analysis (if the table has one) gives one cue score per analysis, otherwise all cue scores are `NaN`, which
   the fusion treats as missing; `SenseFusion.Fuse` gives one probability per analysis and `SenseFusion.Winner` the
   index of the most probable analysis (the lowest index on a tie). The answer is that analysis at the part of speech
   level with its probability as confidence. Analyses of one form that share a part of speech (the past and the
   present of `read`, the lemmas of `left`) have the same tagger posterior, so only the cue scores separate them. The
   tagger posterior of the Auxiliary tag selects the Auxiliary analysis of a word, except directly after an article or
   possessive (the, a, an, my, your, his, its, our, their), where that posterior is ignored so that `the can` and
   `his will` stay nouns.

Postconditions: the result is parallel to the input; a result with details has the level part of speech or sense, a
result without details has the level none and confidence 0; the confidence of a statistical answer is greater than 0
and at most 1; the group level is never produced.

`SegmentOf(words, index)` returns the start and length of the segment around a position.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`. Words unknown to the lexicon are not errors. A missing or corrupt embedded table
surfaces as the `InvalidDataException` of `BasicTables.Default` on first use.

#### Determinism and Thread Safety

The classifier keeps no mutable state: the results depend only on the input words, and every scratch buffer is local
to the call. One instance may be used from many threads at once, and the same words always give the same results.

#### Dependencies

`WordLexicon` (`Lookup` and `SensesOf`), `BasicTables`, `PosTagger`, `CueModel`, `SenseFusion` and the core contracts
(`WordSense`, `WordDetails`, `SenseLevel`).

#### Callers

`BasicWordSenseModel.ClassifyAsync` and the builder (`BasicTrainer`, `BasicTuner` and the evaluation of `basic`).
