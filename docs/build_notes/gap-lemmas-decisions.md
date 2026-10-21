# Gap Lemmas Decisions (Phase 3a)

> **Historical.** This page records an earlier phase of the project. The builder commands it names (for example
> `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`, `error-diagnosis`,
> `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record of how the data was built.
> The page also names sources (Tatoeba CC BY 2.0 FR, Wikipedia, Wiktionary, UD) that were later removed from the corpus
> for licensing reasons: none of their text is in the repository now (`corpus/README.md`).

Thirteen lemmas of the specification had no classifier because the earlier corpus held no sentences for them:
`conjunct`, `cumulate`, `depute`, `domesticate`, `illegitimate`, `outcaste`, `overhead`, `overman`, `overstudies`,
`overstudy`, `overwater`, `unstick` and `uptown`. This page records what real text supplied for each of them and which
were trained. No sentence in this phase was written by the project; every added row is a real sentence with a row in
`corpus/provenance/real-restore.tsv`.

## Rule

A lemma is trained only if real text supplies at least 14 anchors and 8 holdout rows for each of at least two senses.
Anchors and holdout rows are disjoint (a sentence never appears in both), the holdout holds real sentences only, and
the builder chooses the holdout from contributors (articles, works, authors) other than the anchors where possible.
Sources, in order of preference: CC0 and public domain (Tatoeba CC0, Project Gutenberg), Tatoeba CC BY 2.0 FR with the
contributor recorded, and Wikipedia single sentences (CC BY-SA 4.0, article URL recorded). No web excerpt was needed.
Wikipedia was queried only for `overhead` (noun), `uptown` and `conjunct`, with targeted queries, a 2 s delay between
requests and at most one sentence per article; nothing was bulk-copied.

## Method

1. `harvest.py stats` and `extract` with `tools/real-text/targets4.tsv` (one `any` target per lemma with its inflected
   forms) counted and sampled the local sources (Tatoeba, Project Gutenberg, UD EWT).
2. For `overhead`, `uptown` and `conjunct` the local sources were too thin for some senses, so `harvest.py wiki` added
   Wikipedia sentences (CC BY-SA 4.0).
3. The sense menus (with definitions) were added to `corpus/audit/restore-plan.tsv`; the judges saw only the menu and
   the braced sentence, never the source or a proposed label. `real-judge-export` wrote the chunks, blind judges A and B
   judged them independently (B in reversed order), `real-judge-adjudicate` listed the rows that were not auto-accepted
   and judge C decided those (it also sees the A and B answers, as in the earlier restoration). `real-judge-merge`
   wrote `corpus/audit/real-sense-audit-6.tsv`, `-7.tsv` and `-8.tsv` (three rounds, because `uptown` adverb needed more
   sentences than the first two rounds found).
4. `real-import` with all earlier candidate and audit files plus the new ones. Before the new run the import was
   replayed with the old inputs and the plan extended: it reproduced the committed files byte for byte, so the new rows
   only add to `real-restore.tsv` in `anchors/`, `holdout/`, `senses/` and `provenance/`.
5. Forms entries (`conjunct`, `conjuncts`, `overhead`, `overheads`, `uptown`), table rebuild, `validate-provenance`,
   `validate-pos`, `build.ps1`.

## Per Lemma

| Lemma | Raw local hits | Judged | Accepted per sense (anchors / holdout) | Decision |
| --- | ---: | ---: | --- | --- |
| `overhead` | 176 (Tatoeba 75, Gutenberg 101) plus Wikipedia | 236 | adj, adv, noun: 20 / 10 each | Trained, 3 senses |
| `uptown` | 2 (Tatoeba CC0 1, Gutenberg 1) plus Wikipedia | 140 | adj, adv, noun: 20 / 10 each | Trained, 3 senses |
| `conjunct` | 1 (UD EWT) plus Wikipedia | 71 | adj 20 / 10, noun 16 / 8 | Trained, 2 senses |
| `illegitimate` | 18 (Tatoeba 14, Gutenberg 4) | 0 | none | Dropped: one part of speech, see below |
| `domesticate` | 38 (Tatoeba 26, Gutenberg 12) | 0 | none | Dropped: too few for two senses |
| `depute` | 8 (Gutenberg) | 0 | none | Dropped: below threshold |
| `overwater` | 8 (Tatoeba) | 0 | none | Dropped: below threshold |
| `unstick` | 5 (Tatoeba 4, CC0 1) | 0 | none | Dropped: below threshold |
| `overman` | 2 (Tatoeba 1, Gutenberg 1) | 0 | none | Dropped: below threshold |
| `cumulate` | 1 (Gutenberg) | 0 | none | Dropped: below threshold |
| `overstudy` | 1 (Gutenberg) | 0 | none | Dropped: below threshold |
| `outcaste` | 0 | 0 | none | Dropped: no sentences |
| `overstudies` | 0 | 0 | none | Dropped: no sentences |

The raw local hits are the candidate sentences `harvest.py extract` found in the local sources. The ten dropped
lemmas were harvested and counted but not judged: the raw count of every one is
below the 44 sentences that two senses with 14 anchors and 8 holdout rows each would need (22 each), so no judging
outcome could change the decision.

### Judging results of the three trained lemmas

| Lemma | Judged | Agreed | Adjudicated | Undecided | none |
| --- | ---: | ---: | ---: | ---: | ---: |
| `overhead` | 236 | 228 | 8 | 0 | 4 |
| `uptown` | 140 | 113 | 26 | 1 | 18 |
| `conjunct` | 71 | 69 | 2 | 0 | 2 |

Accepted sentences per sense after judging (before the caps of 20 anchors and 10 holdout rows): `overhead` adj 30, adv
162, noun 40; `uptown` adj 38, adv 32, noun 51; `conjunct` adj 45, noun 24. The `none` answers are proper names
(album and song titles such as "Uptown Funk", "Uptown Special"), table headings and a few non-sense uses. The `uptown`
undecided row was dropped.

### Sense definitions and parts of speech

Every sense has exactly one true part of speech; groups name senses that are close in meaning.

- `overhead`: `adj` (located above, especially over the head: overhead light, overhead compartment; also overhead
  costs), `adv` (in the sky or above: flew overhead), `noun` (running expenses of a business, or the extra processing
  cost of a system: administrative overhead, protocol overhead). `adj` and `adv` share the group `above`.
- `uptown`: `adj` (uptown train, uptown stations), `adv` (go uptown, heading uptown), `noun` (the upper or residential
  part of a city, or a district named Uptown). `adj` and `adv` share the group `uptown`.
- `conjunct`: `adj` (joined; conjunct melodic motion, conjunct adverb, conjunct consonants and verb order in
  linguistics) and `noun` (an operand of a conjunction in logic and grammar).

Pronunciation of `overhead`. CMUdict has one entry only (`OW1 V ER0 HH EH1 D`), so a stress split between the noun
and the adjective or adverb cannot be verified from it. Wiktionary and Merriam-Webster give the noun with first-syllable
stress (the noun with first-syllable stress, the adverb with last-syllable stress); Merriam-Webster gives the adjective
first-syllable stress, Wiktionary last-syllable stress. Because the stress of the adjective is not stable across
dictionaries, the senses follow the true part of speech and meaning, not the stress, and no sense is named after a
pronunciation. `uptown`, `conjunct` (CMUdict has no entry) and `illegitimate` carry no verifiable pronunciation split
either.

### Why the other lemmas were dropped

- `illegitimate` (CMUdict: `IH2 L IH0 JH IH1 T AH0 M IH0 T`, adjective). The rare verb "to illegitimate" has no real
  sentence in the sources, and a split of the adjective by meaning (born out of wedlock, against the law) is not a part
  of speech or pronunciation difference, so only one sense exists and a classifier needs at least two.
- `domesticate` (CMUdict `D AH0 M EH1 S T AH0 K EY2 T`): all 38 hits are verb forms or the adjective-like participle
  `domesticated`; 38 sentences cannot give 22 for each of two senses (verb and adjective), and the participle is not
  separable by a part-of-speech label the judges can apply reliably.
- `depute`, `overwater`, `unstick`, `overman`, `cumulate`, `overstudy`, `overstudies`, `outcaste`: the sources hold 0
  to 8 sentences in total. `overman` (noun against verb) and `outcaste` (noun against verb) have different senses,
  but real text supplies 2 and 0 sentences.
  No sentence was authored to fill the gap.

## Result

The table now has 352 lemmas and 726 senses (349 and 718 before). The corpus has 10,338 anchors and 6,088 holdout rows
(real rows added: 156 anchors, 78 holdout). Holdout accuracy of the new lemmas in the build report:

| Lemma | Anchors | Holdout | Best sense | Answered accuracy | Coverage |
| --- | ---: | ---: | ---: | ---: | ---: |
| `conjunct` | 36 | 18 | 88.89 % | 88.24 % | 94.44 % |
| `overhead` | 60 | 30 | 83.33 % | 100.00 % | 63.33 % |
| `uptown` | 60 | 30 | 76.67 % | 94.44 % | 60.00 % |

All three are below the 95 % target of the build report and are listed as weak: the adjective and the adverb of
`overhead` are hard to tell apart (the real-text group accuracy of `overhead` adjective is 70 % against 50 % best
sense), and `uptown` mixes a district name, an adjective and an adverb in short Wikipedia sentences. Overall holdout
accuracy is 96.17 % best sense and 96.37 % group; the new senses are harder than the average. The retraining also
changes the last digits of the abstention margins of other lemmas (floating-point sums over parallel work); no
accuracy of an existing lemma changes by more than the rounding in the build report.

## Licence Notes

The sentences of the three lemmas come from Tatoeba (CC0 and CC BY 2.0 FR, contributor recorded), Project Gutenberg
(public domain) and Wikipedia (CC BY-SA 4.0, article and URL recorded). `corpus/provenance/real-restore.tsv` has 234
new rows, one per anchor or holdout sentence.
