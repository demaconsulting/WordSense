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
word starts with a capital letter. A word whose key ends in `'s` or `'` and whose analyses all carry `Poss=Yes`
(`technician's`, `sergeants'`) is a possessive token: it carries its stem as the key (the mask is that of its possessive
analyses) and the possessive flag, and `PosTagger` reads it as the stem and the possessive marker, the pattern of the
training counts, while the answer for the word is still its own single analysis at its own position. A contraction
(`it's`) or a word with an analysis that is not a possessive is not expanded. The helpers `Normalize` (the lexicon key,
empty for a token without a letter),
`Lower`, `MakeToken` and `SegmentOf` are internal; the builder uses them so training and inference segment text
identically. A small direct-mapped cache (1,024 entries of immutable values, a racing writer can only replace one
valid entry with another) keeps, per word with a single analysis, the answer and its analyses array.

The answer is a core `ClassifyResult` value: the word as written, `IsKnown`, `Analyses` (an `AnalysisCandidate` for
every analysis of the word with its probability and, for an analysis whose lemma and part of speech have exactly one
sense, that sense with probability 1, for an analysis whose lemma and part of speech have two or more senses those senses
ranked by the usage prior (see Usage Prior below), otherwise an empty senses list), the selected analysis as
`Details`, the `SenseLevel`, the confidence and the group id and sense id (only at the sense level, and the group id
only when that sense is in a group, which a sense that is the only one of its lemma and part of speech normally never
is). The
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
   confidence 1, whatever the case of the word; with several senses the analysis lists them ranked by the usage prior and
   the answer is at the part of speech level with confidence 1, because the statistics cannot choose a sense;
3. several analyses: `CueModel.Extract` gets the features of the word in its segment; `BasicTables.FindCue` looks up the
   cue table of the normalized word first (a form that spans lemmas, such as `left` or `bases`, has a table of its own)
   and then the table of the lemma of the first analysis. A table gives one cue score per analysis (`NaN` for an analysis
   it has no column for), otherwise no analysis has a cue score, which leaves the fusion to the tagger alone. The scores
   of a table are scaled by its reliability (`LemmaCue.Reliability`, the factor on the inverse temperature of the fusion),
   so a table that predicts held-out sentences of its own anchors poorly does not outvote the tagger and a table of
   reliability 0 is as good as none;
   `SenseFusion.Fuse` gives one probability per analysis, and the case of the written word and then the tense of the
   context adjust it (below). The tagger token of a word with several parts of speech carries the catalog support of its
   analyses (`WordSupport.Of`), which `PosTagger` blends into the emission of the word as its word prior.
   The classifier builds an `AnalysisCandidate` for each analysis (with the one sense of an analysis that has exactly
   one sense, or the ranked senses of an analysis that has several) and sorts them with a stable insertion sort (the
   number of analyses is small): the slots (lemma and part of speech) by the sum of the probabilities of their
   analyses, largest first, a tie keeping the lexicon order of the first analysis of the slot, and the analyses of one
   slot by their own probability, largest first, a tie keeping the lexicon order. The first is the selected analysis,
   the best variant of the most probable slot, and the probability of its slot is the confidence. If it
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
group level is never produced; the senses of an analysis are its only sense when its lemma and part of speech have
exactly one, its senses ranked by the usage prior (two or more, summing to one) when it has several, and empty when it
has none; the ranking never changes the level, the confidence, the group id, the sense id or the probability of any analysis.

`SegmentOf(words, index)` returns the start and length of the segment around a position.

#### Usage Prior

Most polysemous lemmas have no trained encoder weights, so no model scores their senses; the Basic model, to which the
encoder models fall back for such a word, ranks them instead by how often the senses are used (`UsagePrior`):

- **The prior.** The WordNet 3.0 tag count of each sense (the number of times the sense was tagged in the
  sense-annotated texts of the WordNet project; the core image carries it in its sense frequency section, one 16-bit
  count for each sense of the catalog, 0 for a project sense and for a key that WordNet 3.0 does not list) plus the
  smoothing constant `k`, divided by the sum of that over the senses of the lemma and part of speech:
  `(count + k) / sum(count + k)`. The senses are listed by probability,
  the largest first, and equal probabilities keep the inventory order (the order of Open English WordNet, which puts the
  commonly used senses first, and the project senses last), so a lemma without any count is ranked by the inventory
  order alone with the uniform probability `1/n`.
- **The constant.** `k = 1` (`UsagePrior.Smoothing`). The ranking does not depend on it, only the probabilities do; it
  was chosen on the development half of the independent all-words gold set (`corpus/allwords/dev.tsv`) as the value of a
  grid (0.01 to 5) that gives the lowest mean log loss of the gold sense, 0.9485 nats at 1 against 0.9504 at 2 and
  0.9621 at 0.5, and it is also the simplest. The test half was not used for the choice.
- **Where it applies.** To every analysis whose lemma and part of speech have two or more senses, not only to the selected
  one, so the analyses of one slot share the ranking and a consumer that sums over the analyses gets the probability
  of the slot times the probability of the sense. The classifier asks the prior for a slot with fewer than two senses never
  (a single sense is listed with probability 1 and no sense is nothing to list).
- **What it is not.** The prior is evidence about the lemma, not about the sentence: every occurrence of a word has the
  same ranking, and the context cannot change it. It is never an answer: the level stays the part of speech (or the
  sense of a single-sense analysis), the confidence stays the probability of the slot, and the sense id and the group id
  stay null, exactly as without the ranking. `ClassifyResult.Key` names the label of the top-ranked sense under its rule
  for the part-of-speech level, which is why the keys of such words end with `#label`; every such key is a member of
  `WordLexicon.Keys()`.
