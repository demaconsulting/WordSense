# Quota coverage of the corpus

`relabel-coverage.tsv` is the quota report of every sense of the lemmas in scope (the 845 lemmas of the curation and of
the relabeled corpus; 6,856 senses): for each sense the part of speech, the tier, the WordNet 3.0 tag count, the number
of anchors and of held-out sentences in `../anchors` and `../holdout`, the quota of each, and the status. It is the input
of the quota filling (harvest of real sentences and writing of the missing ones).

The tiers size the data of a sense by its WordNet 3.0 tag count: **C** (five or more tags) needs 20 anchors and 5
held-out sentences, **U** (one to four tags, and a project sense) 10 and 3, **R** (no tag) 5 and 2. A sense is `met` when
it has both quotas, `short` when it has at least one sentence but misses a quota and `empty` when it has none. In
`quota-coverage.tsv`, the report of the current scope, an R sense of a lemma with 15 or more senses needs 3 and 1.

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
`../anchors` and `../holdout` as committed (the original 845-lemma figures are those of the relabeled legacy corpus
before the quota filling and before the removal of the Tatoeba CC BY rows); the relabeled rows (see the `relabel-*`
files in `../README.md`) came
from a corpus that was never sized by tier, so they cover the frequent senses and few of the rare ones.

## Quota filling

`shape-coverage.tsv` is the shape report of the noun/verb lemmas: for every lemma, part of speech and sentence shape that
has a fitting sense (`../curation/shape-fit.tsv`), the fitting senses, the number of kept written rows of that shape
(from the shape tag of the provenance) and the status (`met` with at least 2 rows, otherwise `short`).
`tools/quota-fill/coverage.py` writes it; the shapes are described in "Shape coverage" of `tools/quota-fill/README.md`.

`quota-coverage.tsv` has the same columns measured on the corpus as it is now (`tools/quota-fill/coverage.py` writes it)
and `quota-unfilled.tsv` lists the senses of the lemmas of the finished waves that the harvest and the writing could not
bring to their quota, with the number of sentences written for them and how many of those the blind labeler placed in the
intended sense. Such senses cannot be filled credibly by this method (a near-duplicate of a sibling sense, or a gloss too
thin to write to) and are candidates for the review of the senses and groups. `quota-unfilled.tsv` (937 senses) is rewritten
for the lemmas of a wave when it is filled; the rows of the earlier waves were **not** recomputed after the Tatoeba CC BY
rows were removed, because the senses that fell short only for lack of the removed rows are not unfillable. The method is
in the "Method of the quota filling" section of `../README.md`.

Current state of `quota-coverage.tsv`: 11,027 of 12,376 senses meet their quota (1,148 short, 201 empty). The 12,373
senses of the 1,443 lemmas with anchors are 11,027 met, 1,148 short and 198 empty; 6,310 anchors and 1,374 held-out
sentences are missing for them. The other 3 senses are of 2 lemmas that have no anchor yet (`somewhat` and `traveling`);
45 anchors and 12 held-out sentences are missing for them. The scope includes the 176 senses of the 12 technical lemmas
and the 6 project senses (`../curation/README.md`). All quota-filled sentences are targeted (see "Quota-Filled Rows
(targeted)" in `../README.md`).

Current state of `shape-coverage.tsv`: 4,182 of 4,251 units meet their quota and 69 are short. A lemma whose noun
senses hold less than 0.02 of the noun and verb tag counts has no noun unit.
