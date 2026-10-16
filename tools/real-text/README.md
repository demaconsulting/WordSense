# Real-Text Harvest

`harvest.py` (Python standard library only) extracts candidate real sentences of at most 35 words from openly
licensed or public-domain sources. Raw downloads go to `cache/` (git-ignored); only extracted sentences are used.

## Sources

- Tatoeba English exports (CC0 subset, and CC BY 2.0 FR with the contributor recorded).
- Project Gutenberg works listed in `gutenberg-works.tsv` (US public domain; the header and licence block are stripped).
- Wikipedia and Universal Dependencies English EWT sentences (CC BY-SA, URL recorded), single sentences only.
- Web excerpts (licence `web-excerpt`) are a last resort for senses that no other source covers. `harvest.py` does not
  fetch them; a single short excerpt is entered by hand into a candidates file with its URL, and the builder rejects a
  web excerpt without an `http(s)` URL (`validate-provenance`).

## Phase 4 Candidates

`candidates-a-b.tsv` lists the 112 tier A and B heteronym candidates (53 and 59) taken from a one-off candidate survey
(lemma, inflected forms, ARPAbet variants, probable sense labels and WordNet part-of-speech counts). Provenance:
pronunciations from CMUdict (BSD-style licence), Wiktionary (CC BY-SA 4.0; facts and word lists only, no prose) and
WordNet 3.0 (Princeton licence); word frequencies came from the `wordfreq` package. `new-lemma-inventory.tsv` is the
sense inventory authored from them (one row per sense with part of speech, pronunciation group, ARPAbet, batch, tier and
status), and `docs/build_notes/new-lemma-decisions.md` records every accept and reject decision. Authored sentences for
rare senses go into `anchors/new-*.tsv` and `holdout/new-*.tsv` with the origin `authored`, which `validate-provenance`
checks.

## Usage

```text
python tools/real-text/harvest.py fetch --gutenberg
python tools/real-text/harvest.py extract --corpus corpus --targets tools/real-text/targets.tsv --out audit-work/real/candidates.tsv
python tools/real-text/harvest.py wiki --corpus corpus --lemma LEMMA --query QUERY --pattern REGEX --out audit-work/real/wiki.tsv
python tools/real-text/harvest.py stats --targets tools/real-text/targets.tsv
python tools/real-text/harvest.py eval-sample --corpus corpus --lemmas lemmas.txt --out audit-work/real/candidates-eval.tsv
```

The target lists are `targets.tsv`, `targets2.tsv` and `targets3.tsv` (restoration) and `targets4.tsv` (the 13 gap
lemmas of Phase 3a; only `conjunct`, `overhead` and `uptown` reached the thresholds, see
`docs/build_notes/gap-lemmas-decisions.md`) and `targets5.tsv` (the 18 weak `more-sentences` lemmas of Phase 3b; only
`conjugate`, `insert` and `present` reached the thresholds, see `docs/build_notes/weak-lemma-decisions.md`) and
`targets6.tsv` (the 13 lemmas of Phase 4 batch 1: `contact`, `impact`, `address`, `transfer`, `update`, `protest`,
`collect`, `export`, `accent`, `upgrade`, `second`, `detail` and `combat`; noun and verb context patterns enrich the
rarer reading, see `docs/build_notes/new-lemma-decisions.md`). Use the
builder commands `real-judge-export`, `real-judge-adjudicate`, `real-judge-merge` and `real-import`
(or `real-eval-import`) to judge the candidates blind and write the corpus and its provenance. Phase 3b adds
senses of existing lemmas as extra anchors and a separate real holdout with `real-judge-export --gap-plan real-gap-1`
(menus from `corpus/audit/real-gap-1-plan.tsv`) and `real-gap-import --name real-gap-1`. See the design of the
WordSenseBuilder unit.
