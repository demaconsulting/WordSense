# Real-Text Evaluation

## Method

Real sentences were sampled at random (not by model prediction) for 39 lemmas: the 34 seed lemmas plus the
restored lemmas. They come from Tatoeba and public-domain Project Gutenberg works, up to 14 per lemma and source, and
were judged blind by two judges against the lemma's sense menu, with a third judge for disagreements. Sentences that
no sense fits (21 undecided, 43 none of 1,061) are dropped. At most 12 sentences per sense are kept in
`corpus/realtext/eval-seed.tsv`; they are never used for training and not mixed into the holdout. Rare senses are
oversampled by the random draw only through the per-sense cap. Provenance is in
`corpus/provenance/realtext-eval.tsv`. The report is produced by the builder command `real-eval`.

## Results

| Set | Rows | Best sense | Group | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| Real text, seed and restored lemmas | 551 | 94.92 % | 94.92 % | 93.28 % | 96.50 % |
| LLM-written holdout, same lemmas | 1,240 | 97.74 % | 97.74 % | 96.05 % | 98.32 % |
| Real restored-sense holdout | 19 | 100.00 % | 100.00 % | 89.47 % | 100.00 % |
| LLM-written holdout, restored lemmas | 170 | 98.82 % | 98.82 % | 98.24 % | 99.40 % |

| Source (real text) | Rows | Best sense | Answered accuracy |
| --- | ---: | ---: | ---: |
| Project Gutenberg (public domain) | 272 | 93.38 % | 95.58 % |
| Tatoeba CC BY 2.0 FR | 257 | 96.50 % | 97.53 % |
| Tatoeba CC0 | 18 | 94.44 % | 94.44 % |
| UD EWT | 4 | 100.00 % | 100.00 % |

## Reading the Results

Real text is about 2.8 points harder than the project-written holdout. The weakest cases are verb/noun
confusions of bare forms (`desert`, `object`, `increase`, `polish`) and the `read` present versus past senses, which
differ only by pronunciation and context (58 % and 73 % best sense). Archaic Gutenberg prose is harder than modern
Tatoeba sentences. Group accuracy equals sense accuracy on these lemmas because their senses are single-member
groups, except for the restored split senses, which had no errors in the sample.