- **The mark.** The classifier builds the candidate of such an analysis with `SensesRankedByUsage` true, so that a consumer
  (and `FallbackWordSenseModel`) can tell the ranking from a score of the senses in context; the candidate of an analysis
  with its only sense, with no sense, or built without a ranking function has it false.
- **Cost.** The ranking of a slot is computed once and kept in a small direct-mapped cache (4,096 entries of immutable
  values, a racing writer can only replace one valid entry with another).
- **Inheritance.** `FallbackWordSenseModel` answers a word with the result of its primary model only when the primary
  model scored senses for the selected analysis; for a lemma without weights the encoder returns the lexicon-only
  analyses, so the result of this classifier, ranking included, is the answer of the MiniLM and BertBaseCased models too.

The explicit constructor of the tests takes the ranking as an optional function; without it (the default of the tests that
use a toy lexicon) an analysis with several senses lists none, as before the prior.

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

#### Tense Evidence

The senses belong to a lemma and a part of speech (a slot), so the cue tables, which are trained per lemma and per form
from sense and chooser sentences, have no column for the tense of most verbs: the past and the infinitive of `put`, `cut`
or `set` share the one column of their slot, get the same score and the same tagger posterior, and kept the lexicon
order, so the non-past analysis was always selected. The classifier therefore reads the tense from a model that is
shared by all verbs, as a prior on the split of a slot, applied after the case prior (`ApplyTense`):

- **The model** (`TenseCue`, the tense section of `basic.bin`). A naive Bayes classifier of two classes, past and
  non-past, over the hashed context features of `CueModel.ExtractTense`: the features of the cue model (the distinct
  tokens within five positions, the left and the right neighbor), the same tokens told apart by the side of the target they
  stand on, the edge of the segment as the neighbor of the first or last word (the first and last token with a letter, so
  punctuation does not count as the edge word), and the nearest function word to the left that
  carries the tense of a verb phrase (`CueModel.IsGovernor`: forms of `be`, `have` and `do`, the modals, `to`, negations
  and the contractions of them, a closed class of English grammar and not a list of verbs). The target is never a feature,
  so the model reads only the context. It is trained on every verb of the training split of the representative open
  corpus (`BasicTrainer.TrainTense`): past is `Tense=Past` (the simple past and the participle), non-past is the
  infinitive, the imperative and the present of every person but the third singular; the third singular (spelled apart from
  the past), the gerund and the present participle are left out. The weights are log likelihood ratios in eighths of a nat
  and the prior is the log odds of the classes in the training text (the natural prior).
- **Which slots.** A slot qualifies when it is a verb slot with at least one past analysis (`BasicClassifier.IsPast`:
  `Tense=Past` or `VerbForm=Part`, which is how the lexicon spells the participle; the gerund is `VerbForm=Ger`) and at
  least
  one analysis that is not past. That is the case for every verb whose past and non-past share a spelling, found in the
  lexicon and not in a word list: `put`, `cut`, `set`, `hit`, `let`, `come`, `run`, `read` and the rest.
- **The weight.** The log odds of the model are bounded to 30 nats, and so is their product with the weight (so any
  weight the table accepts keeps the exponential finite). The variants of the slot start with equal shares of
  the probability the slot had, the past variants are weighted by `exp(TenseWeight * L)` (L is the log odds of the model),
  and all probabilities of the
  slot are scaled back to the old total. The probability of the slot, and with it the level, the confidence, the group and
  the sense, never change: only the split
  inside the slot, the order of its analyses and, when it is the selected slot, the selected analysis. `TenseWeight` is
  in the header of the table (1.0, tuned on the development splits only: any weight of at least 1 gave the same accuracy
  there, and the weight 0 turns the model off).
- **Interaction with the form tables.** The cue tables of `read`, `beat`, `cast` and `upset` have their own past and
  present columns, and the fusion has already split those slots when the tense model reads them. The shared model
  replaces that split: both read the same context features, one from 26 or fewer sentences of a form and one from all
  verbs of the corpus, so multiplying them would count the context twice. This was measured on the development data
  (the development half of the independent read set, 205 sentences, and the development splits of the open corpus and
  the treebank): replacing was never worse than multiplying (one more sentence right on the read set, the same tokens
  elsewhere), so the simpler rule stays. A word without a form table is not affected, because its variants were
  tied.
- **Nothing to separate, nothing changes.** A slot without both classes, a word without a tense table, and
  `TenseWeight` 0
  leave the probabilities exactly as they are.

The decision is the tense of the meaning of the word in its sentence (`I will read that tomorrow` is a non-past `read`,
`I have read that already` a past participle): nothing about the sound of the word enters. An encoder model inherits the
split through `FallbackWordSenseModel`, which gives the tied variants of an encoder slot the proportions of this model.

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

`WordLexicon` (`Lookup`, `SensesOf` and, through `UsagePrior`, the tag counts of the image), `UsagePrior`,
`BasicTables` (including its `TenseCue`), `PosTagger`, `CueModel`,
`SenseFusion` and the core contracts (`ClassifyResult`, `WordDetails`, `SenseLevel`, `SenseCandidate`).

#### Callers

`BasicWordSenseModel.ClassifyAsync` and the builder (`BasicTrainer`, `BasicTuner` and the evaluation of `basic`).
