# Real-Text Evaluation

All figures on this page come from one run of the builder command `real-eval` on the committed corpus and the
committed embedded table (format v3, 349 lemmas, 718 senses):

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
| Real text, seed and restored lemmas (`eval-seed.tsv`) | 689 | 92.16 % | 93.03 % | 86.07 % | 95.95 % |
| LLM-written holdout, same lemmas | 1,246 | 97.59 % | 97.59 % | 94.94 % | 98.22 % |
| Real restored-sense holdout (`real-restore.tsv`, origin `real`) | 180 | 85.00 % | 88.89 % | 67.78 % | 95.90 % |
| LLM-written holdout, restored lemmas | 176 | 97.16 % | 97.16 % | 90.34 % | 98.74 % |

The restored-sense holdout file `corpus/holdout/real-restore.tsv` has 225 rows; 219 of them are real sentences and 6
are reinstated project-written ones. Only 180 of the 219 real rows are scored: the other 39 have an inflected target
word (`bowed`, `bowing`, `led`, `tore`, `torn`, and similar) that the shipped forms table does not map to the lemma,
so the classifier reports no lemma for them and the evaluation skips them. For example `bow` bend-verb, whose ten
holdout rows are all `bowed` or `bowing`, has no scored rows. The same skip applies to the 689 evaluation rows only
where such a form occurs; none were skipped there.

## By Source

| Set and source | Rows | Best sense | Group | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Eval / Project Gutenberg (public domain) | 336 | 91.37 % | 91.67 % | 83.93 % | 95.74 % |
| Eval / Tatoeba CC BY 2.0 FR | 326 | 92.64 % | 94.17 % | 87.42 % | 96.14 % |
| Eval / Tatoeba CC0 | 22 | 95.45 % | 95.45 % | 100.00 % | 95.45 % |
| Eval / UD EWT (CC BY-SA 4.0) | 5 | 100.00 % | 100.00 % | 80.00 % | 100.00 % |
| Restored holdout / Project Gutenberg | 58 | 82.76 % | 86.21 % | 65.52 % | 94.74 % |
| Restored holdout / Tatoeba CC BY 2.0 FR | 73 | 86.30 % | 93.15 % | 67.12 % | 95.92 % |
| Restored holdout / Tatoeba CC0 | 6 | 83.33 % | 83.33 % | 33.33 % | 100.00 % |
| Restored holdout / UD EWT | 2 | 100.00 % | 100.00 % | 100.00 % | 100.00 % |
| Restored holdout / Wikipedia (CC BY-SA 4.0) | 41 | 85.37 % | 85.37 % | 75.61 % | 96.77 % |

## Per Lemma and Sense

Lowest best-sense accuracy on the 689 evaluation rows (rows in parentheses):

- `bow` weapon 50.00 % (8) and bend-noun 75.00 % (12); `bow` bend-verb 100.00 % (4).
- `close` adv 58.33 % (12) against `close` adj 91.67 % (12): the bare adverb is mistaken for the adjective.
- `read` present 58.33 % (12) and past 72.73 % (11): the senses differ only by pronunciation and context.
- `present` noun 66.67 % (12) ("at present") and `present` adj 91.67 % (12).
- `desert` verb 71.43 % (7), `tear` rip-verb 75.00 % (12), `polish` verb 75.00 % (4), `transport` verb 80.00 % (5),
  `row` line 77.78 % (9), `object` verb 83.33 % (6), `lead` guide-verb 83.33 % (12).
- `closer` adj and adv 83.33 % at sense level but 100 % at group level (both are in the group `nearer`).
- `rows` argument has one row and it is wrong (0.00 %).

Everything else on the evaluation set is 90 % or better, and many senses are at 100 % (for example `conduct` noun,
`contract`, `convict`, `permit`, `project`, `survey`, `use`, `wind`, `wound`). On the restored-sense holdout the
weakest cells are `bow` bend-noun 60.00 % (5), `upsetting` noun 60.00 % (5), `close` adv 66.67 % (9) and
`upsetting` adj 70.00 % (10); `orient`, `content` verb and `live` adv are 100 %. The full per-sense tables and the
error lists are produced by `real-eval`.

## Real Text Against the LLM-Written Holdout

On the same 39 lemmas real text scores 5.4 points lower on best sense (92.16 % against 97.59 %) and answers 8.9
points less often (86.07 % against 94.94 %), but when the classifier does answer it is almost as accurate (95.95 %
against 98.22 %). The restored lemmas show the same pattern more strongly (85.00 % against 97.16 % best sense).
Reasons for the difference:

- The restored lemmas (`bow`, `close`, `closer`, `lead`, `tear`, `polish`, `present`, `upsetting`) are included here.
  Their senses are close in meaning or share a spelling across parts of speech, and their LLM-written holdouts are
  small.
- Real sentences are less prototypical than the project-written ones: archaic Gutenberg prose (91.37 % best sense),
  bare forms in ambiguous positions (`increase`, `desert`, `object`, `rebel` as verb or noun) and `read` present
  against past.
- The classifier abstains on weak margins, and real text produces more of them, so answered accuracy stays near 96 %
  while coverage drops.
- Each sense has at most 12 evaluation rows, so a single miss moves a sense by about 8 points.

Group accuracy exceeds sense accuracy only where senses share a group: `closer` adjective and adverb (`nearer`),
`polish` adjective and language (`nationality`), and the `bow`, `lead` and `tear` splits.
