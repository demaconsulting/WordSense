# The reviewed suite

A hand-reviewed set of 364 sentences about common ambiguous words and verbs whose past and non-past share a spelling,
kept as a regression test of the three models and as
a readable record of what each model gets wrong. It is **not** a measure of accuracy on running text (the sentences were
chosen to be hard for the words they test); the headline figures are the real-text sense evaluation, SemCor and the
running-text part-of-speech evaluation of the root `README.md`.

## Provenance and license

The first 348 sentences (and their notes) were written for the project by language-model agents under the project's
instructions, then reviewed one by one by a separate reviewer agent (all 348 rows: verdict `OK`, the context gives the
braced word exactly the expected reading; no row needed a changed label). The 16 rows of the list `T` (the tense rows,
below) were written by hand by the developer who added the tense model, each with a sentence whose context says the tense
without doubt, and are not in the corpus (`check-leakage`). They are project-authored content dedicated
to the public domain under CC0 1.0 by the project owner, like every project-written row of the corpus. No sentence is
taken from a third-party text. The expected labels were written and reviewed before any model was run on the suite; the
expectation file is, in contrast, a measurement of the models at the time it was written (the first measurement had Basic,
MiniLM and BertBaseCased at 42.24%, 91.67% and 94.25% on the "fine" measure).

The suite is evaluation data: no training step reads it, and the leakage check (`WordSense.Builder check-leakage`)
verifies that no sentence of it is in the anchors, the held-out sentences, the real-text rows, the open corpus (the
training, development and test splits and the challenge set) or an example sentence of Open English WordNet.

## Files

<!-- markdownlint-disable MD013 -->

| File | Content |
| ---- | ------- |
| `suite.tsv` | The rows: `id`, `list`, `lemma`, `analysis`, `senses`, `target_index`, `sentence`, `note` |
| `expectations.tsv` | For each row and model, the strongest outcome the model is expected to keep: `fine`, `answer` or `miss` |
| `ste-floors.tsv` | The floor of each model on the challenge set (accuracy on all and on the ambiguous tokens, in percent) |

<!-- markdownlint-enable MD013 -->

### `suite.tsv`

- `id` is unique (`H-use-1`); `list` is `H` (132 rows: the 30 most frequent heteronyms of the old pronunciation
  inventory), `M` (124 rows: 62 words that are used as two or more parts of speech, two sentences per part of speech),
  `C` (92 rows: 20 classic heteronyms such as `bow`, `wound`, `polish`) or `T` (16 rows: eight verbs whose past and
  non-past share a spelling, a non-past and a past row each; see "Same-spelling tense rows" below).
- `analysis` is what the selected analysis of the target word must be: `lemma/Part` or `lemma/Part/Feature=Value+...`.
  The lemma is that of the lexicon, so `wound` (the past of `wind`) is `wind/Verb`, and `used` is `use/Verb`.
- `senses` are the acceptable fine senses of the row, separated by `|`; it is empty for a row that only asks for the
  analysis.
- `sentence` is the words separated by single spaces (punctuation as separate words) with the target word in braces, and
  `target_index` is the zero-based index of the braced word.
- `note` is the note of the reviewer (the original text). The notes of the heteronym rows speak of the former
  pronunciation-class ids (`polish#adj`, `live#adv`); they are kept as written because they explain the reading of the
  sentence, and the `senses` column is the current label.

## Grading

The grading rule is `SuiteFile.Grade` (`tools/WordSense.Builder/Suite.cs`, compiled into the three test projects, so the
builder and the tests grade alike). A model's answer is judged **at the level it answered**:

- a row with acceptable senses: right at the **sense** level when the answered sense is one of them, at the **group**
  level when the answered group contains one of them, at the **part-of-speech** level when the selected analysis has the
  lemma, the part of speech and the features of the row; no answer (an unknown word) is wrong;
- a row without acceptable senses: right when the selected analysis has the lemma, the part of speech and the features
  of the row, whatever the level.

The **grade** of a row is `fine` when the answer is right and, for a row with acceptable senses, the most probable
ranked sense is one of them; `answer` when the answer is right at its own level but the top-ranked sense is not
acceptable (or the model ranks no sense); `miss` otherwise. A model that answers at the part-of-speech level and ranks
no senses for a word can reach `answer` but never `fine` on a row with acceptable senses. The Basic model ranks the senses
of every analysis with two or more senses by usage (the usage prior, never an answer), so it can reach `fine` when the
most frequent sense is an acceptable one; that is a base rate and not a decision of the context, which is why the fine
rate of the Basic model stays below that of the encoder models.

## Conversion from the pronunciation classes

The suite was written when the expected label of a heteronym row was a pronunciation class (`live#adj`, `wound#past`)
and the expected label of a word-class row was a part of speech. The conversion to the fine-sense scheme was done once,
from the class-to-sense map of the relabeling (kept in the git history, commit `3f9cafe`, `corpus/migration`):

<!-- markdownlint-disable MD013 -->

| Kind | Rows | Conversion |
| ---- | ---: | ---------- |
| `M` rows, part of speech only | 124 | `analysis` = the word's lemma and the part of speech; no senses |
| class with fine senses | 212 | `senses` = the fine senses the class maps to (current ids); 40 rows had a single sense and 172 an acceptable set; 132 of the sets were later narrowed to one sense and 13 to two senses (one of them, `H-estimate-3`, already accepted exactly those two), so 172 rows now have a single sense, 21 a set of two senses and 19 a larger set |
| `used` rows (habitual `used to`, ordinary past) | 4 | `use/Verb`; habitual = `use#used-to` (2 rows), ordinary past = every other verb sense of `use` (2 rows, a set at the conversion, later narrowed to `use#apply`) |
| entry classes (`read` past or present, `wound` and `dove` as past tenses) | 8 | `analysis` only: `read/Verb/Tense=Past`, `read/Verb/Tense=Pres`, `wind/Verb`, `dive/Verb`; no senses |

<!-- markdownlint-enable MD013 -->

