# The reviewed suite

A hand-reviewed set of 364 sentences about common ambiguous words and verbs whose past and non-past share a spelling.
It is a regression test of the three models and a readable record of what each model gets wrong. It is **not** a measure
of accuracy on running text: the sentences were chosen to be hard for the words they test. The headline figures are the
real-text sense evaluation, SemCor and the running-text part-of-speech evaluation of the root `README.md`.

## Provenance and license

The first 348 sentences and their notes were written for the project by language-model agents under the project's
instructions. A separate reviewer agent then checked every row (verdict `OK` for all 348: the context gives the braced
word exactly the expected reading). The 16 rows of the list `T` (the tense rows) were written by hand by the developer
who added the tense model.

All sentences are project-authored content dedicated to the public domain under CC0 1.0 by the project owner, like every
project-written row of the corpus. No sentence is taken from a third-party text.

The expected labels were written and reviewed before any model was run on the suite. The expectation file is a
measurement of the models.

The suite is evaluation data: no training step reads it. The leakage check (`WordSense.Builder check-leakage`) verifies
that no sentence of it is in the anchors, the held-out sentences, the real-text rows, the open corpus (the training,
development and test splits and the challenge set) or an example sentence of Open English WordNet.

## Files

<!-- markdownlint-disable MD013 -->

| File | Content |
| ---- | ------- |
| `suite.tsv` | The rows: `id`, `list`, `lemma`, `analysis`, `senses`, `target_index`, `sentence`, `note` |
| `expectations.tsv` | For each row and model, the strongest outcome the model is expected to keep: `fine`, `answer` or `miss` |
| `ste-floors.tsv` | The floor of each model on the challenge set (accuracy on all and on the ambiguous tokens, in percent) |

<!-- markdownlint-enable MD013 -->

### `suite.tsv`

- `id` is unique (`H-use-1`).
- `list` is one of:
  - `H` (132 rows): the 30 most frequent heteronyms.
  - `M` (124 rows): 62 words used as two or more parts of speech, two sentences per part of speech.
  - `C` (92 rows): 20 classic heteronyms such as `bow`, `wound`, `polish`.
  - `T` (16 rows): eight verbs whose past and non-past share a spelling (`read`, `put`, `cut`, `set`, `hit`, `let`,
    `spread`, `come`), a non-past and a past row each.
- `analysis` is what the selected analysis of the target word must be: `lemma/Part` or `lemma/Part/Feature=Value+...`.
  The lemma is that of the lexicon, so `wound` (the past of `wind`) is `wind/Verb` and `used` is `use/Verb`.
- `senses` are the acceptable fine senses of the row, separated by `|`. It is empty for a row that only asks for the
  analysis.
- `sentence` is the words separated by single spaces (punctuation as separate words) with the target word in braces.
  `target_index` is the zero-based index of the braced word.
- `note` is the note of the reviewer. The notes of the heteronym rows name pronunciation classes (`polish#adj`,
  `live#adv`), not senses. The `senses` column is the label.

The labels follow these rules:

- The `M` rows, the 8 analysis-only heteronym rows (`read` past or present, `wound` and `dove` as past tenses) and the
  `T` rows ask for the analysis only: no senses. The tense of `read` is a feature of the analysis, not a sense.
- 40 rows accept a set of senses (13 sets of two senses, 27 larger sets); every other row with senses has one. Where a
  set exists, the pronunciation class spreads over several fine senses.
- The one-sense labels were chosen blind by two independent labelers (the language models gpt-6.1-sol and
  claude-sonnet-5.5), who saw every sense of the lemma and part of speech but not the accepted set. A label was kept
  when both chose the same sense and at least one was highly confident. A third reviewer (gpt-6.1-sol) adjudicated near
  duplicate senses (`use#usefulness` and `use#function`, `estimate#approximation` and `estimate#cost-quote`): where a
  model's sense was also right in context, the row accepts both.
- The capitalized entries (`August`, `Polish`) have their own sense ids (`August#noun`, `Polish#adjective`,
  `Polish#noun`) and are expected as such for the capitalized rows.
- `polish#refinement` is accepted with the shine and the polishing agent.
- The `Defense#noun` entry (the Department of Defense) is not an acceptable sense of the `defense` rows.
- The `T` rows are analysis-only, with a context that says the tense without doubt (a modal, do-support, a form of
  `have`, an imperative, a past-time adverb). They are easy by construction: a regression guard for the tense, not a
  measure of accuracy (see "Tense of Same-Spelling Verbs" of `docs/user_guide/accuracy.md`). The expected grade is
  `fine` for all three models.

## Grading

The grading rule is `SuiteFile.Grade` (`tools/WordSense.Builder/Suite.cs`, compiled into the three test projects, so the
builder and the tests grade alike). A model's answer is judged **at the level it answered**:

