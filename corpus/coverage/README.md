# Quota coverage of the corpus

`relabel-coverage.tsv` is the quota report of every sense of the lemmas in scope (the 845 lemmas of the curation and of
the relabeled corpus; 6,856 senses): for each sense the part of speech, the tier, the WordNet 3.0 tag count, the number
of anchors and of held-out sentences in `../anchors` and `../holdout`, the quota of each, and the status. It is the input
of the quota filling (harvest of real sentences and writing of the missing ones).

The tiers size the data of a sense by its WordNet 3.0 tag count: **C** (five or more tags) needs 20 anchors and 5
held-out sentences, **U** (one to four tags, and a project sense) 10 and 3, **R** (no tag) 5 and 2. A sense is `met` when
it has both quotas, `short` when it has at least one sentence but misses a quota and `empty` when it has none.

<!-- markdownlint-disable MD013 -->

| Tier | Senses | Met | Short | Empty |
| --- | ---: | ---: | ---: | ---: |
| C (20 + 5) | 1,383 | 103 | 1,064 | 216 |
| U (10 + 3) | 2,244 | 412 | 1,088 | 744 |
| R (5 + 2) | 3,229 | 551 | 1,022 | 1,656 |
| All | 6,856 | 1,066 | 3,174 | 2,616 |

<!-- markdownlint-enable MD013 -->

81 of the 845 lemmas have every sense met and 590 have at least one empty sense. The senses that fall short need
45,434 more anchors and 12,231 more held-out sentences to meet their quotas. The count is measured on the files of
`../anchors` and `../holdout` as committed; the relabeled rows (see "Relabeled Legacy Corpus" in `../README.md`) came
from a corpus that was never sized by tier, so they cover the frequent senses and few of the rare ones.

## Quota filling (progress)

`quota-coverage.tsv` has the same columns measured on the corpus as it is now (after the filling of the waves done so
far; `tools/quota-fill/coverage.py` writes it) and `quota-unfilled.tsv` lists the senses of the lemmas of the finished
waves that the harvest and the writing could not bring to their quota, with the number of sentences written for them and
how many of those the blind labeler placed in the intended sense. Such senses cannot be filled credibly by this method
(a near-duplicate of a sibling sense, or a gloss too thin to write to) and are candidates for the review of the senses and
groups. The waves, the method and the progress table are in `docs/build_notes/quota-fill.md`. After wave 1 (the 50 most
frequent lemmas): 1,775 of 6,856 senses meet their quota (2,896 short, 2,185 empty); 37,585 anchors and 10,132 held-out
sentences are still missing, 373 and 44 of them in the lemmas of wave 1. All quota-filled sentences are targeted
(see "Quota-Filled Rows (targeted)" in `../README.md`).
