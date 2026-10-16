# New Lemma Results (Phase 4, Batches 1 to 5)

Phase 4 adds new lemmas to the table from real text. This page records batch 1 first and batches 2 to 5 after it
(see [Batches 2 to 5](#batches-2-to-5)). Batch 1: thirteen lemmas with 28 senses. The
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
