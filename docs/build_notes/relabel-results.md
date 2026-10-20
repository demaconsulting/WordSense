# Relabeled corpus and retrained models

This note records the relabeling of the former class-labeled corpus to fine senses, the choice of the sentence labeler,
the curation of 738 more lemmas, the first form-chooser data and the retraining of both encoder models on the result.
The held-out sentences are partly written by a language model (about 57%) and every sentence was labeled by a model or by
rule, so every accuracy below is **optimistic**; the independent evaluation (SemCor 3.0 senses, the English EWT treebank
parts of speech) is a later increment. The figures come from `WordSense.Builder weights <model>` and
`evaluate <model>`.

## Curation merge (R0)

The labels of 738 lemmas (6,283 senses) that a `gpt-5.6-luna` pass had curated were merged into `corpus/curation`
(see its README). A second `gpt-5.6-luna` pass flagged 63 labels that misdescribe their gloss; 34 were changed by hand,
the others were senses with a placeholder gloss (`noun`, `verb`) where a label cannot be judged. After the merge 4,099 of
the 6,283 labels differ from the generated slug. The groups follow the admission rule: of the 120 proposed groups 84 are
kept, all `equivalent` with the evidence `curated:gloss` (the glosses are near-identical), 36 are dropped (they can
return from classifier confusion data). The image has 140 groups of 307 senses. `seed-inventory` now replaces a generated
label (never published) by the curated one; `validate-data` and `compile-image` pass.

## Labeler calibration (R0b)

Gold: the 984 held-out sentences of `batch-1` (known sense ids, 20 lemmas, 294 senses). Every candidate saw the
sentence, the target and the full sense list of the lemma (id, part of speech, gloss) and answered one sense id or
`unsure`. The sentence order was shuffled and no hint was given.

<!-- markdownlint-disable MD013 -->

| Labeler | Accuracy (`unsure` counts wrong) | `unsure` rate | Accuracy on non-`unsure` |
| --- | ---: | ---: | ---: |
| `gpt-6.1-sol` | 93.70% (922/984) | 1.73% (17) | 95.35% (922/967) |
| `gpt-5.6-luna` | 91.06% (896/984) | 0.10% (1) | 91.15% (896/983) |
| `gemma4:26b` (local model server) | not measured | not measured | not measured |

<!-- markdownlint-enable MD013 -->

`gpt-5.6-luna` is 2.64 points behind `gpt-6.1-sol`, more than the 1.0 point allowed, so **`gpt-6.1-sol` is the primary
labeler**. Where sol and luna agree (895 rows) the answer is right 97.09% of the time; the 89 rows where they differ
hold 53 right answers of sol (luna 27, neither 9), so a luna agreement filter would drop more correct rows (53) than
errors (36). The local model server (`192.168.1.62`) accepted connections and listed `gemma4:26b`, but no chat request
completed in two hours (even a request for eight tokens; the model is loaded with 1.2 GB of VRAM and the server was
busy), so gemma could neither be calibrated nor used as a flag. The relabeling uses the primary labeler alone.

## Relabeling (R1)

<!-- markdownlint-disable MD013 -->

| Step | Rows |
| --- | ---: |
| Legacy rows (24,461 anchors, 12,789 holdout, 689 real text) | 37,939 |
| Excluded, CC BY-SA 4.0 (UD English treebank files 1,910, Wikipedia 938, Wiktionary 206) | 3,054 |
| Relabeled by rule (the class has one fine sense and the lexicon analyzes the word as its part of speech) | 8,219 |
| Relabeled by the judge | 25,373 |
| Disputed: judge `unsure` | 991 |
| Disputed: the sentence occurs twice (the holdout copy wins) | 161 |
| Disputed: the sense belongs to a capitalized lemma (`Polish`, `August`; the corpus has lower-case lemmas) | 99 |
| Disputed: the lexicon has no analysis of the word for the lemma and part of speech of the sense | 42 |

<!-- markdownlint-enable MD013 -->

Written: 21,552 anchors, 11,385 held-out sentences and 655 real-text sentences (the evaluator does not read the real-text
set in the new format yet; it is kept for the independent evaluation). Of the judged rows 99% name a sense of the
legacy class (the judge was not told the class), which is the check that the judged labels follow the old classes where
they were right. The `used` rows, which the old corpus had inverted, are labeled by meaning: habitual "used to" is
`use#used-to` (22 rows), the ordinary past is `use#apply` or `use#consume`.

## Form choosers

