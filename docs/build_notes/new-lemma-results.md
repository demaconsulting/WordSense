# New Lemma Results (Phase 4, Batch 1)

Phase 4 adds new lemmas to the table from real text. This page records batch 1: thirteen lemmas with 28 senses. The
sense inventory and the choice of lemmas are in `new-lemma-decisions.md`. Every sentence of this batch is a real
sentence (Tatoeba, Project Gutenberg, UD EWT or a Wikipedia sentence); the sentences were judged blind by two judges
against the sense menu of the lemma, and a third judge decided the disagreements. The project wrote no sentence in
this step. Four senses are short of the thresholds of 14 anchors and 8 holdout rows and wait for authored sentences.

## Method

1. `real-judge-merge` merged the judge files of the 1,640 candidates (1,529 local and 111 Wikipedia sentences) into
   `corpus/audit/real-sense-audit-9.tsv`: 1,610 rows agreed, 26 were adjudicated, 4 stayed undecided and 64 were `none`.
   The blind judges A and B agreed on 1,610 of 1,640 rows (98.2 %); judge C decided the other 30.
2. `real-import` ran with all earlier candidate files, the audits 1 to 9 and the two new candidate files
   (`wiki.tsv` and the local harvest of batch 1). The import is cumulative and rewrites `real-restore.tsv` from the
   whole plan. It only added rows: the earlier rows of `real-restore.tsv` are unchanged (`git diff` shows 0 deleted
   lines). Because the import counts the rows of `real-gap-1.tsv` as existing rows of the same lemmas, that file was
   moved out of `corpus/anchors` and `corpus/holdout` for the duration of the run, as in Phase 3a; otherwise the
   earlier `present` rows would have been cut back by the cap.
3. Forms for all thirteen lemmas (the lemma and its inflected forms, labelled with the senses) were added to
   `corpus/forms/speech.tsv`, the only forms file the builder reads.
4. The table was rebuilt with the `build` command. `validate-provenance`, `validate-pos` and the real-text evaluation
   were run.

## Per Lemma

Anchors and holdout are the counts of the lemma after the import (all real). Holdout accuracy is the best-sense
accuracy of the build report on the holdout rows of the lemma, which are real sentences for every batch-1 lemma.

| Lemma | Senses | Anchors | Holdout | Holdout accuracy | Answered accuracy | Coverage | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `contact` | 2 | 40 | 20 | 100.00 % | 100.00 % | 100.00 % | Trained |
| `impact` | 2 | 40 | 20 | 90.00 % | 94.74 % | 95.00 % | Trained, below 95 % |
| `address` | 2 | 40 | 20 | 100.00 % | 100.00 % | 100.00 % | Trained |
| `transfer` | 2 | 40 | 20 | 95.00 % | 93.75 % | 80.00 % | Trained |
| `update` | 2 | 40 | 20 | 100.00 % | 100.00 % | 90.00 % | Trained |
| `protest` | 2 | 40 | 20 | 95.00 % | 100.00 % | 65.00 % | Trained |
| `export` | 2 | 40 | 20 | 85.00 % | 100.00 % | 65.00 % | Trained, below 95 % |
| `upgrade` | 2 | 37 | 19 | 100.00 % | 100.00 % | 100.00 % | Trained |
| `combat` | 2 | 40 | 20 | 95.00 % | 95.00 % | 100.00 % | Trained |
| `collect` | 2 | 28 | 14 | 100.00 % | 100.00 % | 100.00 % | Provisional: `collect-prayer` short |
| `accent` | 2 | 24 | 12 | 91.67 % | 90.91 % | 91.67 % | Provisional: `accent-verb` short |
| `detail` | 2 | 28 | 14 | 85.71 % | 85.71 % | 100.00 % | Provisional: `detail-verb` short |
| `second` | 3 | 57 | 29 | 86.21 % | 87.50 % | 82.76 % | Provisional: `second-transfer` has no rows |

