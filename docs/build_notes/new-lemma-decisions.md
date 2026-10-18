# New Lemma Decisions (Phase 4 Setup)

> **Historical.** This page records an earlier phase of the project. The builder commands it names (for example
> `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`, `error-diagnosis`,
> `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record of how the data was built.

Phase 4 adds new lemmas to the classifier. This page records, for the 112 candidates of tiers A (53) and B (59) in
`tools/real-text/candidates-a-b.tsv`, whether each is accepted for corpus building, with the evidence and the
pronunciations. The machine-readable result is `tools/real-text/new-lemma-inventory.tsv` (one row per sense, with the
part of speech, pronunciation group, ARPAbet, batch and status). No sentences have been harvested yet.
**Please check the pronunciations by ear**, especially the rows marked "ear check" in the inventory.

## Rule

A lemma is accepted only if a pronunciation split linked to a sense or part of speech is established by the CMU
Pronouncing Dictionary (CMUdict) and a reputable second source (Wiktionary, which gives IPA labelled by part of speech
or by separate etymologies). The following are rejected: free variation of stress with one sense, dialect variants,
orthographic-only splits, inflected forms of another candidate, and splits for which neither source links the reading to
a sense. Only facts (pronunciations and sense lists) are taken from Wiktionary; no text is copied. Where the second
reading is rare, the sense is kept and will need authored sentences, which are recorded with the origin `authored`.

## Doubtful Ten

- `gas`: **rejected**. Noun and verb are both /gas/ (G AE1 S). /gaz/ is only a regional variant.
- `mouse`: **rejected**. M AW1 S; the second reading M AW1 Z is not tied to a sense in CMUdict or Wiktionary.
- `automobile`: **rejected**. Two stress patterns with the same sense; free variation.
- `finance`: **rejected**. Three CMUdict variants (F AH0 N AE1 N S, F IH0 N AE1 N S, F AY1 N AE2 N S) with no sense
  link.
- `alum`: **accepted**. Mineral AE1 L AH0 M versus graduate (alumnus or alumna) AH0 L AH1 M; both in CMUdict, and
  Wiktionary has separate senses.
- `sewer`: **accepted**. Drain S UW1 ER0 (CMUdict); one who sews S OW1 ER0 (Wiktionary, separate etymology).
- `primer`: **accepted**. Paint or explosive cap P R AY1 M ER0 (CMUdict); introductory book P R IH1 M ER0 (US,
  Wiktionary). British speakers say PRAY-mer for both.
- `shower`: **accepted**. Rain or bath (noun and verb) SH AW1 ER0; one who shows SH OW1 ER0 (Wiktionary, separate
  etymology).
- `tier`: **accepted**. Row or rank (noun and verb) T IY1 R; one who ties T AY1 ER0 (Wiktionary, separate etymology).
- `tower`: **accepted**. Structure (noun and verb) T AW1 ER0; one who tows T OW1 ER0 (Wiktionary, separate etymology).

The second readings of `sewer`, `shower`, `tier` and `tower` are rare in real text; their senses will probably need
authored sentences.

## Summary

63 lemmas (with 137 senses; see the inventory for the exact rows) are accepted and 49 are rejected. This is fewer than
the plan's 102 because many candidates are only free stress variants.

| Tier | Accepted | Rejected |
| --- | ---: | ---: |
| A | 39 | 14 |
| B | 24 | 35 |

## Batches

- Batch 1: contact, impact, address, transfer, update, protest, collect, export, accent, upgrade, second, detail,
  combat.
- Batch 2: resign, reset, intern, recreate, perfume, dictate, recess, complex, frequent, ally, traverse, prefix,
  suffix.
- Batch 3: discord, recap, advert, alloy, primer, intermediate, wicked, tier, tower, shower, sewer, forearm, alum.
- Batch 4: supposed, blessed, august, defense, bases, costume, romance, submarine, excerpt, annex, slough, tarry,
  underscore.
- Batch 5: gallant, mow, multiply, reprimand, retrofit, egress, agape, predate, ellipses, adduct, forbear.

Tier A comes first, then tier B; within a tier the lemmas that are common in everyday text come first.

## Accepted Lemmas

Pronunciation groups use `stress-first` (noun reading) and `stress-last` (verb reading) for the stress-shift pairs.

| Lemma | Senses (group: ARPAbet) |
| --- | --- |
| contact | noun stress-first: K AA1 N T AE2 K T; verb stress-last: K AH0 N T AE1 K T |
| impact | noun: IH1 M P AE0 K T; verb: IH2 M P AE1 K T |
| address | noun: AE1 D R EH2 S; verb: AH0 D R EH1 S |
| transfer | noun: T R AE1 N S F ER0; verb: T R AE0 N S F ER1 |
| update | noun: AH1 P D EY2 T; verb: AH0 P D EY1 T |
| protest | noun: P R OW1 T EH2 S T; verb: P R AH0 T EH1 S T |
| collect | verb gather: K AH0 L EH1 K T; noun prayer: K AA1 L IH0 K T |
| export | noun: EH1 K S P AO0 R T; verb: IH0 K S P AO1 R T |
| accent | noun: AE1 K S EH2 N T; verb: AH0 K S EH1 N T |
| upgrade | noun: AH1 P G R EY2 D; verb: AH0 P G R EY1 D |
| second | adjective, noun, verb support: S EH1 K AH0 N D; verb secondment: S AH0 K AA1 N D |
| detail | noun: D IY1 T EY0 L; verb: D IH0 T EY1 L |
| combat | noun: K AA1 M B AE0 T; verb: K AH0 M B AE1 T |
| resign | quit: R IH0 Z AY1 N; sign again: R IY0 S AY1 N (re-sign, written with a hyphen) |
| reset | noun: R IY1 S EH0 T; verb: R IY0 S EH1 T |
| intern | noun and verb trainee: IH1 N T ER0 N; verb confine: IH0 N T ER1 N |
| recreate | refresh: R EH1 K R IY0 EY2 T; make again: R IY0 K R IY0 EY1 T |
| perfume | noun: P ER1 F Y UW0 M; verb: P ER0 F Y UW1 M |
| dictate | noun: D IH1 K T EY2 T; verb: D IH0 K T EY1 T |
| recess | noun: R IY1 S EH0 S; verb: R IH0 S EH1 S |
| complex | adjective: K AH0 M P L EH1 K S; noun: K AA1 M P L EH0 K S |
| frequent | adjective: F R IY1 K W AH0 N T; verb: F R IH0 K W EH1 N T |
| ally | noun: AE1 L AY0; verb: AH0 L AY1 |
| traverse | noun: T R AE1 V ER0 S; verb: T R AH0 V ER1 S |
| prefix | noun: P R IY1 F IH0 K S; verb: P R EH0 F IH1 K S |
| suffix | noun: S AH1 F IH0 K S; verb: S AH0 F IH1 K S |
| discord | noun: D IH1 S K AO0 R D; verb: D IH0 S K AO1 R D |
| recap | noun: R IY1 K AE2 P; verb: R IH0 K AE1 P |
| advert | noun: AE1 D V ER0 T; verb: AE0 D V ER1 T |
| alloy | noun: AE1 L OY2; verb: AH0 L OY1 |
| intermediate | adjective and noun: IH2 N T ER0 M IY1 D IY0 IH0 T; verb: IH2 N T ER0 M IY1 D IY0 EY2 T |
| wicked | evil: W IH1 K AH0 D; past of wick: W IH1 K T |
| forearm | noun: F AO1 R AA2 R M; verb: F AO0 R AA1 R M |
| supposed | adjective and past: S AH0 P OW1 Z D; "supposed to": S AH0 P OW1 S T |
| blessed | adjective: B L EH1 S AH0 D; past verb: B L EH1 S T |
| august | adjective: AO0 G AH1 S T; month: AO1 G AH0 S T |
| defense | general: D IH0 F EH1 N S; sports (US): D IY1 F EH2 N S |
| bases | plural of base: B EY1 S AH0 Z; plural of basis: B EY1 S IY0 Z |
| costume | noun: K AA1 S T UW0 M; verb: K AA0 S T UW1 M |
| romance | noun: R OW1 M AE0 N S; verb: R OW0 M AE1 N S |
| submarine | noun: S AH1 B M ER0 IY2 N; adjective: S AH2 B M ER0 IY1 N |
| excerpt | noun: EH1 K S ER0 P T; verb: EH0 K S ER1 P T |
| annex | noun: AE1 N EH2 K S; verb: AH0 N EH1 K S |
| slough | shed skin (noun and verb): S L AH1 F; swamp: S L AW1 |
| tarry | linger: T EH1 R IY0; covered in tar: T AA1 R IY0 |
| underscore | noun: AH1 N D ER0 S K AO2 R; verb: AH2 N D ER0 S K AO1 R |
| gallant | brave: G AE1 L AH0 N T; courtly adjective and suitor noun: G AH0 L AE1 N T |
| mow | cut: M OW1; hay loft: M AW1 |
| multiply | verb: M AH1 L T AH0 P L AY2; adverb: M AH1 L T AH0 P L IY0 |
| reprimand | noun: R EH1 P R AH0 M AE2 N D; verb: R EH0 P R IH0 M AA1 N D |
| retrofit | noun: R EH1 T R OW0 F IH2 T; verb: R EH2 T R OW0 F IH1 T |
| egress | noun: IY1 G R EH0 S; verb: IH0 G R EH1 S |
| agape | wide open: AH0 G EY1 P; love: AH0 G AA1 P EY2 |
| predate | occur earlier: P R IY0 D EY1 T; prey upon: P R AH0 D EY1 T |
| ellipses | plural of ellipse: IH0 L IH1 P S IH0 Z; plural of ellipsis: IH0 L IH1 P S IY0 Z |
| adduct | noun: AE1 D AH0 K T; verb: AH0 D AH1 K T |
| forbear | verb abstain: F AO0 R B EH1 ER0; noun ancestor: F AO1 R B EH0 ER0 |
| shower, tower, tier, sewer, primer, alum | see the doubtful ten above |

## Rejected Lemmas

| Lemma | Reason |
| --- | --- |
| access, default, outcry, overcast, mandate, research, debut, concrete, uprising, surcharge | free stress variation |
| chauffeur, detour, devise, automobile, finance, quintuple, concord, email | free variation, no sense link |
| gas, mouse, put, number, fine, sin, periodic, colon, unionize | the second reading is uncorroborated or regional |
| supply, syringe, levant, redress, lactate, ravel, belay, retreat, lima | not sense-linked, rare, or a different word |
| chassis, starboard, coop, whoop, swathe, patronize | dialect or spelling variation |
| advertised, annotating, baptized, forbidden, forecasts | inflected forms or one shared pronunciation |
| sake | the drink is written with an accent and CMUdict has a single reading |
| transpose | US English has a single pronunciation; the second reading is not sense-linked |

The free stress variants (for example `mandate`, `research`, `default`, `detour` and `chauffeur`) are the best
candidates to reconsider if the user wants more lemmas.

## Authored Sentences and Dropped Lemmas

Real text could not fill 54 senses, so the project authored sentences for them with `real-authored-import` (batch
`new-authored-1`). The anchor author and the holdout author worked from separate menus and never saw each other's
output (see `tools/real-text/authoring-prompt.md`). Of 964 authored rows, 958 were accepted (576 anchor and 382
holdout rows); five failed the form, brace or length checks and one (`traverse-noun`) was judged as another sense.

Two senses (`predate-earlier`, `predate-prey`) had too few accepted holdout rows (4 and 0 against a minimum of 8).
A separate holdout author wrote 16 top-up rows. **Decision: these 16 rows were not blind-judged.** They passed the
same mechanical checks as every other authored row (target form from the menu, one pair of braces, at most 35 words,
no duplicate, not in the corpus), and the importer checks them again, but no judge compared them with the sense menu.
They are reported as authored holdout rows and are the only authored rows without a judge.

The 12 lemmas that were held back after batches 2 to 5 (`recreate`, `prefix`, `discord`, `alloy`, `sewer`, `alum`,
`costume`, `romance`, `excerpt`, `egress`, `predate`, `forbear`) were restored (their real rows, 211 anchor, 108
holdout, 12 sense and 319 provenance rows, and their forms) before the authored import, so that each has two senses
with anchors.

**Dropped lemmas: none.** After the import every sense has at least 14 anchors and 8 holdout rows (final counts: 2,489
anchors and 1,357 holdout rows for the 137 senses), the builder trained every lemma, and `validate-provenance` and
`validate-pos` pass. Nothing was removed from the corpus or from the forms. The doubtful words ended as follows:

| Lemma | Decision |
| --- | --- |
| `gas`, `mouse`, `automobile`, `finance` | rejected (see above) |
| `alum`, `sewer`, `primer`, `shower`, `tier`, `tower` | accepted and trained; the rare second readings are authored |

The rare senses of `alum`, `sewer`, `shower`, `tier` and `tower` rest on authored sentences only (or on a few real
ones), so their accuracy is likely optimistic; the results page gives the figures separately for real and authored
rows.

## Sources

- CMUdict, BSD-style licence (pronunciations).
- Wiktionary, CC BY-SA 4.0 (pronunciations and sense lists; facts only, no prose copied).
- WordNet 3.0, Princeton licence (part-of-speech counts in `candidates-a-b.tsv`).