**Rows that no longer have a single right answer:** the conversion left 174 rows with an acceptable set (172 classes and
the 2 ordinary `used` rows): pronunciation classes that spread over several fine senses (the class `lead#guide-noun`
holds fifteen senses). A tighter label was then given to 147 of them (see "One-sense labels for the set rows" below):
134 rows (132 classes and the 2 ordinary `used` rows) have a one-sense label, 13 rows a set of the two senses that a
third reviewer found equally right (`H-use-1`, `H-contact-1`, `H-increase-1`, `H-subject-2`, `H-estimate-1`,
`H-estimate-3`, `H-associate-1`, `H-detail-1`, `H-defense-1`, `C-produce-3`, `C-excuse-3`, `C-wound-2` and
`C-buffet-2`; the set of `H-estimate-3` was already these two senses, so its cell is unchanged and 146 cells changed).
The other 27 rows keep their full acceptable set (`H-address-1`, `H-project-1`, `H-project-2`,
`H-increase-2`, `H-record-2`, `H-inside-1`, `H-update-3`, `H-update-4`, `H-associate-2`, `H-separate-3`, `H-present-5`,
`H-present-6`, `H-august-3`, `H-conflict-1`, `C-object-3`, `C-tear-5`, `C-tear-6`, `C-console-2`, `C-desert-3`,
`C-bass-1`, `C-bass-2`, `C-bass-3`, `C-buffet-3`, `C-buffet-4`, `C-bow-2`, `C-invalid-1` and `C-invalid-2`). All 40 rows
with a set are scored as above: a sense answer inside the set, a group answer that has a sense of the set, or a
part-of-speech answer with the right analysis. A later review may narrow the 27 rows too; it does not change the
expectation mechanism. The 8 analysis-only heteronym rows are scored by analysis and features (the tense of `read` is a
feature of the analysis, not a sense).

**Decisions made in the conversion** (also listed in the commit): the capitalized entries (`August`, `Polish`) have
their own sense ids in the catalog (`August#noun`, `Polish#adjective`, `Polish#noun`) and are expected as such for the
capitalized rows; `polish#refinement` (a noun that is pronounced like the shoe polish) is accepted with the shine and
the polishing agent; the `Defense#noun` entry (the Department of Defense) that the old class merged with the common
noun is not an acceptable sense of the `defense` rows. The sentences were not changed.

## Expectations and the policy

`expectations.tsv` records, for every row and model, the strongest grade that the model reached when the file was
written. `BasicSuite_ExpectedPasses_StillPass`, `MiniLMSuite_ExpectedPasses_StillPass` and
`BertBaseCasedSuite_ExpectedPasses_StillPass` (the test projects of the three models) fail when a row grades below its
expectation, so a regression fails the build while the known misses stay visible and documented in one file.

**Policy.**

1. An expectation is **raised** in a reviewed commit when a model improves (`WordSense.Builder evaluate-suite` prints
   how many rows are better than their expectation).
2. An expectation is **never lowered to hide a regression**: a failing row is fixed in the model or its data, or the
   change that caused it is reverted. A row may be lowered only when the *row itself* is found to be wrong (a mislabeled
   sentence or an unacceptable sense in the set); that correction changes `suite.tsv` and is reviewed as such, with the
   reason in the commit.
3. A model that is retrained (a final retraining before a release, a new model) writes a new file with
   `evaluate-suite --write-expectations corpus/suite/expectations.tsv --overwrite true`, and the diff of the file is
   reviewed row by row: every row that goes down is explained, every row that goes up is welcome.
4. A row whose grade differs between the CI platforms is **platform-sensitive**: the int8 encoder arithmetic differs
   slightly between CPUs (instruction sets) and operating systems, which can flip a near tie. Such a row is recorded at
   the weakest grade it reaches on any CI platform and listed under *Platform-sensitive rows* with the platforms; this
   is a property of the arithmetic, not a regression.

### Platform-sensitive rows

| Row | Model | Grade on the development machine | Grade on CI (Windows x64, macOS arm64) | Recorded |
| --- | --- | --- | --- | --- |
| `C-content-6` (`A small raise will not content the angry workers`) | BertBaseCased | fine | miss | miss |
| `H-defense-1` (`The lawyer prepared a strong defense`) | BertBaseCased | fine | answer (Windows x64) | answer |

### Deliberate model change: the usage prior (historical; retraining of both encoder models)

This section describes the retraining with the usage prior, on the corpus of that time, and is kept as history; the
current models are described in the next section. Both encoder models were retrained with the usage prior (the scores
are corrected from the proportions of the balanced
training sentences to the usage of the senses; see the user guide chapter "Accuracy and Model Choice"), and the
expectation file was
rewritten with `evaluate-suite --write-expectations` under rule 3. The change is a deliberate trade measured on
independent text, not a hidden regression: on SemCor (human-annotated running text) the fine top-1 of MiniLM rose from
47.5% to 53.8% and that of BertBaseCased from 56.9% to 60.9%. The Basic model is unchanged. Seven cells changed
(348 rows; MiniLM 91.67% to 91.09% fine and 93.68% to 93.39% answered; BertBaseCased 94.25% to 94.54% fine, 95.69% answered
as before). Each row was checked against the ranked senses the model now gives:

<!-- markdownlint-disable MD013 -->

| Row | Model | Before | Now | Why (a trade-off of the prior, not a defect) |
| --- | --- | --- | --- | --- |
| `H-second-5` (`I second the motion`) | MiniLM | fine | miss | The encoder chooses the part of speech from the mass of the senses of each part of speech. The noun senses of `second` carry more usage than the verb sense `second#support`, so the noun reading now wins (0.59 against 0.31) although the context is a verb. |
| `H-detail-4` (`Her letter details the problems`) | MiniLM | fine | miss | The noun sense `detail#fact` has the larger count and the noun mass rises from about 0.40 to 0.64 against the verb senses. |
| `H-conflict-3` (`the dates and the exam conflict`) | MiniLM | fine | miss | `conflict#incompatibility` has one anchor, so dividing out its small training share lifts its score the most; the noun mass rises from about 0.19 to 0.54. This is the effect of senses that still lack anchors (see the quota deficits in `coverage/README.md`). |
| `C-bass-1` (`hooked a huge bass near the dock`) | MiniLM | fine | answer | The part of speech and the fish reading as a group are still right, but the top-ranked sense is now `bass#pitch` (0.39) before `bass#percoid` (0.33): the three fish senses split their mass while the musical sense has the larger count. |
| `C-bow-3` (`The students bow to their teacher`) | MiniLM | miss | fine | The verb reading is now chosen (0.81 against 0.19 for the noun). |
| `H-present-2` (`The present owner`) | MiniLM | miss | fine | The adjective reading is now chosen (0.65 against 0.34 for the noun). |
| `C-tear-3` (`a small tear in the sleeve`) | BertBaseCased | answer | fine | The top-ranked sense is now `tear#torn-opening`. |

