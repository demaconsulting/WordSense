# Part-of-Speech Audit

> **Historical.** This page records an earlier phase of the project. The builder commands it names (for example
> `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`, `error-diagnosis`,
> `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record of how the data was built.

Every anchor and holdout sentence of the corpus was judged for the real part of speech and form of the braced
word. The audit is the evidence behind the relabel of the sense inventory (see `corpus/audit/`).

## Method

The corpus was cut into chunks. Two independent judges (A: corpus order, B: reversed order) saw only the
lemma and the sentence, never the current label. Rows where the two judges agreed on a part of speech with
at least medium confidence were accepted; every other row went to a third judge (C) who saw both answers.
C is final only when it is confident and matches A or B; otherwise the row is `undecided` and is never
used as evidence. A blind probe of about 5 % of the accepted rows re-checks the agreement. The judge
prompt is frozen in `corpus/audit/judge-prompt.md`. The model was not used.

Pilot (chunks 008, 057, 082) and full run: judges A and B agreed on the part of speech for 15,827 of 15,864
rows (99.77 %). 15,785 rows were accepted directly, 79 went to judge C (15 resolved, 64 undecided). The blind
probe re-judged 790 accepted rows (every 20th) and disagreed on 0.

## Results

| Resolution | Rows |
| --- | ---: |
| adjudicated | 15 |
| agree | 15785 |
| undecided | 64 |
| total | 15864 |

| Final part of speech | Rows |
| --- | ---: |
| adj | 1550 |
| adv | 147 |
| noun | 6621 |
| undecided | 64 |
| verb | 7482 |

## Senses That Are Not Pure

A sense is PURE when one part of speech covers all rows. NOISE means the minority is at most 2 rows or 5 %.
MIXED means a larger minority. Senses with undecided rows are marked.

| Lemma | Sense | Flag | Distribution |
| --- | --- | --- | --- |
| absent | adj | NOISE | adj 20, undecided 2 |
| aggregate | noun | NOISE | noun 21, adj 1 |
| arithmetic | adj | NOISE | adj 21, undecided 1 |
| arsenic | adj | NOISE | adj 24, undecided 1 |
| associate | noun | NOISE | noun 20, adj 2 |
| bow | bend | MIXED | verb 19, noun 15 |
| close | adj | MIXED | adj 27, adv 7 |
| closer | verb | MIXED | adj 7, adv 7 |
| commune | noun | NOISE | noun 21, undecided 1 |
| compact | noun | NOISE | noun 21, undecided 1 |
| conjugate | noun | NOISE | noun 20, adj 2 |
| conscript | noun | NOISE | noun 21, undecided 1 |
| content | adj | MIXED | adj 32, verb 3 |
| digest | noun | NOISE | noun 21, undecided 1 |
| graduate | noun | NOISE | noun 20, adj 2 |
| inside | adv | NOISE | adv 21, undecided 1 |
| invalid | noun | NOISE | noun 32, adj 1 |
| lead | guide | MIXED | verb 21, noun 11 |
| live | adj | MIXED | adj 31, adv 3 |
| nearby | adv | NOISE | adv 21, adj 1 |
| obverse | noun | NOISE | noun 21, adj 1 |
| opiate | verb | NOISE | verb 13, undecided 1 |
| orient | noun | MIXED | noun 14, undecided 8 |
| predestinate | adj | NOISE | adj 21, undecided 1 |
| present | noun | MIXED | noun 23, adj 17 |
| read | past | NOISE | verb 32, undecided 2 |
| record | noun | NOISE | noun 32, adj 2 |
| regenerate | adj | NOISE | adj 21, undecided 1 |
| relet | noun | NOISE | noun 13, undecided 1 |
| retread | noun | NOISE | noun 21, undecided 1 |
| row | argument | NOISE | noun 32, verb 2 |
| rows | argument | NOISE | noun 20, verb 2 |
| rows | line | MIXED | noun 17, verb 3 |
| situate | adj | NOISE | adj 21, undecided 1 |
| tear | rip | MIXED | verb 24, noun 10 |
| underground | adj | NOISE | adj 21, adv 1 |
| undertaking | noun | NOISE | noun 21, verb 1 |
| unlearned | adj | NOISE | adj 19, undecided 2 |

| Flag | Senses |
| --- | ---: |
| MIXED | 10 |
| NOISE | 28 |
| PURE | 669 |

## Forms Worth Knowing

15 rows are comparative or superlative adjectives or adverbs; 22 are past participles and 37 are present participles.
