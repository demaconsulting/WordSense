# Weak Lemma Decisions (Phase 3b)

> **Historical.** This page records an earlier phase of the project. The builder commands it names (for example
> `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`, `error-diagnosis`,
> `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record of how the data was built.
> The page also names sources (Tatoeba CC BY 2.0 FR, Wikipedia, Wiktionary, UD) that were later removed from the corpus
> for licensing reasons: none of their text is in the repository now (`corpus/README.md`).

The diagnosis in `learning-curve-and-diagnosis.md` ranks 126 weak lemmas and gives each a recommendation:
`more-sentences` (18), `consumer-rule` (47), `stronger-encoder` (55) and `fix-labels` (6). This page records what
Phase 3b did for the 18 `more-sentences` lemmas and the six `fix-labels` lemmas, and lists the `consumer-rule` and
`stronger-encoder` lemmas as decisions for the project owner. No sentence was written by the project; no overlap
lemma received sentences.

## Rule for the More-Sentences Lemmas

Real sentences were added as additional anchors only for senses with a coverage gap, and a separate real holdout of
at least 8 rows per sense was kept so the evaluation stays honest. A lemma was accepted only if every sense had at
least 8 judged real candidates for the holdout and each gap sense had at least 6 more for the anchors; otherwise it
was skipped. Holdout rows never move into the anchors: the builder fills the holdout first (from the smallest
contributors, that is articles, works or authors) and takes the anchors from other contributors. Sources, in order
of preference: CC0 and public domain (Tatoeba CC0, Project Gutenberg), Tatoeba CC BY 2.0 FR with the contributor
recorded, and Wikipedia single sentences (CC BY-SA 4.0, article URL recorded in `corpus/provenance/real-gap-1.tsv`).

Method: `harvest.py stats` and `extract` with `tools/real-text/targets5.tsv` (all 18 lemmas, only the inflected forms
listed in `corpus/forms/speech.tsv`), then one targeted Wikipedia query per lemma (at most 30 articles, one sentence
per article, 0.5 s between requests; nothing bulk-copied). The six lemmas below with enough candidates were judged
blind: judges A and B independently (B in reversed order), judge C for the 22 disagreements, using the frozen prompt
`corpus/audit/judge-prompt-sense.md` and the menus from `corpus/audit/real-gap-1-plan.tsv`. Of 483 candidates 461
agreed, 19 were adjudicated, 3 stayed undecided and 54 were `none`; the merged result is
`corpus/audit/real-gap-audit-1.tsv`. `real-gap-import` wrote `corpus/anchors|holdout|provenance/real-gap-1.tsv`.

## Per Lemma

| Lemma | Raw local hits | Judged | Decision | Added (anchors / holdout) |
| --- | ---: | ---: | --- | --- |
| present | about 4,580 | 260 | trained | adj 14 / 0 (10 real holdout existed), noun 14 / 8, verb 14 / 8 |
| insert | 49 | 73 | trained | noun 14 / 8, verb 0 / 8 |
| conjugate | 20 | 50 | trained | verb 12 / 8, noun 0 / 8 |
| incarnate | 22 | 43 | skipped | the verb sense yielded only 4 usable sentences (adjective 22) |
| rerun | 5 | 27 | skipped | the verb sense yielded only 2 usable sentences |
| rethink | 20 | 30 | skipped | the noun sense yielded only 5 usable sentences |
| agglomerate | 0 | none | skipped | 5 Wikipedia candidates in total |
| incurvate | 0 | none | skipped | 1 candidate in total |
| inebriate | 2 | none | skipped | 7 candidates for three senses |
| rehash | 1 | none | skipped | 8 candidates for two senses |
| respray | 0 | none | skipped | 8 candidates for two senses |
| retread | 0 | none | skipped | 13 candidates for two senses |
| guesstimate | 3 | none | skipped | 13 candidates for two senses |
| imbricate | 0 | none | skipped | 21 Wikipedia candidates, mostly one sense |
| interlock | 1 | none | skipped | 16 candidates for two senses |
| situate | 5 | none | skipped | 17 candidates for two senses |
| triplicate | 1 | none | skipped | 16 candidates for two senses |
| correlate | 8 | none | skipped | 20 candidates for two senses |

"Judged none" means the raw candidate count could not reach 8 holdout rows for every sense plus 6 or more anchors for
the gap sense after the judge yield seen in the judged lemmas (88 % of the judged candidates received a sense, but the
rarer senses supplied far fewer: 4, 5 and 2 usable sentences for `incarnate` verb, `rethink` noun and `rerun` verb), so
no judging effort was spent on them. These lemmas stay as they are; their errors remain in the diagnosis. More
sources (for example a licensed corpus of the specialist domains involved) are needed for them.

## Result

`conjugate` and `insert` read well on their real holdout (100 % on both conjugate senses and the insert noun, 87.5 %
on the insert verb), `present` improved on the untouched `real-eval` set from 80 % to 92 % (the noun from 66.7 % to
100 %, the adjective fell from 91.7 % to 83.3 %), and overall real-text figures rose slightly (the full before and
after table is the last section of `learning-curve-and-diagnosis.md`). The overall LLM-written plus real holdout
stays at 96.17 %. `present` noun is still weak on the new real holdout (75 %, 6 of 8): "at present" is still
read as an adjective in two rows, an overlap that more anchors only partly fix (see the decision list below).

## Fix-Labels Lemmas

The six lemmas `foliate`, `quintuplicate`, `relaunch`, `aggregate`, `rerelease` and `resit` were checked row by row
(all anchors and holdout rows of the lemma against its sense definitions). No clear mislabel was found, so nothing was
relabeled. "Ambiguous" in the diagnosis means the nearest anchor of the labeled sense is close, not that the label is
wrong. The recommendation for each is a part-of-speech cue in the consumer (labels fine); expected gain is 0.06 to
0.25 of the lemma's rows (upper bounds):

- `foliate`: "Craftsmen foliate silver" is correctly a verb, yet the bare form is predicted adjective (s_same 0.75,
  s_other 0.81); the adjective row "this lichen is foliate" is correct. Stronger encoder or cue.
- `quintuplicate`: "Costs could quintuplicate" (verb) and "a quintuplicate set" (adjective) are right; the same bare
  form is a verb and an adjective.
- `relaunch`: "The airline relaunches its Boston route" (verb) is answered correctly with a low margin; "the relaunch
  of the app" (noun) is predicted verb.
- `aggregate`: "Sand and gravel aggregates" is a noun and "particles of silt aggregate" a verb; one verb row is predicted
  noun (0.76 against 0.89).
- `rerelease`: "The studio's re-releases of older titles" is a noun predicted verb (0.73 against 0.78).
- `resit`: "Students have one resit per module" is a noun predicted verb (0.89 against 0.84).

## Decisions for the Project Owner: Overlap Lemmas

These lemmas have senses whose sentences are about as close to each other as to their own sense (both cosine
similarities at least 0.77, `s_other` within 0.05 of `s_same`). More anchors do not separate them, so none were added.
Each needs a decision. "Consumer rule" means a cue or rule in the consuming code (for example a part-of-speech tag,
a tense cue, a preceding preposition) that overrides or filters the classifier; "stronger encoder" means a larger
or better sentence encoder; "fix labels" means the labels were checked and no change is needed. Expected gain is the
overlap share of the lemma's evaluated rows, an upper bound (the `expected_gain` column of `weak-lemmas.tsv`).

- `bow` weapon (20 errors in 50 rows; real-eval weapon 50 %). Evidence: the ribbon bow and the bow of a boat are read
  as the weapon or the bend, for example "his quaint old-fashioned bow" (bend-noun predicted weapon) and "a large
  bow of blue ribbon". Recommendation: consumer rule on the preceding noun ("hair bow", "bow tie", "bows of the
  canoe"), plus a clearer split in the sense menu (ribbon, ship's front). Expected gain: up to 40 %.
- `close` adverb (18 of 68; real-eval adverb 58 %). Evidence: "lives close by", "close to Layla's hometown": the bare
  adverb is predicted adjective. Recommendation: consumer rule, an adverb cue before "by" and "to" or after a verb.
  Expected gain: up to 16 %.
- `closer` (21 of 48). Evidence: adverb read as adjective ("a little closer", "move closer to me") and the noun
  "a closer" (the person) read as adjective; adjective and adverb are already one group (`nearer`). Recommendation:
  consumer rule, using the group level. Expected gain: up to 42 %.
- `read` present and past (18 of 61; real pooled 65 %). Evidence: "I read the news every morning" (present)
  predicted past, "had read the manuscripts" (past) predicted present; the spellings are identical.
  Recommendation: consumer rule, tense from the surrounding verb group ("had", "have", "will", "to", time words).
  Expected gain: up to 18 %.
- `reread` present and past (4 of 16). Evidence: the same split ("He rereads", "Yesterday I reread").
  Recommendation: consumer rule, share the `read` tense cue. Expected gain: up to 19 %.
- `present` noun (19 of 82; real-eval noun 100 % after Phase 3b, was 66.7 %). Evidence: "He is in the hospital at
  present", "the most original moralist at present in England" are still predicted adjective in the real holdout.
  Recommendation: consumer rule, the phrases "at present" and "for the present" are the noun sense. Expected gain:
  up to 4 % on the lemma, most of the remaining noun errors.
- `desert` verb (4 of 60; real-eval verb 71 %). Evidence: "I do not desert", "will never desert": the bare verb is
  predicted noun. Recommendation: stronger encoder (or a modal or "to" cue); the curve is plateaued. Expected gain:
  up to 7 %.
- `upsetting` (13 of 38). Evidence: noun ("the upsetting of a boat"), verb ("upsetting men's courses") and adjective
  ("potentially upsetting questions") share one form. Recommendation: stronger encoder; the adjective and verb split
  is also a part-of-speech problem. Expected gain: up to 21 %.
- `excommunicate` (8 of 24). Evidence: adjective, verb and noun share the bare form; "declared him excommunicate"
  (adjective) is read as verb. Recommendation: consumer rule, a part-of-speech cue (verb after "to" or a subject,
  adjective after "declared him", noun after an article). Expected gain: up to 29 %.
- `polish` (18 of 58). Evidence: language, adjective and the noun "polish" (shine) overlap; the capitalized "Polish"
  is the language or the adjective. Recommendation: consumer rule, capitalization plus a determiner or noun cue; the
  group `nationality` already merges two senses. Expected gain: up to 29 %.
- `uptown` (13 of 30, all real). Evidence: noun, adjective and adverb differ by syntax only. Recommendation:
  consumer rule, a part-of-speech cue. Expected gain: up to 43 %.

The remaining 47 `consumer-rule` and 55 `stronger-encoder` lemmas are listed in
`learning-curve-and-diagnosis.md`; the pattern is the same: bare forms shared between verb, noun and adjective
(`overflow`, `overrun`, `discourse`, `underlay`, `reincarnate`, `oversteer`) belong to the consumer rule; flat curves
with overlaps (`collocate`, `overhead`, `rewind`, `lead`, `bass`, `rows`) belong to the stronger encoder.

### Suggested Order

1. A shared part-of-speech or tense cue in the consumer for the bare-form lemmas (it covers `read`, `reread`,
   `excommunicate`, `uptown`, `foliate`, `quintuplicate`, `relaunch`, `aggregate`, `rerelease`, `resit` and most of
   the 47 `consumer-rule` lemmas) is the largest single gain.
2. The phrase rules for `at present`, `bow tie` and `close by` are small and cheap.
3. A stronger encoder helps the 55 flat-curve lemmas but is a larger change; decide after the cue is in.
