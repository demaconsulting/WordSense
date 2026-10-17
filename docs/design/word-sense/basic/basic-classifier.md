### BasicClassifier Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicClassifier` implements `IWordSenseClassifier` with the lexicon and the ordered rules: it answers unambiguous
words directly and picks the part of speech of ambiguous words from their neighbors.

#### Data Model

An internal sealed class without state. Per word it derives a normalized key (lower case, U+2019 mapped to `'`,
empty for a token without a letter) and, for ambiguous words, the previous and the next normalized word (null when
empty or absent) and the set of candidate parts of speech.

#### Key Methods

`ClassifyAsync(words, cancellationToken)` validates the arguments, checks cancellation and then works synchronously
and returns a completed task. For each word:

1. no lexicon entry: `(word, null, 0)`;
2. one entry: `(word, entry, 1.0)`;
3. several entries: `Rules.Evaluate` with the neighbors and the candidate parts of speech. If a rule fired and
   exactly one entry has the selected part of speech, the result is that entry with the rule's confidence; otherwise
   `(word, null, 0)`.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`. Words unknown to the lexicon are not errors.

#### Dependencies

`WordLexicon`, `Rules` and the core contracts.

#### Callers

`BasicWordSenseModel.Create` and the builder (`BasicPrecision`).
