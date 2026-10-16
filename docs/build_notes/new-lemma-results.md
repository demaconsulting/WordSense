# New Lemma Results (Phase 4)

Phase 4 adds new lemmas to the table. This page records batch 1 first, batches 2 to 5 after it
(see [Batches 2 to 5](#batches-2-to-5)) and the final state after the authored sentences
(see [Final Results](#final-results)). Batch 1: thirteen lemmas with 28 senses. The
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

## Batches 2 to 5

Batches 2 to 5 add 50 lemmas with 109 senses in the plan (13, 13, 13 and 11 lemmas). They follow the method of batch 1
and, as in batch 1, every sentence is a real sentence (Tatoeba, Project Gutenberg, UD EWT or Wikipedia) and the project
wrote no sentence in this step.

### Method of Batches 2 to 5

1. `real-judge-merge` merged the blind judge files into `corpus/audit/real-sense-audit-10.tsv` to
   `real-sense-audit-13.tsv` (one file per batch). The blind judges A and B agreed on 3,596 of the 3,779 candidates
   (95.2 %); judge C decided the others.

   | Batch | Audit | Rows | Agree | Adjudicated | Undecided | None | A and B agree |
   | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
   | 2 | 10 | 1,147 | 1,100 | 45 | 2 | 37 | 95.9 % |
   | 3 | 11 | 866 | 856 | 10 | 0 | 28 | 98.8 % |
   | 4 | 12 | 1,152 | 1,086 | 52 | 14 | 44 | 94.3 % |
   | 5 | 13 | 614 | 554 | 47 | 13 | 26 | 90.2 % |

   In batch 2, 30 judge lines had no confidence column; they were repaired by inserting `M` before the merge. The
   figures above include those lines.
2. `real-import` ran once with all earlier candidate files, the candidate files of batches 1 to 5 (the local
   harvest and the Wikipedia sentences of each batch) and the audits 1 to 13. As in batch 1, `real-gap-1.tsv` was moved
   out of `corpus/anchors` and `corpus/holdout` for the run and restored afterwards. `git diff` shows 0 deleted lines
   in `real-restore.tsv` (anchors, holdout, provenance and senses), so the rows of earlier lemmas are unchanged.
3. Forms for the lemmas that have rows were added to `corpus/forms/speech.tsv` (131 rows; the last six were found by
   the corpus-target test for inflections of the rarer senses). A plural or third-person
   `-s` form carries every noun and verb sense of its lemma, so that the forms file does not hide the verb sense of a
   form the inventory lists only for the noun. Senses with no rows are not in the senses file and have no label.
4. The builder cannot train a lemma that has anchors for fewer than two senses. Twelve lemmas have only one sense with
   rows (the other sense has no row or too few real sentences to be imported): `recreate`, `prefix`, `discord`,
   `alloy`, `sewer`, `alum`, `costume`, `romance`, `excerpt`, `egress`, `predate` and `forbear`. Their rows were
   held back: they were removed from the four `real-restore.tsv` files (211 anchor, 108 holdout, 12 sense and 319
   provenance rows) and not given forms, and are kept in `audit-work/p4b2-5/held-back/` (not versioned). The lemmas wait
   for authored sentences; the next `real-import` run regenerates their real rows.
5. The table was rebuilt, then `validate-provenance`, `validate-pos` (833 senses) and the real-text evaluation were run.

### Per Lemma of Batches 2 to 5

Counts are those of the lemma after the import. Holdout accuracy is the best-sense accuracy of the build report on the
holdout rows of the lemma. A lemma is provisional while one of its senses is short of 14 anchors or 8 holdout rows; the
builder trains thin senses (it only warns) so these lemmas stay in the table until the authored sentences arrive.

#### Batch 2

| Lemma | Senses | Anchors | Holdout | Holdout accuracy | Answered accuracy | Coverage | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `resign` | 2 | 25 | 12 | 100.00 % | 100.00 % | 100.00 % | Provisional: `resign-sign-again` short |
| `reset` | 2 | 40 | 20 | 90.00 % | 100.00 % | 80.00 % | Trained, below 95 % |
| `intern` | 3 | 42 | 21 | 100.00 % | 100.00 % | 57.14 % | Provisional: `intern-train` short |
| `recreate` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `perfume` | 2 | 35 | 18 | 100.00 % | 100.00 % | 100.00 % | Trained |
| `dictate` | 2 | 31 | 16 | 87.50 % | 86.67 % | 93.75 % | Provisional: `dictate-noun` short, below 95 % |
| `recess` | 2 | 28 | 14 | 100.00 % | 100.00 % | 100.00 % | Provisional: `recess-verb` short |
| `complex` | 2 | 40 | 20 | 95.00 % | 100.00 % | 85.00 % | Trained |
| `frequent` | 2 | 40 | 20 | 100.00 % | 100.00 % | 80.00 % | Trained |
| `ally` | 2 | 33 | 16 | 93.75 % | 100.00 % | 93.75 % | Provisional: `ally-verb` short, below 95 % |
| `traverse` | 2 | 25 | 12 | 100.00 % | 100.00 % | 91.67 % | Provisional: `traverse-noun` short |
| `prefix` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `suffix` | 2 | 24 | 12 | 100.00 % | 100.00 % | 91.67 % | Provisional: `suffix-verb` short |

#### Batch 3

| Lemma | Senses | Anchors | Holdout | Holdout accuracy | Answered accuracy | Coverage | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `discord` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `recap` | 2 | 12 | 5 | 100.00 % | 100.00 % | 80.00 % | Provisional: `recap-noun`, `recap-verb` short |
| `advert` | 2 | 27 | 14 | 100.00 % | 100.00 % | 100.00 % | Provisional: `advert-verb` short |
| `alloy` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `primer` | 2 | 23 | 11 | 100.00 % | 100.00 % | 90.91 % | Provisional: `primer-paint`, `primer-textbook` short |
| `intermediate` | 2 | 31 | 16 | 87.50 % | 87.50 % | 100.00 % | Provisional: both senses short, <95 % |
| `wicked` | 2 | 24 | 12 | 100.00 % | 100.00 % | 100.00 % | Provisional: `wicked-wick` short |
| `tier` | 3 | 35 | 18 | 100.00 % | 100.00 % | 77.78 % | Provisional: `tier-verb`, `tier-one-who-ties` short |
| `tower` | 2 | 40 | 20 | 100.00 % | 100.00 % | 80.00 % | Provisional: `tower-one-who-tows` short |
| `shower` | 2 | 40 | 20 | 95.00 % | 93.75 % | 80.00 % | Provisional: `shower-one-who-shows` short |
| `sewer` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `forearm` | 2 | 24 | 12 | 100.00 % | 100.00 % | 100.00 % | Provisional: `forearm-verb` short |
| `alum` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |

#### Batch 4

| Lemma | Senses | Anchors | Holdout | Holdout accuracy | Answered accuracy | Coverage | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `supposed` | 3 | 60 | 30 | 93.33 % | 94.74 % | 63.33 % | Trained, below 95 % |
| `blessed` | 2 | 40 | 20 | 90.00 % | 94.12 % | 85.00 % | Trained, below 95 % |
| `august` | 2 | 40 | 20 | 95.00 % | 100.00 % | 85.00 % | Trained |
| `defense` | 2 | 40 | 20 | 100.00 % | 100.00 % | 95.00 % | Trained |
| `bases` | 2 | 24 | 11 | 100.00 % | 100.00 % | 100.00 % | Provisional: `bases-verb`, `bases-basis` short |
| `costume` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `romance` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `submarine` | 2 | 39 | 20 | 85.00 % | 88.89 % | 90.00 % | Trained, below 95 % |
| `excerpt` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `annex` | 2 | 40 | 20 | 100.00 % | 100.00 % | 95.00 % | Trained |
| `slough` | 2 | 27 | 14 | 100.00 % | 100.00 % | 92.86 % | Provisional: `slough-skin`, `slough-shed` short |
| `tarry` | 2 | 26 | 13 | 92.31 % | 100.00 % | 84.62 % | Provisional: `tarry-tar` short, below 95 % |
| `underscore` | 2 | 31 | 16 | 100.00 % | 100.00 % | 87.50 % | Provisional: `underscore-noun` short |

#### Batch 5

| Lemma | Senses | Anchors | Holdout | Holdout accuracy | Answered accuracy | Coverage | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `gallant` | 3 | 47 | 23 | 78.26 % | 85.00 % | 86.96 % | Provisional: `gallant-suitor` short, below 95 % |
| `mow` | 2 | 24 | 12 | 100.00 % | 100.00 % | 100.00 % | Provisional: `mow-haymow` short |
| `multiply` | 2 | 29 | 14 | 100.00 % | 100.00 % | 92.86 % | Provisional: `multiply-adverb` short |
| `reprimand` | 2 | 36 | 19 | 89.47 % | 92.86 % | 73.68 % | Trained, below 95 % |
| `retrofit` | 2 | 17 | 8 | 100.00 % | 100.00 % | 100.00 % | Provisional: `retrofit-noun`, `retrofit-verb` short |
| `egress` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `agape` | 2 | 29 | 14 | 92.86 % | 100.00 % | 57.14 % | Provisional: `agape-open` short, below 95 % |
| `predate` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |
| `ellipses` | 2 | 16 | 8 | 87.50 % | 100.00 % | 75.00 % | Provisional: both `ellipses-*` senses short, <95 % |
| `adduct` | 2 | 24 | 12 | 100.00 % | 100.00 % | 100.00 % | Provisional: `adduct-noun`, `adduct-verb` short |
| `forbear` | 2 | - | - | - | - | - | Held back: fewer than two senses with rows |

The 12 held-back lemmas have no row in the table. Of the 38 other lemmas, 11 are fully trained and 27 are provisional.
The senses of a held-back lemma that have real rows (for example `alum-mineral`) are complete or thin as listed below
and are held back only because their lemma cannot train with one sense.

### Deficits of Batches 2 to 5

The list is in `audit-work/p4b2-5/deficits.tsv` (batches 2 to 5) and `audit-work/p4-deficits.tsv` (with the four
batch-1 senses); `audit-work/p4-deficit-menu.tsv` has the sense menu (id, lemma, forms, tags, definition, examples) of
every deficit sense for the authoring step. A sense is a deficit when it has fewer than 14 anchors or 8 holdout rows.
The real pool is the number of accepted real sentences. For senses with a pool below four, the import wrote no rows, so
they have 0 anchors and 0 holdout rows although the pool is not empty. Counts for the held-back lemmas are the counts of
the rows that were held back.

| Lemma | Sense | Anchors | Holdout | Anchor shortfall | Holdout shortfall | Real pool |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `resign` | `resign-sign-again` | 5 | 2 | 9 | 6 | 7 |
| `intern` | `intern-train` | 6 | 3 | 8 | 5 | 9 |
| `recreate` | `recreate-refresh` | 0 | 0 | 14 | 8 | 3 |
| `dictate` | `dictate-noun` | 11 | 6 | 3 | 2 | 17 |
| `recess` | `recess-verb` | 8 | 4 | 6 | 4 | 12 |
| `ally` | `ally-verb` | 13 | 6 | 1 | 2 | 19 |
| `traverse` | `traverse-noun` | 5 | 2 | 9 | 6 | 7 |
| `prefix` | `prefix-verb` | 0 | 0 | 14 | 8 | 3 |
| `suffix` | `suffix-verb` | 4 | 2 | 10 | 6 | 6 |
| `discord` | `discord-verb` | 0 | 0 | 14 | 8 | 1 |
| `recap` | `recap-noun` | 6 | 2 | 8 | 6 | 8 |
| `recap` | `recap-verb` | 6 | 3 | 8 | 5 | 9 |
| `advert` | `advert-verb` | 7 | 4 | 7 | 4 | 11 |
| `alloy` | `alloy-verb` | 0 | 0 | 14 | 8 | 4 |
| `primer` | `primer-paint` | 10 | 5 | 4 | 3 | 15 |
| `primer` | `primer-textbook` | 13 | 6 | 1 | 2 | 19 |
| `intermediate` | `intermediate-noun` | 11 | 6 | 3 | 2 | 17 |
| `intermediate` | `intermediate-verb` | 0 | 0 | 14 | 8 | 0 |
| `wicked` | `wicked-wick` | 4 | 2 | 10 | 6 | 6 |
| `tier` | `tier-verb` | 11 | 6 | 3 | 2 | 17 |
| `tier` | `tier-one-who-ties` | 4 | 2 | 10 | 6 | 6 |
| `tower` | `tower-one-who-tows` | 0 | 0 | 14 | 8 | 0 |
| `shower` | `shower-one-who-shows` | 0 | 0 | 14 | 8 | 0 |
| `sewer` | `sewer-seamstress` | 0 | 0 | 14 | 8 | 0 |
| `forearm` | `forearm-verb` | 4 | 2 | 10 | 6 | 6 |
| `alum` | `alum-mineral` | 9 | 5 | 5 | 3 | 14 |
| `alum` | `alum-graduate` | 0 | 0 | 14 | 8 | 4 |
| `bases` | `bases-verb` | 4 | 1 | 10 | 7 | 5 |
| `bases` | `bases-basis` | 0 | 0 | 14 | 8 | 4 |
| `costume` | `costume-verb` | 0 | 0 | 14 | 8 | 2 |
| `romance` | `romance-verb` | 0 | 0 | 14 | 8 | 2 |
| `excerpt` | `excerpt-verb` | 0 | 0 | 14 | 8 | 3 |
| `slough` | `slough-skin` | 0 | 0 | 14 | 8 | 1 |
| `slough` | `slough-shed` | 7 | 4 | 7 | 4 | 11 |
| `tarry` | `tarry-tar` | 6 | 3 | 8 | 5 | 9 |
| `underscore` | `underscore-noun` | 11 | 6 | 3 | 2 | 17 |
| `gallant` | `gallant-suitor` | 9 | 4 | 5 | 4 | 13 |
| `mow` | `mow-haymow` | 4 | 2 | 10 | 6 | 6 |
| `multiply` | `multiply-adverb` | 9 | 4 | 5 | 4 | 13 |
| `retrofit` | `retrofit-noun` | 8 | 4 | 6 | 4 | 12 |
| `retrofit` | `retrofit-verb` | 9 | 4 | 5 | 4 | 13 |
| `egress` | `egress-verb` | 0 | 0 | 14 | 8 | 2 |
| `agape` | `agape-open` | 13 | 6 | 1 | 2 | 19 |
| `predate` | `predate-earlier` | 7 | 4 | 7 | 4 | 11 |
| `predate` | `predate-prey` | 0 | 0 | 14 | 8 | 2 |
| `ellipses` | `ellipses-ellipse` | 7 | 4 | 7 | 4 | 11 |
| `ellipses` | `ellipses-ellipsis` | 9 | 4 | 5 | 4 | 13 |
| `adduct` | `adduct-noun` | 11 | 5 | 3 | 3 | 16 |
| `adduct` | `adduct-verb` | 13 | 7 | 1 | 1 | 20 |
| `forbear` | `forbear-ancestor` | 0 | 0 | 14 | 8 | 2 |

Together with the four batch-1 senses this is 54 deficit senses; all of them wait for authored sentences, so the
lemmas that are in the table with a short sense are provisional.

### Overall Figures After Batches 2 to 5

| Figure | After batch 1 | After batches 2 to 5 |
| --- | ---: | ---: |
| Lemmas in the table | 365 | 403 |
| Senses | 753 | 833 |
| Anchors, holdout rows | 10,900, 6,384 | 12,108, 6,987 |
| Overall holdout, best sense | 96.08 % | 96.05 % (6,987 rows) |
| Overall holdout, group | 96.33 % | 96.32 % |
| Real-text evaluation (`eval-seed.tsv`, 689 rows) | 92.60 % | 92.60 % |
| Real restored-sense holdout (`real-restore.tsv`, builder report) | 89.84 % (551 rows) | 92.81 % (1,154 rows) |

The 603 new holdout rows of `real-restore.tsv` score about 95.5 %. The `real-eval` report now evaluates 545 rows of
`real-restore.tsv` (89.72 %); the 689 rows of `eval-seed.tsv` contain no new lemma and are unchanged.

## Final Results

The authored step completed Phase 4. This section is the final state: 63 accepted lemmas, none dropped, 137 senses.

### Method of the Authored Step

1. The 12 held-back lemmas were restored: their real rows (211 anchor, 108 holdout, 12 sense and 319 provenance rows)
   went back into the four `real-restore.tsv` files and their forms (42 rows) into `corpus/forms/speech.tsv`. A
   verb form (`intermediated`, `intermediating`) was added for `intermediate`.
2. The 16 top-up holdout rows for `predate-earlier` and `predate-prey` passed the checks of the preparation step (forms
   of the menu, braces, at most 35 words, no duplicate, not in the corpus). **They were not blind-judged.** See
   `new-lemma-decisions.md`.
3. `real-authored-import --name new-authored-1` imported 576 anchor rows (author `authored-anchor`) and 398 holdout
   rows (author `authored-holdout`, 382 accepted plus the 16 top-up rows) and declared 18 new senses in
   `senses/new-authored-1.tsv`. Provenance flags every one of these rows `authored`. No sense is short of 14 anchors or
   8 holdout rows (`resign-sign-again` has exactly 8 holdout rows).
4. The table was rebuilt (415 lemmas, 863 senses). `validate-provenance`, `validate-pos` (863 senses), the real-text
   evaluation and the unit tests (575 passed) are green. The build warns about no sense with fewer than 8 anchors.
5. No lemma had a shortfall or could not be trained, so **no lemma was dropped** (0 lemmas, 0 rows, 0 forms removed).

### Per Lemma After Authoring

Counts are of the lemma after the import (all senses). Anchors and holdout are real / authored. The accuracy columns
are best-sense accuracies of the build report on the holdout rows of the lemma: real-only and authored-only rows (an
empty split is `-`), all rows (All), and answered accuracy and coverage with abstention (Answered, Coverage), which
cover all holdout rows of the lemma. **Authored accuracy is optimistic:** the sentences were written for a known sense
menu by an author who knew what the senses are, and the 16 top-up rows were not judged.

| Lemma | Senses | Anchors real / auth. | Holdout real / auth. | Real-only | Authored-only | All | Answered | Coverage |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `contact` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 100.00 % |
| `impact` | 2 | 40 / 0 | 20 / 0 | 90.00 % | - | 90.00 % | 94.74 % | 95.00 % |
| `address` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 100.00 % |
| `transfer` | 2 | 40 / 0 | 20 / 0 | 95.00 % | - | 95.00 % | 93.75 % | 80.00 % |
| `update` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 90.00 % |
| `protest` | 2 | 40 / 0 | 20 / 0 | 95.00 % | - | 95.00 % | 100.00 % | 65.00 % |
| `collect` | 2 | 28 / 8 | 14 / 6 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 95.00 % |
| `export` | 2 | 40 / 0 | 20 / 0 | 85.00 % | - | 85.00 % | 100.00 % | 65.00 % |
| `accent` | 2 | 24 / 12 | 12 / 8 | 91.67 % | 62.50 % | 80.00 % | 88.89 % | 90.00 % |
| `upgrade` | 2 | 37 / 0 | 19 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 100.00 % |
| `second` | 4 | 57 / 16 | 29 / 10 | 86.21 % | 100.00 % | 89.74 % | 90.32 % | 79.49 % |
| `detail` | 2 | 28 / 8 | 14 / 6 | 85.71 % | 83.33 % | 85.00 % | 94.12 % | 85.00 % |
| `combat` | 2 | 40 / 0 | 20 / 0 | 95.00 % | - | 95.00 % | 95.00 % | 100.00 % |
| `resign` | 2 | 25 / 11 | 12 / 6 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `reset` | 2 | 40 / 0 | 20 / 0 | 90.00 % | - | 90.00 % | 100.00 % | 80.00 % |
| `intern` | 3 | 42 / 10 | 21 / 7 | 85.71 % | 71.43 % | 82.14 % | 100.00 % | 64.29 % |
| `recreate` | 2 | 20 / 16 | 10 / 10 | 90.00 % | 90.00 % | 90.00 % | 100.00 % | 75.00 % |
| `perfume` | 2 | 35 / 0 | 18 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 100.00 % |
| `dictate` | 2 | 31 / 5 | 16 / 4 | 81.25 % | 100.00 % | 85.00 % | 88.89 % | 90.00 % |
| `recess` | 2 | 28 / 8 | 14 / 6 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 80.00 % |
| `complex` | 2 | 40 / 0 | 20 / 0 | 95.00 % | - | 95.00 % | 100.00 % | 85.00 % |
| `frequent` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 80.00 % |
| `ally` | 2 | 33 / 3 | 16 / 4 | 93.75 % | 100.00 % | 95.00 % | 100.00 % | 90.00 % |
| `traverse` | 2 | 25 / 10 | 12 / 8 | 91.67 % | 100.00 % | 95.00 % | 100.00 % | 95.00 % |
| `prefix` | 2 | 20 / 16 | 10 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 85.00 % |
| `suffix` | 2 | 24 / 12 | 12 / 8 | 100.00 % | 62.50 % | 85.00 % | 100.00 % | 70.00 % |
| `discord` | 2 | 20 / 16 | 10 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `recap` | 2 | 12 / 20 | 5 / 15 | 100.00 % | 93.33 % | 95.00 % | 100.00 % | 75.00 % |
| `advert` | 2 | 27 / 9 | 14 / 6 | 100.00 % | 83.33 % | 95.00 % | 95.00 % | 100.00 % |
| `alloy` | 2 | 20 / 16 | 10 / 10 | 100.00 % | 90.00 % | 95.00 % | 100.00 % | 75.00 % |
| `primer` | 2 | 23 / 9 | 11 / 9 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 95.00 % |
| `intermediate` | 3 | 31 / 21 | 16 / 14 | 87.50 % | 100.00 % | 93.33 % | 93.33 % | 100.00 % |
| `wicked` | 2 | 24 / 11 | 12 / 8 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `tier` | 3 | 35 / 17 | 18 / 12 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 93.33 % |
| `tower` | 3 | 40 / 16 | 20 / 10 | 100.00 % | 90.00 % | 96.67 % | 100.00 % | 80.00 % |
| `shower` | 3 | 40 / 16 | 20 / 10 | 95.00 % | 80.00 % | 90.00 % | 89.66 % | 96.67 % |
| `sewer` | 2 | 20 / 16 | 10 / 10 | 90.00 % | 100.00 % | 95.00 % | 95.00 % | 100.00 % |
| `forearm` | 2 | 24 / 12 | 12 / 8 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `alum` | 2 | 9 / 23 | 5 / 15 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 95.00 % |
| `supposed` | 3 | 60 / 0 | 30 / 0 | 93.33 % | - | 93.33 % | 94.44 % | 60.00 % |
| `blessed` | 2 | 40 / 0 | 20 / 0 | 90.00 % | - | 90.00 % | 94.12 % | 85.00 % |
| `august` | 2 | 40 / 0 | 20 / 0 | 95.00 % | - | 95.00 % | 100.00 % | 85.00 % |
| `defense` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 95.00 % |
| `bases` | 3 | 24 / 28 | 11 / 19 | 100.00 % | 94.74 % | 96.67 % | 100.00 % | 76.67 % |
| `costume` | 2 | 20 / 15 | 10 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `romance` | 2 | 20 / 15 | 10 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `submarine` | 2 | 39 / 0 | 20 / 0 | 85.00 % | - | 85.00 % | 88.89 % | 90.00 % |
| `excerpt` | 2 | 16 / 16 | 9 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 94.74 % |
| `annex` | 2 | 40 / 0 | 20 / 0 | 100.00 % | - | 100.00 % | 100.00 % | 95.00 % |
| `slough` | 3 | 27 / 25 | 14 / 16 | 92.86 % | 87.50 % | 90.00 % | 100.00 % | 60.00 % |
| `tarry` | 2 | 26 / 10 | 13 / 7 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `underscore` | 2 | 31 / 5 | 16 / 4 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `gallant` | 3 | 47 / 7 | 23 / 6 | 78.26 % | 100.00 % | 82.76 % | 87.50 % | 82.76 % |
| `mow` | 2 | 24 / 12 | 12 / 8 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 95.00 % |
| `multiply` | 2 | 29 / 7 | 14 / 6 | 92.86 % | 83.33 % | 90.00 % | 94.12 % | 85.00 % |
| `reprimand` | 2 | 36 / 0 | 19 / 0 | 94.74 % | - | 94.74 % | 92.86 % | 73.68 % |
| `retrofit` | 2 | 17 / 15 | 8 / 12 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 85.00 % |
| `egress` | 2 | 19 / 16 | 10 / 10 | 100.00 % | 60.00 % | 80.00 % | 94.12 % | 85.00 % |
| `agape` | 2 | 29 / 3 | 14 / 4 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 72.22 % |
| `predate` | 2 | 7 / 25 | 4 / 16 | 100.00 % | 75.00 % | 80.00 % | 80.00 % | 100.00 % |
| `ellipses` | 2 | 16 / 16 | 8 / 12 | 87.50 % | 100.00 % | 95.00 % | 100.00 % | 95.00 % |
| `adduct` | 2 | 24 / 8 | 12 / 8 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| `forbear` | 2 | 20 / 16 | 10 / 10 | 100.00 % | 100.00 % | 100.00 % | 100.00 % | 100.00 % |

Totals over the 63 lemmas: 1,913 real and 576 authored anchors, 959 real and 398 authored holdout rows. Best-sense
accuracy is 94.99 % on the real holdout rows, 92.96 % on the authored rows and 94.40 % on all 1,357 rows. The authored
figure is not higher than the real one here because the authored rows cover the rarest senses (for example the one who
shows, ties or tows readings), which are harder for the model; it is nevertheless an optimistic figure, because the
authored holdout was written by an author who knew the senses and 16 rows were not judged. Treat the real-only column
as the honest measure.

The weakest lemmas (holdout accuracy at or below 85 %) are `accent`, `detail`, `suffix`, `submarine`, `export`, `intern`,
`gallant`, `egress` and `predate`. The real and authored columns are listed per sense in
`corpus/audit/new-lemma-speech-map.tsv` (see below).

### Decisions per Doubtful Word

| Lemma | Decision | Outcome |
| --- | --- | --- |
| `gas`, `mouse`, `automobile`, `finance` | Rejected | Not in the table (uncorroborated reading or free variation) |
| `alum` | Accepted | Trained; `alum-graduate` is authored only (16 anchors, 10 holdout); 100.00 % holdout |
| `sewer` | Accepted | Trained; `sewer-seamstress` is authored only; 95.00 % holdout |
| `primer` | Accepted | Trained; both senses have real and authored rows; 100.00 % holdout |
| `shower` | Accepted | Trained; `shower-one-who-shows` is authored only; 80.00 % on that sense, 90.00 % for the lemma |
| `tier` | Accepted | Trained; `tier-one-who-ties` is mostly authored (6 real, 20 authored); 100.00 % holdout |
| `tower` | Accepted | Trained; `tower-one-who-tows` is authored only; 90.00 % on that sense, 96.67 % for the lemma |

None of the six accepted doubtful words was dropped.

### Speech Mapping File

`corpus/audit/new-lemma-speech-map.tsv` (inside this repository) maps every sense of the Phase 4 lemmas to its
pronunciation. Columns: `lemma`, `sense`, `pos`, `group` (pronunciation group), `arpabet` (the ARPAbet of the sense),
`anchors` and `holdout` (counts), `source_mix` (`real=N;authored=M` over anchors and holdout), `holdout_accuracy`
(best-sense accuracy of the sense in percent) and `status` (`trained` or `dropped`). The sense inventory is
`tools/real-text/new-lemma-inventory.tsv`; a lemma that was dropped would appear with status `dropped` and empty counts.
No lemma was dropped, so every row is `trained` (137 rows).

### Overall Figures After Authoring

| Figure | Phase 3b baseline | After batches 2 to 5 | Final |
| --- | ---: | ---: | ---: |
| Lemmas in the table | 352 | 403 | 415 |
| Senses | 726 | 833 | 863 |
| Anchors, holdout rows | not recorded, 6,136 | 12,108, 6,987 | 12,895, 7,493 |
| Overall holdout, best sense | 96.17 % | 96.05 % | 95.86 % (7,493 rows) |
| Overall holdout, group | 96.37 % | 96.32 % | 96.20 % |
| With abstention | 98.54 % at 90.50 % | 98.38 % at 90.11 % | 98.32 % at 89.92 % |
| Noun versus verb, best tag | 97.03 % (5,459 rows) | 96.94 % (6,178 rows) | 96.84 % (6,667 rows) |
| Real-text evaluation (`eval-seed.tsv`, 689 rows) | 92.60 % | 92.60 % | 92.60 % (96.57 % of the 84.62 % answered) |
| Real restored-sense holdout (`real-restored`) | 86.20 % | 89.72 % (545 rows) | 92.91 % (1,256 rows) |

The overall holdout figure falls by 0.31 points against the Phase 3b baseline because 63 new lemmas with rare senses
are harder than the average (the 63 lemmas score 94.40 % on their own 1,357 holdout rows) and because the authored
senses are measured by sentences with a different style. The real-text evaluation is unchanged: it contains no new
lemma, so the new rows did not disturb the earlier ones. The authored holdout file `new-authored-1.tsv` scores 92.96 %
(398 rows) in the build report and is not included in the real-text figures.
