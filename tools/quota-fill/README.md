# Quota filling tools

Python scripts (standard library only) that fill the per-sense quotas of the encoder corpus with real and written
sentences. The results and the method are described in `docs/build_notes/quota-fill.md`; the corpus files they write are
described in `corpus/README.md` ("Quota-Filled Rows (targeted)").

All work files (the sentence pool, the labeling batches and their answers) live in the git-ignored folder
`tools/real-text/cache/quota/` (`pool.tsv`, `documents.tsv`, `sources.tsv`, `suite.tsv`, `text/`, `work/`).

<!-- markdownlint-disable MD013 -->

| Script | Purpose |
| --- | --- |
| `lexdata.py`, `lexindex.py`, `qf.py` | Read `data/` (senses, glosses, forms), the quota status of every sense, the waves |
| `textutil.py` | Sentence splitting, the sentence filter and the duplicate key of the builder (`Corpus.Normalize`) |
| `pool.py build` | Builds the harvest pool from the public-domain documents and the CC0 Tatoeba export |
| `faa.py extract` | Extracts the text of the accepted FAA handbooks (credited pages dropped) into the pool documents |
| `curate_new.py export\|apply` | Adds lemmas (labels reviewed by a luna pass) and project senses to `corpus/curation/senses.tsv` |
| `techsenses.py export\|apply` | The technical senses and their extra anchors (`corpus/curation/tech-senses.tsv`) |
| `gapfinder.py export\|report` | Finds meanings that the catalog lacks in the FAA sentences the labeler could not place |
| `harvest.py candidates WAVE` | Samples candidate sentences for the lemmas of a wave and writes the labeling batches (`--family`, `--only-lemmas`, `--first-batch` add a supplementary batch) |
| `confusion.py WAVE` | Chooses the senses that get extra real anchors from the calibration-split errors |
| `migrate_prov.py` | One-time migration of the wave 1 provenance rows to the licence basis column |
| `fill.py ingest WAVE`, `fill.py select WAVE [--kind real\|written]` | Reads the labels, picks the rows that fill the deficits and writes the corpus files |
| `written.py export\|ingest\|labeled WAVE` | The top-up by writing: writer batches, validation, blind-label batches |
| `chooser_harvest.py candidates\|select WAVE` | Harvest of chooser rows for the forms that lack a lemma |
| `choosers.py status` | The forms whose chooser rows lack a lemma |
| `coverage.py [WAVE]` | Writes `corpus/coverage/quota-coverage.tsv` and prints the counts |
| `usage_weighted.py LABEL=OUTCOMES.tsv ...` | Diagnostic: re-weights the real test rows of `evaluate --outcomes` files by the harvest share of each sense (an independent usage prior) and prints the weighted fine top-1 and sense-level figures (see `docs/build_notes/frequency-prior.md`) |
| `placeholder_glosses.py` | Detection and replacement of placeholder glosses of project senses (see `corpus/curation/README.md`) |
| `prompts/` | The instructions given to the labeler, the writer, the lemma chooser and the gloss writer |

<!-- markdownlint-enable MD013 -->

The pool documents are the ones of the open English part-of-speech corpus: Project Gutenberg books published 1930 or
earlier (header, footer and licence stripped), works of US federal employees that are in the public domain in the
United States under 17 U.S.C. §105 and the agency policy (VOA Learning English staff-written articles, pages of USGS,
NPS and the National Weather Service, and the FAA handbooks and advisory circulars listed in `faa.py`, without any
passage credited to a third party), and the Tatoeba sentences of the CC0 export. Wikipedia, Wiktionary, CC BY-SA text,
Universal Dependencies data, news sites, NOS, NASA, the FAA documents with third-party rights (the Parachute Rigger
Handbook, the Aviation Weather Handbook, Aerodynamics for Naval Aviators, the MC-4 manual) and the Tatoeba CC BY
sentences are never used. (The earlier waves read the Tatoeba CC BY sentences too; `license_filter.py` removed them, see
`corpus/README.md`.)

Every row written by these scripts is targeted (found or written for one sense or form) and its provenance row says so.
