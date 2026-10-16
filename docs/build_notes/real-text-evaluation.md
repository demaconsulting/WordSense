# Real-Text Evaluation

All figures on this page come from one run of the builder command `real-eval` on the committed corpus and the
committed embedded table (format v3, 352 lemmas, 726 senses; the three lemmas `conjunct`, `overhead` and `uptown`
were added after the first version of this page, see `gap-lemmas-decisions.md`; Phase 3b added real anchors for
`conjugate`, `insert` and `present`, see `weak-lemma-decisions.md`):

```text
dotnet run --project tools/WordSense.Builder -c Release -- real-eval --corpus corpus --model model --report report.md
```

## Method

Real sentences were drawn at random (not by model prediction) for 39 lemmas: the 34 seed lemmas plus the restored
lemmas. They come from Tatoeba and public-domain Project Gutenberg works, up to 14 per lemma and source, and were
judged blind by two judges against the lemma's sense menu, with a third judge for disagreements. In total 1,061
sentences were judged: 1,020 agreed, 20 were adjudicated, 21 stayed undecided and 43 were `none` (no sense fits).
The undecided and `none` sentences are dropped. At most 12 sentences per sense are kept, which gives the 689
sentences (79 lemma and sense pairs) in `corpus/realtext/eval-seed.tsv`. Every one of these 689 rows has a row in
`corpus/provenance/realtext-eval.tsv` (689 provenance rows, origin `real`); the larger judged number of 1,061 refers
only to sentences judged before the drops and the per-sense cap. The evaluation sentences are never used for
training and are not mixed into the holdout.

## Results

| Set | Rows | Best sense | Group | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Real text, seed and restored lemmas (`eval-seed.tsv`) | 689 | 92.60 % | 93.47 % | 85.63 % | 96.44 % |
| LLM-written holdout, same lemmas | 1,268 | 97.71 % | 97.71 % | 95.19 % | 98.26 % |
| Real restored-sense and gap-lemma holdout (`real-restore.tsv`) | 297 | 86.20 % | 90.24 % | 72.05 % | 96.26 % |
| LLM-written holdout, restored lemmas | 181 | 97.79 % | 97.79 % | 91.71 % | 98.80 % |
| Real Phase 3b holdout (`real-gap-1.tsv`) | 48 | 93.75 % | 93.75 % | 89.58 % | 97.67 % |
| LLM-written holdout, Phase 3b lemmas | 62 | 91.94 % | 91.94 % | 90.32 % | 98.21 % |

Phase 3b (`weak-lemma-decisions.md`) added 68 real anchors and the 48 real holdout rows of `real-gap-1.tsv`. Before
it the evaluation set scored 92.16 % best sense (95.95 % answered accuracy, 85.92 % answered); the rise to 92.60 %
comes
from `present` (the 25 `present` rows went from 80 % to 92 %).

The holdout file `corpus/holdout/real-restore.tsv` has 303 rows: the 225 rows of the restored senses (219 real
sentences and 6 reinstated project-written ones) and 78 real rows of the gap lemmas `conjunct`, `overhead` and
`uptown` (Phase 3a). All 297 real rows are scored. The restored senses alone scored 219 rows at 87.67 % best sense
before the gap lemmas were added; the gap lemmas lower the pooled figure (they are mostly Wikipedia sentences, see the
source table). Before the forms table listed the inflected
targets (`bowed`, `bowing`, `led`, `tore`, `torn`, and similar), 39 of them were skipped because the classifier
reported no lemma; the earlier run therefore scored 180 rows (85.00 % best sense, 67.78 % answered). Adding the
missing forms raised the scored rows to 219, the best sense to 87.67 % and the answered share to 73.06 %. The 689
evaluation rows are unchanged (none were skipped). The guard test
`WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma` keeps every corpus target resolvable.

## By Source

