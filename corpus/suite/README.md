# The reviewed suite

A hand-reviewed set of 348 sentences about common ambiguous words, kept as a regression test of the three models and as
a readable record of what each model gets wrong. It is **not** a measure of accuracy on running text (the sentences were
chosen to be hard for the words they test); the headline figures are the real-text sense evaluation, SemCor and the
running-text part-of-speech evaluation of the root `README.md`.

## Provenance and license

The 348 sentences (and their notes) were written for the project by language-model agents under the project's
instructions, then reviewed one by one by a separate reviewer agent (all 348 rows: verdict `OK`, the context gives the
braced word exactly the expected reading; no row needed a changed label). They are project-authored content dedicated
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
  inventory), `M` (124 rows: 62 words that are used as two or more parts of speech, two sentences per part of speech)
  or `C` (92 rows: 20 classic heteronyms such as `bow`, `wound`, `polish`).
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
acceptable (or the model ranks no sense); `miss` otherwise. A model that answers at the part-of-speech level and does
not rank senses (Basic) can reach `answer` but never `fine` on a row with acceptable senses; that is the honest
difference between the models and is why the Basic model's fine rate is low.

## Conversion from the pronunciation classes

The suite was written when the expected label of a heteronym row was a pronunciation class (`live#adj`, `wound#past`)
and the expected label of a word-class row was a part of speech. The conversion to the fine-sense scheme was done once,
from the class-to-sense map of the relabeling (kept in the git history, commit `3f9cafe`, `corpus/migration`):

<!-- markdownlint-disable MD013 -->

| Kind | Rows | Conversion |
| ---- | ---: | ---------- |
| `M` rows, part of speech only | 124 | `analysis` = the word's lemma and the part of speech; no senses |
| class with fine senses | 212 | `senses` = the fine senses the class maps to (current ids); 40 rows have a single sense, 172 an acceptable set |
| `used` rows (habitual `used to`, ordinary past) | 4 | `use/Verb`; habitual = `use#used-to` (2 rows), ordinary past = every other verb sense of `use` (2 rows, a set) |
| entry classes (`read` past or present, `wound` and `dove` as past tenses) | 8 | `analysis` only: `read/Verb/Tense=Past`, `read/Verb/Tense=Pres`, `wind/Verb`, `dive/Verb`; no senses |

<!-- markdownlint-enable MD013 -->

**Rows that no longer have a single right answer:** the 174 rows with an acceptable set (172 classes and the 2 ordinary
`used` rows) were pronunciation classes that spread over several fine senses (the class `lead#guide-noun`
holds fifteen senses). They are scored as above: a sense answer inside the set, a group answer that has a sense of the
set, or a part-of-speech answer with the right analysis. A tighter, one-sense label per row is possible and would
make the rows stricter; it is a candidate for a later review and does not change the expectation mechanism. The
8 analysis-only heteronym rows are scored by analysis and features (the tense of `read` is a feature of the analysis,
not a sense).

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

## Known misses

The rows that all three models miss (`C-polish-2`, the adjective `Polish` in `a traditional Polish soup`, which the
tagger reads as a noun in the noun compound; `live` and `close` as adverbs, `way` as an adverb, `show` as a noun) are the
real failures of the part-of-speech and sense choice that the suite is there to show (the current grades are in
`expectations.tsv`). The `Polish`, `polish` and `august` rows that were misses for a lemma that the models had no
way to separate (`august` finds the month `August` and the adjective with the same prior) now use the case of the
word; see the next section.
