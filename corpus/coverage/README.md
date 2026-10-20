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
