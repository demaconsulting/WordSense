### BasicClassifier Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`BasicClassifier` is the internal helper of the Basic model with the lexicon and the statistical units: it decides
the answer level of every word, answers unambiguous words directly and, for words with several analyses, drives the
tagger, the cue model and the fusion to give every analysis its probability and select the best analysis of the most
probable lemma and part of speech.

#### Data Model

An internal sealed class holding read-only references: the statistics (`BasicTables`, or null for the embedded
table loaded on first use) and the lexicon functions (`WordLexicon` by default: `TryLookup` for the analyses, and the
sense count and `SensesOf` of a lemma and part of speech). The second
constructor is internal so the tests can use a toy table and lexicon. Per call it derives, once per word, the
lower-case token (typographic apostrophe U+2019 mapped to `'`) and, per segment, the tagger tokens. A tagger token is
fixed to the punctuation tag for a token without a letter (the other tag when the token contains a digit); otherwise
it carries the lower-case key, the bit mask of the parts of speech of the lexicon analyses of the word and whether the
word starts with a capital letter. The helpers `Normalize` (the lexicon key, empty for a token without a letter),
`Lower`, `MakeToken` and `SegmentOf` are internal; the builder uses them so training and inference segment text
identically. A small direct-mapped cache (1,024 entries of immutable values, a racing writer can only replace one
valid entry with another) keeps, per word with a single analysis, the answer and its analyses array.

The answer is a core `ClassifyResult` value: the word as written, `IsKnown`, `Analyses` (an `AnalysisCandidate` for
every analysis of the word with its probability and, for an analysis whose lemma and part of speech have exactly one
sense, that sense with probability 1, otherwise an empty senses list), the selected analysis as `Details`, the
`SenseLevel`, the confidence and the group id and sense id (only at the sense level, and the group id only when that
sense is in a group, which a sense that is the only one of its lemma and part of speech normally never is). The
classifier produces three of the four levels:

<!-- markdownlint-disable MD013 -->

| Case | Level | Details | Group and sense id | Confidence |
| --- | --- | --- | --- | --- |
| no analysis, or a token without a letter | none | null (not known, no analyses) | null | 0 |
| one analysis, one sense for its lemma and part of speech | sense | the analysis | the sense, no group | 1 |
| one analysis, several senses | part of speech | the analysis | null | 1 |
| several analyses, selected analysis with one sense | sense | the selected analysis | the sense | probability of the slot (lemma and part of speech) of the selected analysis |
| several analyses, selected analysis with several senses | part of speech | the selected analysis | null | probability of the slot of the selected analysis |

<!-- markdownlint-enable MD013 -->

#### Key Methods

`ClassifyAsync(words, cancellationToken)` validates the arguments, checks cancellation before and for every element
while it materializes the input (so a long or lazy sequence stays cancelable) and then works synchronously
and returns a completed task. An empty list gives an empty array. Otherwise it rents the token and posterior buffers
from `ArrayPool` (returned in a `finally`) and walks the list segment by segment; a segment ends after a `.`, `!` or `?`
token and the cancellation token is checked per segment. For each segment it builds the tagger tokens, calls
`PosTagger.Posteriors` and then, per word, decides the answer from the lexicon lookup of the normalized form, which
returns the analyses of the form (one per lemma, part of speech and features, never one per sense):

1. no analysis or no letter: `ClassifyResult.Unknown(word)`, with its shared empty arrays. The test is the
   presence of an analysis, not the kind of token, so a number word such as `one` or `twenty` is answered with
   details; only a token without a letter (a digit string, punctuation) is unknown;
2. one analysis: it has probability 1. The answer is built once per word and cached together with its one-element
   analyses array, which results for that word may then share (a cache hit; reference identity of arrays is not
   part of the contract, and an entry may be evicted). The sense count of its lemma and part of speech decides: with
   exactly one sense the analysis lists it with
   probability 1 and the answer is at the sense level with that sense id, no group id (unless the sense has one) and
   confidence 1, whatever the case of the word; with several senses the analysis lists no senses and the answer is at the
   part of speech level with confidence 1, because the statistics cannot choose a sense;
3. several analyses: `CueModel.Extract` gets the features of the word in its segment; `BasicTables.FindCue` looks up the
   cue table of the normalized word first (a form that spans lemmas, such as `left` or `bases`, has a table of its own)
   and then the table of the lemma of the first analysis. A table gives one cue score per analysis (`NaN` for an analysis
   it has no column for), otherwise no analysis has a cue score, which leaves the fusion to the tagger alone;
   `SenseFusion.Fuse` gives one probability per analysis, and the case of the written word then adjusts it (below).
   The classifier builds an `AnalysisCandidate` for each analysis (with the one sense of an analysis that has exactly
   one sense) and sorts them with a stable insertion sort (the number of analyses is small): the slots (lemma and
   part of speech) by the sum of the probabilities of their analyses, largest first, a tie keeping the lexicon order
   of the first analysis of the slot, and the analyses of one slot by their own probability, largest first, a tie
   keeping the lexicon order. The first is the selected analysis, the best variant of the most probable slot, and
   the probability of its slot is the confidence. If it
   has exactly one sense the answer is at the sense level with that sense (and its group, normally none), otherwise at the
   part of speech level. Analyses of one form that share a part of speech (the past and the
   present of `read`, the lemmas of `left`) have the same tagger posterior, so only the cue scores separate them; the
   analyses of one lemma and part of speech share the senses, so their split never lowers the confidence. The
   tagger posterior of the Auxiliary tag selects the Auxiliary analysis of a word, except directly after an article or
   possessive (the, a, an, my, your, his, its, our, their), where that posterior is ignored so that `the can` and
   `his will` stay nouns.