- A row with acceptable senses is right at the **sense** level when the answered sense is one of them. It is right at
  the **group** level when the answered group contains one of them. It is right at the **part-of-speech** level when
  the selected analysis has the lemma, the part of speech and the features of the row. No answer (an unknown word) is
  wrong.
- A row without acceptable senses is right when the selected analysis has the lemma, the part of speech and the
  features of the row, whatever the level.

The **grade** of a row is:

- `fine` when the answer is right and, for a row with acceptable senses, the most probable ranked sense is one of them;
- `answer` when the answer is right at its own level but the top-ranked sense is not acceptable (or the model ranks no
  sense);
- `miss` otherwise.

A model that answers at the part-of-speech level and ranks no senses can reach `answer` but never `fine` on a row with
acceptable senses. The Basic model ranks the senses of every analysis with two or more senses by usage (the usage
prior, never an answer), so it can reach `fine` when the most frequent sense is an acceptable one. That is a base rate,
not a decision of the context, so the fine rate of the Basic model stays below that of the encoder models.

## Expectations and the policy

`expectations.tsv` records, for every row and model, the strongest grade that the model reached when the file was
written. `BasicSuite_ExpectedPasses_StillPass`, `MiniLMSuite_ExpectedPasses_StillPass` and
`BertBaseCasedSuite_ExpectedPasses_StillPass` (the test projects of the three models) fail when a row grades below its
expectation. A regression fails the build, and the known misses stay visible in one file.

Answered / fine on all 364 rows, measured on the development machine (where BertBaseCased grades the platform-sensitive
rows below `fine`):

| Model | Answered | Fine |
| ----- | -------: | ---: |
| Basic | 96.43% | 78.85% |
| MiniLM | 96.43% | 91.76% |
| BertBaseCased | 98.63% | 95.60% |

**Policy.**

1. An expectation is **raised** in a reviewed commit when a model improves (`WordSense.Builder evaluate-suite` prints
   how many rows are better than their expectation).
2. An expectation is **never lowered to hide a regression**: a failing row is fixed in the model or its data, or the
   change that caused it is reverted. A row may be lowered only when the *row itself* is found to be wrong (a mislabeled
   sentence or an unacceptable sense in the set). That correction changes `suite.tsv` and is reviewed as such, with the
   explanation in the commit message and the pull request description.
3. A model that is retrained (a final retraining before a release, a new model) writes a new file with
   `evaluate-suite --write-expectations corpus/suite/expectations.tsv --overwrite true`. The diff of the file is
   reviewed row by row: every row that goes down is explained in the commit message and the pull request description,
   every row that goes up is welcome.
4. A row whose grade differs between the CI platforms is **platform-sensitive**: the int8 encoder arithmetic differs
   slightly between CPUs (instruction sets) and operating systems, which can flip a near tie. Such a row is recorded at
   the weakest grade it reaches on any CI platform and listed under *Platform-sensitive rows* with the platforms. A row
   is listed only when a CI run shows a lower grade on a named platform. This is a property of the arithmetic, not a
   regression.

### Platform-sensitive rows

<!-- markdownlint-disable MD013 -->

| Row | Model | Grade on the development machine | Grade on CI (Windows x64, macOS arm64) | Recorded |
| --- | --- | --- | --- | --- |
| `C-content-6` (`A small raise will not content the angry workers`) | BertBaseCased | fine | miss | miss |
| `H-defense-1` (`The lawyer prepared a strong defense`) | BertBaseCased | fine | answer (Windows x64) | answer |

<!-- markdownlint-enable MD013 -->

## Known misses

The rows that all three models miss are the real failures of the part-of-speech and sense choice that the suite is there
to show:

- `C-polish-2` (`a traditional Polish soup`): the tagger reads the adjective `Polish` as a noun in the noun compound.
- `live` and `close` as adverbs, `way` as an adverb and `show` as a noun.

Two rows are graded as a miss for one model that could read them right:

- `H-present-4` (`The shop sells ideas for wedding presents under twenty dollars`), Basic: `presents` has five verb
  counts in the open corpus and the WordNet 3.0 counts of `present` are verb-heavy, so the word prior leans to the verb
  (0.79). The context (`wedding presents`) is a noun. A hand fix would be a hand list, so the row stays a miss.
- `M-long-2` (`She has long brown hair that reaches her waist`), MiniLM: a near tie decided by the tagger. The combined
  result has the adverb at 0.498 and the adjective at 0.489. Basic reads `long` as an adverb here at 0.997, which moves
  the blend of MiniLM (the primary weight 0.6) across the tie.

The current grades of every row are in `expectations.tsv`.