<!-- markdownlint-enable MD013 -->

The three MiniLM rows that went to `miss` are verb readings of lemmas whose noun senses are more frequent and the
encoder's own part-of-speech choice was close to even (0.54 to 0.64 confidence); two other close rows moved the other way.
The net effect on the MiniLM suite is two rows fine and one row answered (of 348), against a gain of six points
of fine top-1 on SemCor. The expectation of these rows is the measurement of the retrained model; if a later change
improves them, the policy raises the expectation again.

### Retraining after the quota waves 4u and 4r (all three models)

The corpus grew by the tier waves `4u` and `4r` (about 14,500 anchor and 5,100 held-out rows; see `../README.md`), and
Basic (its cue tables are trained from the anchors), MiniLM and BertBaseCased were retrained (calibration target 0.97).
The expectation file was rewritten with `evaluate-suite --write-expectations` under rule 3. `C-content-6` (listed
above) and `H-close-4` stay `miss` for BertBaseCased as in the previous file: the rewrite measured `fine` on the
development machine, but both are platform-sensitive near ties (`H-close-4` has not been verified on the CI platforms).
Twenty cells changed against the previous file (348 rows; 11 went up, 9 down). Measured answered / fine:
Basic 94.25% / 42.82%, MiniLM 93.39% / 91.67%, BertBaseCased 96.84% / 95.69%. Basic changed too: its cue tables were
retrained from the new anchors, and its choice between two analyses of one form decides the lemma and the part of
speech of all three models.

Rows that went up: `H-live-5` (Basic and BertBaseCased miss to fine), `H-live-6` (BertBaseCased miss to fine),
`H-update-3` (BertBaseCased answer to fine), `H-defense-3` and `H-defense-4` (MiniLM and BertBaseCased answer to fine),
`C-tear-3`, `C-tear-4` and `C-bow-2` (MiniLM answer to fine).