## Per Sense

| Lemma | Sense | Anchors | Holdout | Result |
| --- | --- | ---: | ---: | --- |
| `contact` | `contact-noun`, `contact-verb` | 20, 20 | 10, 10 | Complete |
| `impact` | `impact-noun`, `impact-verb` | 20, 20 | 10, 10 | Complete |
| `address` | `address-noun`, `address-verb` | 20, 20 | 10, 10 | Complete |
| `transfer` | `transfer-noun`, `transfer-verb` | 20, 20 | 10, 10 | Complete |
| `update` | `update-noun`, `update-verb` | 20, 20 | 10, 10 | Complete |
| `protest` | `protest-noun`, `protest-verb` | 20, 20 | 10, 10 | Complete |
| `export` | `export-noun`, `export-verb` | 20, 20 | 10, 10 | Complete |
| `upgrade` | `upgrade-noun`, `upgrade-verb` | 17, 20 | 9, 10 | Complete |
| `combat` | `combat-noun`, `combat-verb` | 20, 20 | 10, 10 | Complete |
| `collect` | `collect-verb` | 20 | 10 | Complete |
| `collect` | `collect-prayer` | 8 | 4 | Deficit |
| `accent` | `accent-noun` | 20 | 10 | Complete |
| `accent` | `accent-verb` | 4 | 2 | Deficit |
| `detail` | `detail-noun` | 20 | 10 | Complete |
| `detail` | `detail-verb` | 8 | 4 | Deficit |
| `second` | `second-ordinal-adj`, `second-noun` | 20, 20 | 10, 10 | Complete |
| `second` | `second-support` | 17 | 9 | Complete |
| `second` | `second-transfer` | 0 | 0 | Deficit |

## Deficits

Real text is exhausted for these senses, so they wait for authored sentences (the parent step, `real-authored-import`):

| Lemma | Sense | Anchors | Holdout | Anchor shortfall | Holdout shortfall | Real pool |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `collect` | `collect-prayer` | 8 | 4 | 6 | 4 | 12 |
| `accent` | `accent-verb` | 4 | 2 | 10 | 6 | 6 |
| `detail` | `detail-verb` | 8 | 4 | 6 | 4 | 12 |
| `second` | `second-transfer` | 0 | 0 | 14 | 8 | 0 |

The builder can train a lemma whose rare sense is thin (it only warns that a sense has fewer than 8 anchors), so the
four lemmas stay in this rebuild; `second` is trained with its three senses that have rows and `second-transfer` is not
yet in the sense file (it has no row) and not in the forms labels. These four lemmas are provisional: their figures
change when the authored sentences arrive. Their other senses reach the caps of 20 anchors and 10 holdout rows, so only
the one named sense per lemma needs authored sentences.

## Overall Figures

| Figure | Phase 3b baseline | After batch 1 |
| --- | ---: | ---: |
| Lemmas in the table | 352 | 365 |
| Senses | 726 | 753 |
| Overall holdout, best sense | 96.17 % | 96.08 % (6,384 rows) |
| Overall holdout, group | 96.37 % | 96.33 % |
| Real-text evaluation (`eval-seed.tsv`, 689 rows) | 92.60 % | 92.60 % |
| Real restored-sense holdout (`real-restore.tsv`) | 86.20 % (297 rows) | 89.84 % (551 rows, builder report) |

The overall holdout figure falls by 0.09 points because the new senses are harder than the average and the four
provisional lemmas are thin; 13 of the 365 lemmas are new. The real-text evaluation set (`eval-seed.tsv`) does not
contain any batch-1 lemma, so its figures are unchanged by the new lemmas and show that the new rows did not disturb
the earlier ones. The `real-eval` report still evaluates the 297 earlier rows of `real-restore.tsv`; the 248 batch-1
holdout rows are measured by the build report and appear in the per-lemma table above.
