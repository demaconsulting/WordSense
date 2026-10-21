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
expectation file is, in contrast, a measurement of the models at the time it was written (the first measurement is
recorded in `docs/build_notes/independent-evaluation.md`).

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

## Known misses

The rows that all three models miss (the capitalized `Polish` rows and `polish` the shoe polish, `august` the
adjective, `live` and `close` as adverbs, `way` as an adverb, `show` as a noun) are the real failures of the
part-of-speech and sense choice that the suite is there to show; the current figures are in
`docs/build_notes/independent-evaluation.md`. A frequent cause is the lexicon folding the capitalized entry into the
lower-case spelling (`august` finds the month `August` and the adjective with the same prior).