The builder now trains a chooser for a form shared by several lemmas or tenses (`ChooserCorpus`,
`WeightsTrainer.AttachChoosers`, see the design of the builder). The chooser rows (`corpus/choosers`) hold 2,293
anchors and 1,066 held-out sentences of 326 forms (313 shared by several lemmas, 13 verb forms with two tenses): `read`
and `reread` take their tense from the legacy class, the other tense forms (`cast`, `upset`, `beat`, ...) from a
second `gpt-6.1-sol` call (207 rows, 24 left out as `unsure`), the lemma forms from the sense the judge chose. 82 forms
have
rows for every analysis and 78 of them have a trained lemma to store the chooser with; 243 forms got none because the
legacy corpus has rows of only one of their lemmas (the participles that are adjectives: `advanced`, `allied`, ...) and
one form lacks a label for an analysis. Held-out chooser sentences of the trained choosers (481): MiniLM top-1 88.77%,
answered at the thresholds 74.64% with precision 94.15%; BERT base cased 92.72%, 86.49% and 97.12%.

## Quota coverage

`corpus/coverage/relabel-coverage.tsv` lists every sense of the 845 lemmas in scope: 1,066 of 6,856 senses meet their
quota, 3,174 are short and 2,616 are empty; 45,434 anchors and 12,231 held-out sentences are missing (see its README).

## Retraining and measurement

Both encoders were trained with `weights <model> --recompute-mean` (the mean is frozen again from now on). 764 lemmas
have weights (25,040 anchors; 65 more lemmas have anchors for one sense only); the fit takes about 25 seconds, the whole
run two minutes for MiniLM and eight for BERT (embeddings cached). The half-precision gate holds (0.00 and -0.01 points).
On the full held-out set (12,369 sentences, all lemmas, an answer is right at its own level):

<!-- markdownlint-disable MD013 -->

| Model | Sense (share, precision) | Group (share, precision) | Part of speech (share, precision) | Fine top-1 | Part of speech | Best analysis |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basic | 22.9%, 90.1% | 0.0%, n/a | 77.1%, 90.8% | n/a | 90.81% | 90.55% |
| MiniLM | 57.2%, 91.9% | 1.4%, 94.1% | 41.4%, 91.2% | 75.73% | 95.14% | 94.89% |
| BERT base cased | 69.8%, 93.5% | 0.8%, 95.0% | 29.4%, 94.1% | 80.63% | 97.59% | 97.36% |

<!-- markdownlint-enable MD013 -->

On the 12,230 held-out sentences of the trained lemmas the fine top-1 is 80.21% (MiniLM) and 85.44% (BERT). The Basic
table was retrained from the legacy corpus only to match the new word image (it has no fine senses).

## Suite

The 348-row suite of common ambiguous words (lists H, M and C, hand-authored sentences that no training row contains) was
run with the new models and scored against the earlier baseline. The suite labels of the lists H and C are the retired
class ids; they were mapped to fine senses with the class map: a row is right when the most probable sense is a sense of
the class ("fine"), and "part of speech" scores the selected analysis (its lemma and part of speech, and the tense
features of `read`) only. The rows of `used` follow the old corpus inversion in the suite, so they are scored by meaning
(habitual = `use#used-to`). The list M is scored as before (the part of speech).

<!-- markdownlint-disable MD013 -->

| List | Basic before / now | MiniLM before / now (part of speech) | BERT before / now (part of speech) | MiniLM now (fine) | BERT now (fine) |
| --- | ---: | ---: | ---: | ---: | ---: |
| H (132) | 82.6% / 88.6% | 89.4% / 93.9% | 96.2% / 97.0% | 87.9% | 90.9% |
| M (124) | 92.7% / 92.7% | 65.3% / 93.5% | 66.9% / 96.0% | 93.5% | 96.0% |
| C (92) | 91.3% / 94.6% | 96.7% / 96.7% | 100.0% / 98.9% | 85.9% | 91.3% |
| All (348) | 88.5% / 91.7% | 82.8% / 94.5% | 86.8% / 97.1% | 89.4% | 92.8% |

<!-- markdownlint-enable MD013 -->

The earlier baseline scored the retired classes (a pronunciation or part-of-speech class per word); the new "part of
speech" column is the comparable one, the "fine" column is stricter. The list M gained 28 to 29 points because the
encoders now have weights for the 764 lemmas instead of 20. The lexicon has no gap row any more (`August`, `Polish`).
The remaining misses of BERT are rows whose lemma has weights but whose form needs a chooser that the corpus cannot
train yet (`used`: the adjective `used` has no rows; `dove`, `polish`) and the capitalized lemmas.

## Decisions for the owner

- **Gemma flag**: the free second labeler could not run (see above); the relabeling is primary-only (sol, 93.7% on the
  calibration set). Run gemma again when the server answers, to flag the 25,373 judged rows.
- **CC BY-SA rows**: the 3,054 rows are excluded from the encoder corpus and copied (not moved) to
  `corpus/legacy/excluded-cc-by-sa/`, because the Basic table is trained from the unchanged legacy files (its table must
  stay reproducible). Retrain Basic without them, or keep it as it is.
- **Legacy folder and class map** stay (Basic trains from them; the class map is read by its trainer). They go together
  when Basic no longer needs them.
- **Choosers** need rows for the other lemma of every participle form (243 forms): the quota filling is the place.