Postconditions: the result is parallel to the input; a known word has details and the level part of speech or sense, an
unknown word has no details, no analyses, the level none and confidence 0; the analyses of a known word are all listed,
ordered by slot and variant, with probabilities that sum to one; the confidence of a statistical answer is greater than
0 and at most 1; the
group level is never produced; the senses of an analysis are empty unless its lemma and part of speech have exactly one
sense.

`SegmentOf(words, index)` returns the start and length of the segment around a position.

#### Case Evidence

A form can have analyses with a capitalized lemma and with a lower-case lemma that its lower-case spelling carries
(`Polish` and `polish`, `August` and `august`); the tagger and the cues are blind to the case, so on a near tie they chose
by accident. The written case is evidence of which lemma is meant, and the classifier uses it as a prior on the
analyses, applied to the probabilities after the fusion and renormalized:

- **What a word says** (`CaseOf`, from the letters of the word as written). A word with no upper-case letter is
  `Lower`, in any position: a proper name is not written without its capital. A word whose first letter alone is upper
  case, of two letters or more, is `Capitalized`, and says something only when it is not the first word of its segment
  (the first word with a letter, so a quotation mark before it does not change that), is not in a run of capitalized
  words and the segment is not in title case. An upper-case word (`POLISH`), a single capital letter (the pronoun `I`, the
  letter `A`: spelled so in every sentence) and a mixed-case word (`McDonald`, `eBay`) say nothing.
- **Runs of capitalized words.** A `Capitalized` word is in a run when the word directly before it or directly after
  it, in its segment, is also not written in lower case; the first word of the segment never counts as that neighbor,
  because its capital is only the convention of a sentence, so `Polish` in `The Polish delegation` is isolated. A
  neighbor without a letter (a comma, a quotation mark) is not capitalized, so punctuation ends a run; an upper-case or
  mixed-case neighbor (`USA`, `iPhone`) that is not the first word counts as capitalized. A run is a name or a title
  (`State
  Department`, `United States`), where a common word is capitalized as part of it. Only the adjacent words are read: the
  small words inside a name (`Secretary of State`) break the run, which is accepted for simplicity.
- **Title-case segments** (`IsTitleCaseSegment`). A segment with at least two words that have letters, each of them
  capitalized, upper case, mixed or one of the small words of a title in lower case (`a`, `an`, `and`, `as`, `at`, `but`,
  `by`, `for`, `in`, `of`, `on`, `or`, `the`, `to`, `with`), capitalizes every word by convention, so no word of it says
  anything, a lower-case small word included. A heading is not a separate kind of input (the words arrive as a tokenized
  text): it is covered where it is a run, upper case or a title-case segment.
- **The weight.** The probabilities come from the fusion first. An analysis whose lemma starts with a capital letter
  disagrees with a `Lower` word, and one whose
  lemma starts with a lower-case letter disagrees with a `Capitalized` word; its probability is multiplied by 0.1
  (`CaseMismatchWeight`) and the probabilities of all analyses are divided by their new sum, so they still sum to one.
  The listing that follows is the usual one (slots by total probability, ties in lexicon order), so among equally
  probable analyses the first in lexicon order is selected. The constant is a prior on the writer (one chance in ten
  that the convention was broken: a name typed in lower case, a common word capitalized for emphasis or in a title), not
  a fitted value, and it is far from zero so that clear context can still outweigh it.
- **Nothing to separate, nothing changes.** When every analysis of the word agrees with the case, or none does (a name
  with capitalized lemmas only written in lower case, a capitalized word whose lemmas are all lower case), the
  probabilities are left exactly as they are: a word never loses its only reading.
- **Candidates.** The word is looked up in lower case, as every word is, so the candidates are the lemmas that the
  lower-case spelling carries. A capitalized lemma that only the exact capitalized spelling has (`Jack`, `State`, `May`,
  listed by `WordLexicon.TryLookup` for the exact spelling only, marked `Capitalization=Title`) is not a candidate. This
  was measured: looking an isolated capitalized word up as written made Basic answer the capitalized lemma `State` for
  the capitalized common word of legal and formal text (`each State`, `any State`), where the lemma is `state`, and the
  sense-level precision on SemCor fell by up to four points for Basic with about 70 more wrong sense answers; the
  run rule did not remove them because the words are isolated. The lemmas of `Polish` and `August` are carried by the
  lower-case spelling and need no such lookup.

The training is not affected (the cue tables and the tagger counts are built from lower-cased text), so the embedded
table is unchanged.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`. Words unknown to the lexicon are not errors. A missing or corrupt embedded table
surfaces as the `ModelIntegrityException` of `BasicTables.Default` on first use.

#### Determinism and Thread Safety

The only shared mutable state is the single-analysis answer cache described in the data model: its entries are
immutable and a racing writer can only replace one valid entry with another, so the results never depend on timing. The
results depend only on the input words, and every scratch buffer is local to the call. One instance may be used from
many threads at once, and the same words always give the same results.

#### Dependencies

`WordLexicon` (`Lookup` and `SensesOf`), `BasicTables`, `PosTagger`, `CueModel`, `SenseFusion` and the core contracts
(`ClassifyResult`, `WordDetails`, `SenseLevel`).

#### Callers

`BasicWordSenseModel.ClassifyAsync` and the builder (`BasicTrainer`, `BasicTuner` and the evaluation of `basic`).
