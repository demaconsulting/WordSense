# Independent evaluation, running-text part of speech, the reviewed suite and the leakage check

This note records the evaluation tooling of increment I5 and its first measurements: the independent sense evaluation on
SemCor, the part-of-speech accuracy of every model on running text, the reviewed suite of 348 sentences, and the leakage
check of the corpus with what it found. The commands are `evaluate-senses`, `evaluate-running`, `evaluate-suite`,
`check-leakage` and `tiers` of `tools/WordSense.Builder`; the figures were measured with the models retrained after the
leakage fix (see the last section). The cost columns (RAM, latency, first use) of the model table come from the last
`evaluate` run on a development machine that was not set aside for the measurement and are to be remeasured in I6.

## SemCor: license and use

SemCor 3.0 (352 texts of the Brown Corpus annotated with WordNet senses; Rada Mihalcea's automatic mapping to WordNet
3.0 of the Princeton SemCor 1.6) is distributed under the license of WordNet. The archive `semcor3.0.tar.gz` carries
the Princeton license ("Permission to use, copy, modify and distribute this software and database and its
documentation for any purpose and without fee or royalty is hereby granted, provided that you agree to comply with the
following copyright notice and statements, including the disclaimer"), and its README repeats the grant for the mapped
database ("In agreement with the license from Princeton University, you are granted permission to use, copy, modify
and distribute this database for any purpose and without fee and royalty"). **Verdict: clearly permissive for
evaluation use** (the page that lists the download says that other data of that site is GPL; the archive's own license
and README are the ones that apply, and both name Princeton's license). The project therefore downloads the archive to
a git-ignored cache, verifies the pinned SHA-256 (`A8000014...F017B`), reads it, reports scores and never writes a
SemCor sentence to the repository; the notice is in `data/PROVENANCE.md`. The example sentences of Open English
WordNet are therefore **not** needed as the independent sense evaluation; they are only checked for leakage (below).
The Brown Corpus text under SemCor is not redistributed by us.

## Independent sense evaluation (`evaluate-senses`)

The WordNet 3.0 sense key of an annotated word is `lemma%lexsn`; it is mapped to the sense of the catalog that has the
key (the catalog keeps the Open English WordNet keys, equal to the WordNet 3.0 keys for every sense the 2025 release
did not change). The evaluation words are the single-word annotations with exactly one key whose lemma is one of the
775 trained lemmas.

<!-- markdownlint-disable MD013 -->

| Step | Words |
| --- | ---: |
| annotated words | 234,136 |
| left out: multiword expressions | 16,793 |
| left out: sense dropped by the mapping from WordNet 1.6 to 3.0 | 6,598 |
| single words with an annotation | 210,745 |
| left out: more than one sense key | 763 |
| key is the key of a sense of the catalog (**mapping coverage 97.71%** of the single words with one key) | 205,167 |
| lemma is trained (**the evaluation set**) | 31,490 |
| sentences left out because they are also in a part of the corpus or an Open English WordNet example | 35 |

The most frequent keys without a sense in the catalog are `be%2:42:04::` (665 words), `ask%2:32:00::` (230),
`know%2:31:02::` (110) and the satellite adjectives `several`, `next`, `each`, `first`, `any` (the 2025 release changed
them).

| Model | Sense (share) | Sense precision | Group (share) | Group precision | Part of speech (share) | Part-of-speech precision | Fine top-1 | Group top-1 | Part of speech | MFS baseline |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Basic | 3.32% | 67.34% | 0.00% | n/a | 96.55% | 93.54% | 2.51% | n/a | 92.58% | 60.68% |
| MiniLM | 1.65% | 76.69% | 3.71% | 86.23% | 94.51% | 93.39% | 47.46% | 58.62% | 93.22% | 60.68% |
| BERT base cased | 12.55% | 83.02% | 3.29% | 84.09% | 84.03% | 96.11% | 56.92% | 66.51% | 96.08% | 60.68% |

<!-- markdownlint-enable MD013 -->

*Fine top-1* is the share of the words whose most probable ranked sense is the gold sense (Basic ranks no sense for
most words); *group top-1* is the share of the words whose gold sense is in a group and whose most probable sense is
in that group; *MFS baseline* is the share of the words whose gold sense is the first sense of the gold lemma and part
of speech in the inventory order (the baseline knows the gold lemma and part of speech).

What the figures say:

- The sense-level answers are much less precise on SemCor than the 95% (cross-validated) and 97% (real-text calibration)
  targets of the thresholds: 83% for BERT and 77% for MiniLM, on text that is very unlike the training sentences (Brown
  Corpus prose of 1961, annotated with fine WordNet senses that annotators themselves disagree on).
- The fine top-1 of the encoders (47% and 57%) is **below the most-frequent-sense baseline (61%)**, which is optimistic
  because the order of the senses comes from WordNet counts that include SemCor itself. The sense choice for the trained
  lemmas is therefore not yet better than "always the commonest sense" on independent text; the part of speech is the
  dependable part of an answer.
- The share answered at the sense level is 12.6% for BERT and 1.7% for MiniLM, so the sense claim is made rarely; the
  honest levels (group and part of speech) carry the rest.
- Mapping losses are small (97.7% of the keys map) and do not change these conclusions.

## Running-text part of speech (`evaluate-running`)

Every model is scored through its public interface on the same sets: the `test` split of the representative open
corpus (the headline; random CC0 text), the test file of the English EWT treebank mapped to the conventions of the
lexicon by `tools/open-corpus/scripts/prepare_tagger_eval.py` (evaluation only; downloaded to a git-ignored cache, its
SHA-256 pinned by the script, never committed) and the 32 sentences of the simplified-technical-English challenge set
(its own line).

<!-- markdownlint-disable MD013 -->

| Model | Set | Sentences | Scored tokens | Accuracy (all tokens) | Ambiguous tokens | Accuracy (ambiguous tokens) | Not answered | Excluded |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Basic | open corpus test (representative) | 1,501 | 21,481 | 93.85% | 12,810 | 91.48% | 663 | 3,888 |
| Basic | UD-EWT test (convention-mapped) | 2,077 | 19,472 | 91.60% | 12,482 | 88.54% | 1,376 | 3,892 |
| Basic | STE challenge set | 32 | 222 | 98.65% | 138 | 97.83% | 0 | 50 |
| MiniLM | open corpus test (representative) | 1,501 | 21,481 | 93.72% | 12,810 | 91.28% | 663 | 3,888 |
| MiniLM | UD-EWT test (convention-mapped) | 2,077 | 19,472 | 91.60% | 12,482 | 88.54% | 1,376 | 3,892 |
| MiniLM | STE challenge set | 32 | 222 | 98.20% | 138 | 97.10% | 0 | 50 |
| BERT base cased | open corpus test (representative) | 1,501 | 21,481 | 93.98% | 12,810 | 91.70% | 663 | 3,888 |
| BERT base cased | UD-EWT test (convention-mapped) | 2,077 | 19,472 | 91.85% | 12,482 | 88.93% | 1,376 | 3,892 |
| BERT base cased | STE challenge set | 32 | 222 | 98.20% | 138 | 97.10% | 0 | 50 |

<!-- markdownlint-enable MD013 -->

"Not answered" is a token with a word-class gold tag that the model does not answer (a word that is not in the
dictionary: names, typos, foreign words); "excluded" is a token that the corpus or the convention tags as punctuation
or other (numbers, symbols, foreign words, particles other than *to*). The three models are within 0.3 points of each
other on the random text: the encoders choose the part of speech of the trained lemmas only, and those are a small
part of the running words, so the Basic tagger sets the running-text part-of-speech accuracy; the encoders' advantage
is the sense level (above), and on the trained lemmas' held-out sentences the part of speech of BERT is 96.2% against
92.7% for Basic (the real-text test split of `evaluate`).

## The reviewed suite (`evaluate-suite`)

The 348 reviewed sentences (`corpus/suite`; provenance, the conversion of the heteronym rows from the
pronunciation-class ids to the part of speech and the fine senses, and the grading are in `corpus/suite/README.md`)
give these pass rates (*answered*: the answer is right at the level the model gave; *fine*: besides, the most probable
ranked sense is acceptable):

<!-- markdownlint-disable MD013 -->

| List | Rows | Basic answered / fine | MiniLM answered / fine | BERT base cased answered / fine |
| --- | ---: | ---: | ---: | ---: |
| H (heteronyms, 30 words) | 132 | 94.70% / 10.61% | 93.18% / 92.42% | 95.45% / 94.70% |
| M (words with several parts of speech) | 124 | 95.16% / 95.16% | 95.16% / 95.16% | 96.77% / 96.77% |
| C (classic heteronyms, 20 words) | 92 | 91.30% / 16.30% | 92.39% / 85.87% | 94.57% / 90.22% |
| all | 348 | 93.97% / 42.24% (327 / 147 rows) | 93.68% / 91.67% (326 / 319) | 95.69% / 94.25% (333 / 328) |

<!-- markdownlint-enable MD013 -->

The earlier measurement of the same sentences with the pronunciation-class expectation (before the conversion) was
41.7%, 92.2% and 95.4% on the "fine" measure, so the conversion to the fine-sense scheme changed the rates by less
than two points. Basic reaches the *fine* grade only on the rows that ask for the analysis (the 124 part-of-speech
rows and the `read`, `wound` and `dove` rows), which is why its fine rate is low: it does not rank senses for the
heteronyms.

The rows that **all three models miss** (known misses, listed in `corpus/suite/expectations.tsv` as `miss`) are the
capitalized `Polish` rows and `polish` the shoe polish (the dictionary folds the capitalized entry into the lower-case
spelling, and the analysis of the language or the nationality competes with the shoe polish), `august` the adjective
(the month `August` wins with the same prior), `live` and `close` as adverbs, `way` as an adverb and `show` as a noun.
They are real failures of the choice of the analysis, not defects of the labels.

Each model's test project fails when a row falls below its expectation (`BasicSuite_ExpectedPasses_StillPass`,
`MiniLMSuite_ExpectedPasses_StillPass`, `BertBaseCasedSuite_ExpectedPasses_StillPass`); the policy is that an
expectation is raised for an improvement and never lowered to hide a regression. The same mechanism keeps each model
above a floor on the challenge set (`ChallengeSetFloorTests`, `corpus/suite/ste-floors.tsv`, the accuracy on all
tokens and on the ambiguous tokens, truncated to one decimal).

## The leakage check (`check-leakage`)

The check compares the normalized sentences of all parts of the corpus. The first run found these shared sentences
(exact matches after normalization; the number of rows removed is larger than the number of sentences when a sentence
has several rows):

<!-- markdownlint-disable MD013 -->

| Training part | Evaluation part | Shared sentences | Fix |
| --- | --- | ---: | --- |
| anchors | open corpus development / test | 105 / 103 | 209 anchor rows removed (some rows share a sentence) |
| anchors | Open English WordNet examples | 1 | removed with the anchors |
| chooser anchors | open corpus development / test | 32 / 26 | 58 chooser anchor rows removed |
| open corpus training | held-out sentences | 150 | 182 held-out rows removed (the open corpus is a frozen, validated corpus) |
| open corpus training | chooser held-out sentences | 54 | 69 chooser held-out rows removed |
| held-out / chooser held-out | open corpus development / test | 17 + 14 / 11 + 4 | removed with the held-out rows |
| held-out | Open English WordNet examples | 1 | removed with the held-out rows |

<!-- markdownlint-enable MD013 -->

All the shared sentences were real Tatoeba (CC0) sentences that the quota harvest drew from the same pool as the open
corpus (the harvest excluded the corpus and the suite, not the open corpus), plus one written sentence that equals a
dictionary example. 463 provenance rows went with the removed rows. The reviewed suite, the real-text rows and the
challenge set overlap nothing. After the fix the check passes: what is left is training-with-training overlap (anchors
with the chooser anchors and the open corpus training split: duplicates, nothing is evaluated on them), the chooser
held-out rows that are views of held-out rows, one three-word stock phrase ("i dare you") that is also an Open English
WordNet example (not a copy of creative text), and three SemCor sentences that are also open corpus sentences (the
sense evaluation leaves them out, like the 35 sentences that it leaves out in all). The runs of eight words that two
parts share (a few dozen) are reported as informational.

The removal of the anchors changed the training data, so `train-basic` and `weights` for both encoders were run again
(the embedding and fit caches made it fast: 148 lemmas refitted); the thresholds were recalibrated with the unchanged
procedure. The real-text sense measurements before and after the fix agree within noise (the HEAD weights on the
cleaned held-out set: MiniLM 3.19% @ 86.57%, BERT 13.75% @ 94.46%; the retrained weights: 3.14% @ 86.36%, 15.18% @
93.73%). Those figures are far below the ones of the README before this increment (8.1% @ 92.7% and 18.7% @ 94.9%),
which were measured before the retraining that followed the removal of the CC BY rows (commit 191746a) and are the
state of the branch, not an effect of the leakage fix.

## Quota report (`tiers`)

`tiers --output FILE` reports, for every sense of the trained lemmas, the tier of its WordNet 3.0 tag count (20 and 5,
10 and 3, 5 and 2), the counts and the status, and the senses that fall short with the sentences that are missing (the
C# replacement of `tools/quota-fill/coverage.py`, which stays the tool of the quota filling).

## Decisions (made without the owner)

1. **SemCor is used**, because its license is permissive; the Open English WordNet examples are only leakage-checked.
2. **The expectation of a suite row is a set of acceptable fine senses** (the pronunciation class of the old label),
   judged at the level the model answered; a one-sense label per row is a later refinement.
3. **One expectation file with a column per model** (`fine`, `answer`, `miss`), a three-valued ratchet, instead of one
   file per model with pass or miss: it records both that a model answers right and that its ranked senses are right.
4. **The capitalized entries are their own senses** in the suite (`August#noun`, `Polish#adjective`, `Polish#noun`),
   as the catalog has them; the `Defense#noun` (the Department of Defense) is not an acceptable sense of the common
   noun.
5. **The leakage fix removes the training copy**; where the open corpus is the other side (it is a frozen, validated
   corpus), the held-out copy is removed instead.
6. **A SemCor sentence that is also in the corpus is left out of the evaluation** rather than removed from the corpus.
7. **The running-text evaluation reads the gold file that `prepare_tagger_eval.py` writes** (the checksum and the
   convention map stay in the script), instead of repeating them in C#.
8. **The cost columns are flagged for remeasurement** on a quiet machine in I6.
9. **The weights were retrained after the leakage fix** (`train-basic`, `weights minilm`, `weights bert-base-cased`);
   I6 still re-freezes the global mean and retrains from scratch.