Rows that went down (nine cells), each measured with the models before (the expectation file's models) and now. None
of the new anchors of these lemmas was found to be mislabeled: the rows of the new senses agree with their glosses, and
none was removed. The scores are the probabilities of the analyses and (for the encoders) the ranked senses of the
target word.

<!-- markdownlint-disable MD013 -->

| Row | Model | Before | Now | Measured reason |
| --- | --- | --- | --- | --- |
| `C-resume-2` (`He sent out ten resumes last week`) | Basic | answer | miss | Near tie of the two analyses of `resumes`: the verb now has 0.54 and the noun 0.46 (before: noun 0.69, verb 0.31). The anchors of `resume` grew from 41 to 55 verb rows (`resume#assume-again` +9, `resume#summarize` +5) and from 14 to 24 noun rows, but the new noun rows are all `resume#sketch` (a summary), none in the curriculum-vitae reading of the row. |
| `C-content-5` (`They had to content themselves with cold sandwiches`) | MiniLM | fine | miss | The adjective now wins the part of speech: `content#satisfied` 0.56 against the verb senses 0.39 (`content#satisfy-partly` 0.32, `content#satisfy` 0.07); before the verb had 0.67 against 0.30. Six senses of `content` had no anchor before; the new `content#satisfy` (10 anchors, verb, "make content") shares the verb mass and the new `content#contentedness` (10 anchors) is close to the adjective. |
| `C-polish-3` (`speaks fluent Polish`) | Basic, MiniLM, BertBaseCased | fine | miss | Near tie between the analyses `Polish/Noun` and `polish/Noun` (the lemma `Polish` has no anchors): now `polish/Noun` 0.517 against 0.471 (before `Polish/Noun` 0.523 against 0.462). The lemma `polish` grew from 34 to 60 anchors (`polish#smooth-shine` +9, `polish#refine` +7, `polish#refinement` +6, `polish#polish-refine` +4), which tips the tie; the three models share the analysis of Basic. |
| `C-polish-6` (`silver polish in small tins`) | Basic, MiniLM, BertBaseCased | answer | miss | The same tie in the other direction: now `Polish/Noun` 0.444 against `polish/Noun` 0.431 (before `polish/Noun` 0.449 against 0.435), so the answer is `Polish#noun`, the wrong lemma. A swing of 0.014 decides the grade; this row belongs to the known `polish` misses below. |
| `C-bow-3` (`The students bow to their teacher`) | MiniLM | fine | answer | The part of speech (verb 0.82, before 0.74) is still right, but the top-ranked sense is now `bow#play-bow` (0.57; "play on a stringed instrument with a bow") before `bow#accede` (0.12) and `bow#bow-down` (0.12); before `bow#bow-down` had 0.48. `bow#play-bow` is new (5 written anchors, e.g. "The cellist bowed a mournful melody"), and the competing verb senses gained `bow#accede` +9 and `bow#waist-bend` +5. |

<!-- markdownlint-enable MD013 -->

The new rows of `resume#summarize` (the archaic "give a summary of", for example the written "The analyst resumed the
quarterly figures") are unusual sentences but carry the sense of their gloss. The grades above are the measurement of
the retrained models; if a later change improves them, the policy raises the expectation again.

### The case of the written word (all three models, no retraining)

`BasicClassifier` now uses the case in which a word is written to choose between a capitalized and a lower-case lemma
that the lower-case spelling carries (see `docs/design/word-sense/core/basic-classifier.md`, "Case Evidence"): a
lower-case word weighs the analyses of capitalized lemmas (`Polish`, `August`) down to a tenth, and an isolated
capitalized word inside a sentence weighs the lower-case lemmas down to a tenth; a sentence-initial capital, a
capitalized word next to another capitalized word (`State Department`), an upper-case or mixed-case word and the words
of a title-case segment say nothing.
This is a rule of the classifier, not a retrained table, so `basic.bin` and the weights of the encoder models did not
change. The encoder models answer these words through Basic (a form of several lemmas without a chooser, or a lemma
without weights, is answered by their fallback). The expectation file was rewritten with
`evaluate-suite --write-expectations` under rule 3 and the rule 4 rows restored (`C-content-6` and `H-close-4` stay
`miss` for BertBaseCased: the rewrite measured `fine` for both on the development machine, see the platform-sensitive
rows). Measured answered / fine (348 rows) before and after: Basic 94.25% / 42.82% to 95.69% / 43.39%, MiniLM
93.39% / 91.67% to 94.83% / 92.24%, BertBaseCased 96.84% / 95.69% to 98.28% / 96.26%.

A first version also looked an isolated capitalized word up as written, which adds the capitalized lemmas that only the
exact spelling has (`State`, `May`, `Jack`). It gave the same suite result and was dropped: on SemCor it made all three
models answer about 70 more words at the sense level, all wrong (Basic sense precision 68.84% to 64.51%), because legal
and formal text capitalizes the common word (`each State`, `any State`) and SemCor annotates the lemma `state`. The run
rule (a capitalized word next to another capitalized word) did not remove them (64.68%), because the words are isolated.
With the lemmas that the lower-case spelling carries only, SemCor is back at its previous figures (Basic 3.25% @ 68.91%,
MiniLM 8.33% @ 88.71%, BertBaseCased 26.87% @ 86.63%).
Fifteen cells changed (five rows, each for all three models); none went down:

<!-- markdownlint-disable MD013 -->

| Row | Models | Before | Now | Why |
| --- | --- | --- | --- | --- |
| `C-polish-1` (`The Polish delegation arrived in Warsaw late on Monday`) | Basic, MiniLM, BertBaseCased | miss | fine | The capitalized word inside the sentence now has the capitalized lemma `Polish`; the adjective (0.55) wins against the noun (0.35) and the lower-case `polish` (0.10). |
| `C-polish-3` (`My neighbor speaks fluent Polish and a little German`) | Basic, MiniLM, BertBaseCased | miss | fine | The near tie `polish/Noun` 0.517 against `Polish/Noun` 0.471 is now `Polish/Noun` 0.89 against `polish/Noun` 0.10. This is the row that the retraining after the quota waves 4u and 4r lowered (see above); the cause was the missing evidence of the case, not the anchors. |
| `C-polish-6` (`The shop sells silver polish in small tins`) | Basic, MiniLM, BertBaseCased | miss | answer | The lower-case word now prefers the lemma `polish`: `polish/Noun` 0.747, `polish/Verb` 0.171, `Polish/Noun` 0.077 (before `Polish/Noun` 0.444 against `polish/Noun` 0.431). The row asks for a sense and Basic answers at the part-of-speech level, so `answer` is the most it can reach. |
| `H-august-3` (`The old professor had an august presence that quieted the room`) | Basic, MiniLM, BertBaseCased | miss | answer | The lower-case word has the adjective `august` (0.84) against the month `August` (0.16). |
| `H-august-4` (`The ceremony took place in the august halls of the old parliament`) | Basic, MiniLM, BertBaseCased | miss | answer | The same: the adjective wins. |

<!-- markdownlint-enable MD013 -->

The rows that stay misses for the case words: `C-polish-2` (`a traditional Polish soup`) has the capitalized lemma now
(`Polish/Noun` 0.64 and `Polish/Adjective` 0.28) but the tagger prefers the
noun between an adjective and a noun, which is a part-of-speech error, not a lemma error. The case does not change the
other rows of the suite (`C-polish-4`, `C-polish-5`, `C-polish-7` and `C-polish-8`, `H-august-1` and `H-august-2` keep
their grades).

### Same-spelling tense rows and the tense model (all three models, no retraining of the encoders)

`BasicClassifier` now reads the tense of a verb whose past and non-past share a spelling from the context (see
`docs/design/word-sense/core/basic-classifier.md`, "Tense Evidence"), and `FallbackWordSenseModel` gives the encoder
models the split of Basic for the variants of a slot that the encoder gave the same probability. The encoder weights and
`wordsense.img` did not change; `basic.bin` was retrained (format `WSB4`). Two changes of this file:

- **One expectation raised** under rule 1: `H-read-3` (`Tomorrow I will {read} the whole report before the meeting`, the
  present tense) goes from `miss` to `fine` for MiniLM and BertBaseCased (it was `fine` for Basic): both encoders used
  to select the past `read` for every sentence, because the past was listed first and the two variants tied at 0.50.
  The other three cells that measure better than the file on the development machine are the rule 4 rows and stay
  as recorded: `H-close-4` (`miss`), `C-content-6` (`miss`) and `H-defense-1` (`answer`) for BertBaseCased.
- **Sixteen reviewed rows** of the new list `T` (the file has 364 rows): a non-past and a past row for each of `read`, `put`,
  `cut`, `set`, `hit`, `let`, `spread` and `come` (`I will {read} that tomorrow` and `I have {read} that already`,
  `Please {put} the books on the shelf` and `She had {put} the keys in her bag before she left`, and so on). They are
  analysis-only rows (the lemma, the part of speech and the tense feature of the analysis, no sense), written by hand for
  this change with a context that says the tense without doubt (a modal, do-support, a form of `have`, an imperative, a
  past-time adverb), and are not in the corpus: `check-leakage` finds no shared sentence. The expected grade is `fine`
  for all three models. Before the tense model all three models graded 8 of the 16 rows `fine` and 8 `miss` (the past
  rows, and `I will read that tomorrow`, where the encoders picked the past), and now every row is `fine`.

Measured answered / fine on the 348 older rows before and after: Basic 95.69% / 43.39% unchanged, MiniLM 94.83% / 88.51%
to 95.11% / 88.79%, BertBaseCased 98.28% / 93.68% to 98.56% / 93.97%; on all 364 rows: Basic 95.88% / 45.88%, MiniLM
95.33% / 89.29%, BertBaseCased 98.63% / 94.23%. The `T` rows are easy by construction (a context that decides), so they
are a regression guard for the tense and not a measure of accuracy; the measures are in "Tense of Same-Spelling Verbs" of
`docs/user_guide/accuracy.md`.

### Deliberate model change: part-of-speech reconciliation of MiniLM (rule 3, no retraining)

`FallbackWordSenseModel` now combines the slot probabilities of a primary result that is at the part-of-speech level
with those of Basic (`p_primary^w × p_fallback^(1−w)`), and `MiniLMWordSenseModel` passes the primary weight
`w = 0.6` (BertBaseCased passes 1, so it is unchanged: the composition without a weight). The weight is the optimum of
the development splits of the running-text evaluation (see `docs/design/word-sense/mini-lm/mini-lm-word-sense-model.md`).
The encoder weights, the word image and `basic.bin` did not change in this change, and an answer at the sense or group level
is never touched (the outcome file of `evaluate` for MiniLM has every sense and group row identical before and after).
The expectation file was rewritten with `evaluate-suite --write-expectations` under rule 3 and the rule 4 rows restored
(`H-close-4` and `C-content-6` stay `miss` and `H-defense-1` stays `answer` for BertBaseCased: the rewrite measured
`fine` for the three on the development machine). The change is a deliberate trade measured on independent text, not a
hidden regression. Figures of the final build (all four changes together, with the primary weight 1 against 0.6 for
MiniLM): on the treebank test file MiniLM gains 0.27 points (91.88% to 92.15%) and on the open corpus test split 0.24
points (93.81% to 94.05%); on the branch of the change alone the gains were 0.24 points on both, and 34 of the 42 verb
`use` tokens that MiniLM called a noun were repaired. On that branch six cells changed against main (364 rows): five went
up and one went down; Basic and BertBaseCased did not change. **In the final build the cell that went down,
`M-change-4`, is `fine` again** (the clause state of the tagger, next section, repairs it), so the net effect of the
reconciliation is five MiniLM rows up and none down. Measured answered / fine on all 364 rows of the final build, MiniLM
with the weight 1 and with the weight 0.6: 95.33% / 90.66% to 96.70% / 92.03% (list `H`: 93.94% / 87.12% to 96.21% /
89.39%; list `C`: 96.74% / 88.04% to 97.83% / 89.13%; list `M`: 95.16% / 95.16% to 95.97% / 95.97%; list `T`: unchanged,
all `fine`). Each row was checked, on the branch of the change, against the slot probabilities (the sum over the
analyses of a part of speech) of the encoder alone (the weight 1) and of MiniLM now (the probabilities below are those
of the branch, before the clause state of the tagger; the five rows are `fine` in the final build):

<!-- markdownlint-disable MD013 -->

| Row | Model | Before | Now | Why |
| --- | --- | --- | --- | --- |
| `H-second-5` (`I {second} the motion to end the meeting early`) | MiniLM | miss | fine | The encoder alone had the noun on top (noun 0.634, verb 0.270); Basic reads the verb (0.998). Blended: verb 0.881, noun 0.089. |
| `H-estimate-1` (`a rough {estimate} of the repair cost`) | MiniLM | miss | fine | The encoder alone had the verb on top (verb 0.937, noun 0.063); Basic reads the noun (1.000). Blended: noun 0.869, verb 0.131. |
| `H-detail-4` (`Her letter {details} the problems`) | MiniLM | miss | fine | The encoder alone had the noun on top (noun 0.597, verb 0.403); Basic reads the verb (0.989). Blended: verb 0.829, noun 0.171. |
| `M-review-4` (`The committee will {review} the application`) | MiniLM | miss | fine | The encoder alone had the noun on top (noun 0.553, verb 0.447); Basic reads the verb after `will` (1.000). Blended: verb 0.995. |
| `C-content-5` (`They had to {content} themselves`) | MiniLM | miss | fine | The encoder alone chose the adjective (0.562 against verb 0.390); Basic reads the verb (0.999). Blended: verb 0.941, adjective 0.051; the sense `content#satisfy-partly` is the top ranked sense of the verb. |
| `M-change-4` (`The leaves {change} color in the fall`) | MiniLM | fine | **miss** | **The one row that went down on the branch of this change; fine again in the final build.** See below. |

<!-- markdownlint-enable MD013 -->

**`M-change-4`, the row that went down on the branch of this change.** The encoder alone gives the verb `change` the
larger slot (verb 0.534, noun 0.466) and is right. Basic's own answer on that branch was wrong for both words of the
pair: `leaves` is the noun `leaf` (0.582, against the noun `leave` 0.413 and the verb `leave` 0.005) and `change` is a
noun with 0.814 (verb 0.186), the tagger reading `the leaves change` as a noun compound. The blend is a geometric mean,
so a confident tagger carries more weight than an encoder that is nearly even: the noun scores
`0.466^0.6 × 0.814^0.4 = 0.583` and the verb `0.534^0.6 × 0.186^0.4 = 0.350`, which normalizes to noun 0.625 and verb
0.375, so the noun was selected (the sense `leaf` of `leaves` is answered at the sense level by the encoder and is
unchanged). The weight at which the row flips is between 0.90 and 0.92; every weight of 0.90 or less loses it, and at
0.5 `M-long-2` goes as well. That was the price of a tagger that was confidently wrong on a verb after a plural noun: the
same blend repairs the verb `use` (42 tokens called a noun by the encoder alone, 8 on the branch) and the five rows
above, where the tagger is right and the encoder is close to even. The later change that repairs the row is the clause
state of the tagger (next section): Basic now reads `change` as the verb (0.51), so the final build grades the row
`fine` for MiniLM and its expectation is the one of main. The explanation that rule 3 asks for every row that goes down
therefore applies to the branch only: the combined diff against main has no row that went down (see "The four changes
together" below).

### The clause state and the possessive expansion of the tagger (Basic retrained, encoders and image unchanged)

The tagger of `BasicWordSenseModel` now knows whether the current clause already has a verb (the transitions are
counted and read per clause state) and reads a possessive that is written as one token as its stem and the possessive
marker; `basic.bin` was retrained (format `WSB5`). On its own branch the encoder weights and `wordsense.img` did not
change; the encoder models inherit the part of speech of Basic where they fall back, so their cells could have moved
too, and on that branch none did (`evaluate-suite` found no row below an expectation for any model). In the final build
the same change repairs `M-change-4` for MiniLM (see the reconciliation above). **Four expectations of Basic were
raised** under rule 1, each a part-of-speech row that the clause state fixes (the grades are against main, with the
usage prior of the final build; the probabilities are those of the branch, measured with the tagger of that branch):

<!-- markdownlint-disable MD013 -->

| Row | Model | Before (main) | Now | Why |
| --- | --- | --- | --- | --- |
| `M-change-4` (`The leaves {change} color in the fall`) | Basic | miss | fine | The clause has no verb yet, so after the noun phrase `The leaves` a verb is four times as likely as in a clause that has one; `change` was a noun (0.81) and is now the verb (0.51). The probabilities are close, so the row is near a tie. |
| `H-close-4` (`The two hikers walked {close} together along the narrow ledge`) | Basic | miss | fine | The clause already has the verb `walked`, so `close` is no longer read as a second verb: the adverb 0.46 now wins against the verb 0.42 (before 0.42 against 0.45). A near tie as well; the grade is `fine` and not `answer` because of the usage prior, which ranks the senses of the adverb. |
| `C-resume-2` (`He sent out ten {resumes} last week and got two calls`) | Basic | miss | answer | The clause already has the verb `sent`, so the second verb reading of `resumes` is suppressed: the noun is 0.75 (before the verb 0.54). |
| `C-resume-4` (`Train service {resumes} as soon as the snow stops falling`) | Basic | answer | fine | The clause has no verb before `resumes`, so the verb reading wins against the noun compound `Train service resumes` (verb 0.63, noun 0.37 in the final build) and the most frequent sense of the verb is the acceptable sense; with the prior alone the row stays `answer`. |

<!-- markdownlint-enable MD013 -->

The cells that the development machine grades higher than the file for BertBaseCased (`H-close-4`, `H-defense-1` and
`C-content-6`) are the platform-sensitive rows of rule 4 and stay as recorded. Measured answered / fine on all 364 rows
on the branch of this change, before and after: Basic 95.88% / 45.88% to 96.70% / 46.15%, MiniLM 95.33% / 89.29% and
BertBaseCased 98.63% / 94.23% unchanged. On the final build Basic is 96.70% / 79.12% (the usage prior adds the ranked
senses; the clause state accounts for 3 rows of the answered figure and 3 rows of the fine figure of the 364 rows, 0.82
points each).

### The four changes together (main `f4473ff` to the final build)

The expectation file of the final build was written once with `evaluate-suite --write-expectations` (rule 3) on the
combined build, with the rule 4 rows reset to their recorded grades, and diffed against main row by row. **No cell went
down; 137 cells went up** (Basic 122, MiniLM 10, BertBaseCased 5; 364 rows of three models):

- Basic: 119 rows `answer` to `fine` (118 through the usage prior, listed in its section, and `C-resume-4` through the
  clause state), `H-close-4` and `M-change-4` from `miss` to `fine` and `C-resume-2` from `miss` to `answer` (the clause
  state).
- MiniLM: the five rows of the usage prior (`H-live-2`, `H-august-3`, `H-august-4`, `C-dove-1`, `C-polish-7`, `answer` to
  `fine`) and the five rows of the reconciliation (`H-second-5`, `H-estimate-1`, `H-detail-4`, `M-review-4` and
  `C-content-5`, `miss` to `fine`); `M-change-4`, the one row that the reconciliation alone lost, is `fine` as on main.
- BertBaseCased: the same five rows of the usage prior (`answer` to `fine`); the three rows of rule 4 are as recorded
  (`H-close-4` and `C-content-6` `miss`, `H-defense-1` `answer`).
- The heteronym analyses (the adjective `graduate`, the noun `separate` and the forms of `axe`) moved no cell of their
  own: the rows of `graduate` and `separate` (`H-graduate-1` to `4`, `H-separate-1` to `4`) went up through the usage
  prior, and no row asks for `axes`.

Measured answered / fine on all 364 rows, on the development machine, main (`f4473ff`) to the final build: Basic
95.88% / 45.88% to 96.70% / 79.12%, MiniLM 95.33% / 89.29% to 96.70% / 92.03%, BertBaseCased 98.63% / 94.23% to
98.63% / 95.60% (BertBaseCased grades the three platform-sensitive rows `fine` here, so these two figures are those of
the development machine). By list, answered / fine, Basic, MiniLM, BertBaseCased: `H` (132 rows) 97.73% / 69.70%,
96.21% / 89.39%, 100.00% / 95.45%; `M` (124 rows) 95.97% / 95.97%, 95.97% / 95.97%, 96.77% / 96.77%; `C` (92 rows)
95.65% / 66.30%, 97.83% / 89.13%, 98.91% / 93.48%; `T` (16 rows) `fine` for all three.

### One-sense labels for the set rows (no model change)

The 174 rows that accepted a set of senses (see the conversion above) were labeled again, blind, by two independent
labelers (the language models gpt-6.1-sol and claude-sonnet-5.5). Each saw every sense of the lemma and part of speech
with the sentence, but **not** the accepted set, so the set could not steer the label. A row was narrowed to one sense
when both labelers chose the same sense, that sense was inside the accepted set and at least one of them was highly
confident. That gave 147 candidate one-sense labels (the `senses` cell of `suite.tsv` is the only column that changed;
the label is always a member of the old set). The other 27 rows keep their full set because the labelers disagreed, one
was unsure, or a labeler chose a sense outside the set (the list is under "Rows that no longer have a single right
answer").

**Adjudication of the narrowed labels.** WordNet senses are often near-duplicates (`use#usefulness` and `use#function`,
`estimate#approximation` and `estimate#cost-quote`), and the suite must not penalize a model for a sense that is equally
right. Where a narrowed label made a model drop a grade (26 rows), a third reviewer (gpt-6.1-sol) judged, in context,
whether each candidate sense, the new label and the top-ranked sense of the model, is acceptable for the sentence. The
labelers and the adjudicator were blind to the grades of the models and to which sense was the label. When the model's
sense was also acceptable (13 rows), the `senses` cell is the set of the two senses, ordered like the other sets; it is
a subset of the old set (for `H-estimate-3` it equals the old set, which had these two senses only). When the model's
sense was judged wrong in context (the other 13 rows), the row keeps its
one-sense label. For `H-detail-1` the adjudicator accepted `detail#part` (BertBaseCased's sense) but not
`detail#discourse` (MiniLM's), so the row is a two-sense set and the MiniLM sense stays wrong. The result for the 174
rows is 134 with a one-sense label, 13 with a set of two senses and 27 with the full old set.

No model, weight or table changed; only the rows are stricter. The answers of the models are as before, but a top-ranked
sense that was inside the old set may be outside the narrowed label, so the grade `fine` becomes `answer` (the group or
part-of-speech answer is still right). Under rule 2 this is the row's correction: the old set accepted every sense that
the pronunciation class had mapped to, including senses that the sentence does not carry (the labelers chose
`use#function` for "find a use for the gadget", not the other six noun senses of the set). The expectation file was
rewritten with `evaluate-suite --write-expectations` under rule 3 and the rule 4 rows restored (`C-content-6` and
`H-close-4` stay `miss` for BertBaseCased: the rewrite measured `fine` for both on the development machine). 22 cells in
14 rows changed against the file before the review (348 rows): MiniLM 13 `fine` to `answer`, BertBaseCased 9 `fine` to
`answer`, Basic none; no cell went up and none went to `miss`. One more cell is recorded lower under rule 4:
`H-defense-1` for BertBaseCased is `fine` on the development machine (its two-sense set holds the top-ranked sense) but
`answer` on the Windows x64 CI runner, where a near tie puts a third sense of the old set first, so it is recorded
`answer` and listed under *Platform-sensitive rows*. Measured answered / fine (348 rows, development machine) before
and after:
Basic 95.69% / 43.39% unchanged, MiniLM 94.83% / 92.24% to 94.83% / 88.51%, BertBaseCased 98.28% / 96.26% to
98.28% / 93.68%.

Each cell that went down, with the top-ranked sense of the model (the sum over the analyses of the probability of the
analysis times that of the sense; MiniLM / BertBaseCased where both went down, the score of the one-sense label in the
same order) and the reason that the adjudicator gave for why the model's sense is wrong in context. In all 22 cells the
model answered at the part-of-speech or the group level and the answer stays right; only the top-ranked sense is outside
the label.

<!-- markdownlint-disable MD013 -->

| Row | Model | Before | Now | Measured (top-ranked sense, score; label, score) | Adjudicator's reason |
| --- | --- | --- | --- | --- | --- |
| `H-house-3` (`The stadium can house more than fifty thousand fans`) | MiniLM, BertBaseCased | fine | answer | `house#accommodate-person` 0.74 / 0.78; label `house#contain` 0.25 / 0.21. | The stadium's capacity for spectators does not describe providing lodging. |
| `H-address-2` (`The addresses on these old letters are hard to read`) | MiniLM, BertBaseCased | fine | answer | `address#geographic-point` 0.61 / 0.69; label `address#written-directions` 0.14 / 0.15. | The locations themselves are not the writing that is read. |
| `H-lead-6` (`Our manager led the team through a difficult year`) | MiniLM, BertBaseCased | fine | answer | MiniLM `lead#be-first` 0.96, BertBaseCased `lead#direct-music` 0.58; label `lead#command` 0.01 / 0.10. | The manager guided the team rather than being ahead in a competition (`lead#be-first`); no musical performance is conducted (`lead#direct-music`). |
| `H-contract-1` (`Neither side wanted to break the contract before it expired`) | MiniLM | fine | answer | `contract#bridge-contract` 0.55; label `contract#written-agreement` 0.41. | An expiring agreement between parties is not a bridge bid. |
| `H-inside-2` (`The inside walls of the cave were covered with paintings`) | MiniLM, BertBaseCased | fine | answer | `inside#building-interior` 0.85 / 0.95; label `inside#inner-side` 0.05 / 0.01. | The enclosed space is a cave, not a building. |
| `H-update-1` (`The software update will be released next week`) | MiniLM, BertBaseCased | fine | answer | `update#updating` 0.84 / 0.98; label `update#info-update` 0.13 / 0.02. | A released software deliverable is not the act of updating. |
| `H-detail-1` (`She described every small detail of the accident`) | MiniLM | fine | answer | `detail#discourse` 0.50; label `detail#fact` 0.01 (BertBaseCased's sense `detail#part`, 0.47, is now in the set). | Each countable detail is a particular, not an extended treatment of particulars. |
| `H-overall-2` (`Her overall grade improved after the final exam`) | MiniLM | fine | answer | `overall#general` 0.80; label `overall#comprehensive` 0.10. | The grade is an aggregate result, not a description limited to the main features. |
| `H-conflict-4` (`His statement conflicts with the evidence found at the scene`) | MiniLM, BertBaseCased | fine | answer | `conflict#violate` 0.82 / 0.97; label `conflict#contrast` 0.18 / 0.03. | Evidence is not a rule or law that is violated. |
| `C-sow-3` (`Farmers sow the field with wheat in early autumn`) | MiniLM, BertBaseCased | fine | answer | `sow#seed` 1.00 / 0.91; label `sow#plant-seeds` 0.00 / 0.09 (BertBaseCased answers the group `sow#group:sowing`, which has the label). | The sentence takes the field as the object; `sow#seed` takes the seeds. |
| `C-sow-4` (`He sows seeds along the garden fence every April`) | MiniLM | fine | answer | `sow#plant-seeds` 0.52; label `sow#seed` 0.48 (nearly tied for MiniLM). | This frame takes the ground as the object, not the seeds. |
| `C-bass-4` (`Our band still needs someone to play the bass on Friday`) | MiniLM | fine | answer | `bass#pitch` 0.33; label `bass#instrument` 0.24. | The bass to be played is the instrument, not the musical range itself. |
| `C-buffet-1` (`Dinner at the lodge is a long buffet with soups and salads`) | MiniLM, BertBaseCased | fine | answer | `buffet#counter` 0.77 / 0.68; label `buffet#meal` 0.20 / 0.31. | Dinner denotes a meal, not dining-room furniture. |
| `C-bow-3` (`The students bow to their teacher at the start of class`) | BertBaseCased | fine | answer | `bow#accede` 0.59; label `bow#greeting-bow` below 0.04 (`bow#bow-down` 0.20 is the second). | The sentence describes a greeting gesture, not yielding to an opinion or wish. |

The 13 rows that now accept two senses (`H-use-1`, `H-contact-1`, `H-increase-1`, `H-subject-2`, `H-estimate-1`,
`H-estimate-3`, `H-associate-1`, `H-detail-1`, `H-defense-1`, `C-produce-3`, `C-excuse-3`, `C-wound-2` and
`C-buffet-2`) have the grades that they had before the narrowing, except for `H-detail-1` (MiniLM), which is in the
table above, and `H-defense-1` (BertBaseCased), which is platform-sensitive (see above).

| Row | Label | Added sense (judged equally acceptable) |
| --- | --- | --- |
| `H-use-1` | `use#function` | `use#usefulness` |
| `H-contact-1` | `contact#communication` | `contact#interaction` |
| `H-increase-1` | `increase#growth` | `increase#increment` |
| `H-subject-2` | `subject#theme` | `subject#issue` |
| `H-estimate-1` | `estimate#cost-quote` | `estimate#approximation` |
| `H-estimate-3` | `estimate#approximate` | `estimate#predict` |
| `H-associate-1` | `associate#compeer` | `associate#subordinate-member` |
| `H-detail-1` | `detail#fact` | `detail#part` |
| `H-defense-1` | `defense#legal-plea` | `defense#justification` |
| `C-produce-3` | `produce#manufacture` | `produce#yield` |
| `C-excuse-3` | `excuse#overlook` | `excuse#forgive` |
| `C-wound-2` | `wound#lesion` | `wound#combat-injury` |
| `C-buffet-2` | `buffet#meal` | `buffet#snack-bar` |

<!-- markdownlint-enable MD013 -->

The analysis (lemma and part of speech) that the models chose was right for every cell above; the grade fell only because
the top-ranked sense is not the one-sense label and was judged wrong in context. The grades are the measurement of the
current models; if a later change improves them, the policy raises the expectation again.

### The usage prior of the untrained lemmas (all three models, no retraining)

`BasicClassifier` now ranks the senses of every analysis whose lemma and part of speech have two or more senses by the
usage prior: the WordNet 3.0 tag count of each sense (embedded in the word image, format version 5) with add-one
smoothing, equal counts in inventory order (see `docs/design/word-sense/core/basic-classifier.md`, "Usage Prior"). The
encoder models answer a word whose lemma they have no weights for through Basic, so all three models inherit the
ranking. It is a ranking and never an answer: the level, the confidence, the sense id and the group id of every result
are those of the model before the change, and neither the encoder weights, `basic.bin` nor the inventory hash changed
(on the branch of that change; the final build retrained `basic.bin` and the image).
Only the sense lists of the analyses gain content, so a row can only go up, and only the grade `fine` (the most probable
ranked sense is an acceptable sense) can change. The expectation file was rewritten with
`evaluate-suite --write-expectations` under rule 3 and the rule 4 rows restored (`C-content-6` and `H-close-4` stay `miss`
and `H-defense-1` stays `answer` for BertBaseCased: the rewrite measured `fine` for all three on the development machine,
see the platform-sensitive rows). Measured answered / fine on the 364 rows before and after, on the development machine
(where BertBaseCased grades the three platform-sensitive rows `fine`): Basic 95.88% / 45.88% to
95.88% / 78.30%, MiniLM 95.33% / 89.29% to 95.33% / 90.66%, BertBaseCased 98.63% / 94.23% to 98.63% / 95.60%.
131 cells went up in the rewrite and none went down (three of them are the platform-sensitive rows, which stay as recorded);
the final build keeps these 128 rows and adds the rows of the other changes (see "The four changes together"):

- **Basic, 118 rows from `answer` to `fine`** (76 rows of the list `H` and 42 of the list `C`): the rows that ask for a
  sense of a common heteronym or a classic one (`bow`, `wound`, `polish`, `live`, ...) were answered at the
  part-of-speech level without any ranked sense, so the most probable sense of the answer was empty; it is now the most
  frequent sense of the lemma and part of speech, which is an acceptable sense of the row in these 118 rows. The rows of
  the list `M` (the part of speech decides) and `T` do not change. The other rows of the lists `H` and `C` stay `answer`
  or `miss`: a ranking that is the same in every sentence is right for only one of the senses of a lemma, so the rows
  whose sentence calls for another sense, and the rows whose part of speech is wrong, do not change.
- **MiniLM and BertBaseCased, the same five rows from `answer` to `fine`**: `H-live-2` (`My aunt lives in a small town by
  the sea`), `H-august-3`, `H-august-4`, `C-dove-1` (`A gray dove sat quietly on the garden wall at dawn`) and `C-polish-7`
  (`I will polish the silver before the guests arrive`): the encoders answer these rows through the Basic fallback (the
  lemma has no weights, or the form is a form of several lemmas without a chooser), the fallback chooses the right analysis
  and its most frequent sense is an acceptable sense of the row.

The grades are a measurement of the models at the time of the change, as for every expectation: a model that later
ranks these senses wrongly falls below them and fails the build, which is the point of the file. The suite is a regression
check and says nothing about the accuracy of the ranking on running text; that measurement is the all-words gold set of
`corpus/allwords` (see `docs/user_guide/accuracy.md`).

### Deliberate model change: the word prior of the tagger (rule 3, Basic retrained)

Basic has a part-of-speech prior for every word (the WordNet 3.0 tag counts of the senses of the analyses blended into
the emission, every ambiguous word tabled, the exponent of the emission weight at 1) and a reliability for every cue
table (from its anchors and from the running text of the training split of the open corpus); see
`docs/user_guide/accuracy.md`, "The Word Prior of the Tagger". The encoder models take the part of speech and the
probabilities of Basic for the words they do not decide. The expectation file is written with
`evaluate-suite --write-expectations` under rule 3, and the three platform-sensitive rows are kept under rule 4
(`C-content-6`, `H-close-4` and `H-defense-1` of BertBaseCased measure `fine` on the development machine and stay as
recorded). Answered / fine on the 364 rows: Basic 96.43% / 79.12%, MiniLM 96.43% / 91.76%, BertBaseCased 98.63% /
95.60%. Two rows are graded as a miss for a model that could read them right; rule 3 explains both:

<!-- markdownlint-disable MD013 -->

| Row | Model | Grade | Why |
| --- | --- | --- | --- |
| `H-present-4` (`The shop sells ideas for wedding presents under twenty dollars`) | Basic | miss | `presents` has five verb counts in the open corpus, and the WordNet 3.0 counts of `present` are verb-heavy, so the word prior leans to the verb, which is chosen at 0.79. The context (`wedding presents`) is a noun; a hand fix would be a hand list, so the row is graded as a miss. |
| `M-long-2` (`She has long brown hair that reaches her waist`) | MiniLM | miss | A near tie decided by the tagger: the combined result has the adverb at 0.498 and the adjective at 0.489. Basic reads `long` as an adverb here (a miss for Basic in the file too) at 0.997, which moves the blend of MiniLM (the primary weight 0.6) across the tie. |

<!-- markdownlint-enable MD013 -->

The strength of the prior is chosen on the development files with the suite as a binary guard only: a weaker prior (3
pseudo counts) gains more on the development files but loses five suite rows (`state`, `feel`, `august`, `resume` and
`present`), so the strength of 7 stays, which leaves `H-present-4` as the one row of Basic that is graded a miss. The
pseudo count of the running-text reliability of the cue tables is chosen the same way: the development files are flat,
and a pseudo count of 20 or less damps the table of `feel` (0 tokens helped and 2 hurt) enough to turn `M-feel-2`
(Basic, `a cozy feel`, a noun at 0.54 against 0.46) into a miss, while 50 keeps it.

## Known misses

The rows that all three models miss (`C-polish-2`, the adjective `Polish` in `a traditional Polish soup`, which the
tagger reads as a noun in the noun compound; `live` and `close` as adverbs, `way` as an adverb, `show` as a noun) are the
real failures of the part-of-speech and sense choice that the suite is there to show (the current grades are in
`expectations.tsv`). The `Polish`, `polish` and `august` rows that were misses for a lemma that the models had no
way to separate (`august` finds the month `August` and the adjective with the same prior) now use the case of the
word; see the next section.