| Set and source | Rows | Best sense | Group | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Eval / Project Gutenberg (public domain) | 336 | 91.96 % | 92.26 % | 83.33 % | 96.43 % |
| Eval / Tatoeba CC BY 2.0 FR | 326 | 92.94 % | 94.48 % | 87.42 % | 96.49 % |
| Eval / Tatoeba CC0 | 22 | 95.45 % | 95.45 % | 95.45 % | 95.24 % |
| Eval / UD EWT (CC BY-SA 4.0) | 5 | 100.00 % | 100.00 % | 80.00 % | 100.00 % |
| Restored and gap holdout / Project Gutenberg | 92 | 88.04 % | 91.30 % | 72.83 % | 97.01 % |
| Restored and gap holdout / Tatoeba CC BY 2.0 FR | 92 | 88.04 % | 94.57 % | 70.65 % | 96.92 % |
| Restored and gap holdout / Tatoeba CC0 | 9 | 88.89 % | 88.89 % | 66.67 % | 100.00 % |
| Restored and gap holdout / UD EWT | 2 | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| Restored and gap holdout / Wikipedia (CC BY-SA 4.0) | 102 | 82.35 % | 85.29 % | 72.55 % | 94.59 % |
| Phase 3b holdout / Project Gutenberg | 11 | 90.91 % | 90.91 % | 72.73 % | 100.00 % |
| Phase 3b holdout / Tatoeba CC BY 2.0 FR | 18 | 94.44 % | 94.44 % | 94.44 % | 94.12 % |
| Phase 3b holdout / Tatoeba CC0 | 1 | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| Phase 3b holdout / UD EWT | 1 | 0.00 % | 0.00 % | 0.00 % | n/a |
| Phase 3b holdout / Wikipedia (CC BY-SA 4.0) | 17 | 100.00 % | 100.00 % | 100.00 % | 100.00 % |

## Per Lemma and Sense

Lowest best-sense accuracy on the 689 evaluation rows (rows in parentheses):

- `bow` weapon 50.00 % (8) and bend-noun 75.00 % (12); `bow` bend-verb 100.00 % (4).
- `close` adv 58.33 % (12) against `close` adj 91.67 % (12): the bare adverb is mistaken for the adjective.
- `read` present 58.33 % (12) and past 72.73 % (11): the senses differ only by pronunciation and context.
- `present` adj 83.33 % (12) and noun 100.00 % (12); before Phase 3b the noun scored 66.67 % ("at present") and the
  adjective 91.67 %.
- `desert` verb 71.43 % (7), `tear` rip-verb 75.00 % (12), `polish` verb 75.00 % (4), `transport` verb 80.00 % (5),
  `row` line 77.78 % (9), `object` verb 83.33 % (6), `lead` guide-verb 83.33 % (12).
- `closer` adj and adv 83.33 % at sense level but 100 % at group level (both are in the group `nearer`).
- `rows` argument has one row and it is wrong (0.00 %).

Everything else on the evaluation set is 90 % or better, and many senses are at 100 % (for example `conduct` noun,
`contract`, `convict`, `permit`, `project`, `survey`, `use`, `wind`, `wound`). On the restored-sense holdout the
weakest cells are `bow` bend-noun 71.43 % (7), `upsetting` noun 60.00 % (5), `close` adv 66.67 % (9),
`upsetting` adj 70.00 % (10), `closer` adj 70.00 % (10) and `rows` argument 50.00 % (2); `orient`, `content` verb and
`live` adv are 100 %. The gap lemmas (Phase 3a) score `overhead` adj 50.00 % (10; 70.00 % at group level, the
adjective is mistaken for the adverb), `overhead` adv and noun 100 %, `uptown` noun 70.00 % (10), adj and adv 80.00 %
(10 each) and `conjunct` adj 100 % (10), noun 75.00 % (8). The full per-sense tables and the
error lists are produced by `real-eval`.

## Real Text Against the LLM-Written Holdout

On the same 39 lemmas real text scores 5.1 points lower on best sense (92.60 % against 97.71 %) and answers 9.6
points less often (85.63 % against 95.19 %), but when the classifier does answer it is almost as accurate (96.44 %
against 98.26 %). The restored lemmas show the same pattern more strongly (86.20 % against 97.79 % best sense).
Reasons for the difference:

- The restored lemmas (`bow`, `close`, `closer`, `lead`, `tear`, `polish`, `present`, `upsetting`) are included here.
  Their senses are close in meaning or share a spelling across parts of speech, and their LLM-written holdouts are
  small.
- Real sentences are less prototypical than the project-written ones: archaic Gutenberg prose (91.96 % best sense),
  bare forms in ambiguous positions (`increase`, `desert`, `object`, `rebel` as verb or noun) and `read` present
  against past.
- The classifier abstains on weak margins, and real text produces more of them, so answered accuracy stays near 96 %
  while coverage drops.
- Each sense has at most 12 evaluation rows, so a single miss moves a sense by about 8 points.

Group accuracy exceeds sense accuracy only where senses share a group: `closer` adjective and adverb (`nearer`),
`polish` adjective and language (`nationality`), and the `bow`, `lead` and `tear` splits.
