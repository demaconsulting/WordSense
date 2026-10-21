# Quota filling tools

Python scripts (standard library only; `faa.py` needs `pypdf`) that fill the per-sense quotas of the encoder corpus with
real and written
sentences. The method is described in the "Method of the quota filling" section of `corpus/README.md`; the corpus files
they write are
described in `corpus/README.md` ("Quota-Filled Rows (targeted)"). Every sentence is written to `corpus/provenance` with
the four columns `id`, `split`, `source`, `source_key`; the source must be one of the allowed sources of
`tools/WordSense.Builder/ProvenanceSources.cs` (`fill.py` and `chooser_harvest.py` write them, `qf.py` reads them), and
the evidence for each source is in "Sources" of `corpus/README.md`.

All work files (the sentence pool, the labeling batches and their answers) live in the git-ignored folder
`tools/quota-fill/cache/quota/` (`pool.tsv`, `documents.tsv`, `sources.tsv`, `suite.tsv`, `text/`, `work/`); the Tatoeba
CC0 export `tools/quota-fill/cache/cc0.tar.bz2` and the FAA PDFs (`tools/quota-fill/cache/faa`, or the folder in
`FAA_SOURCE_DIR`) are read from the same cache.

<!-- markdownlint-disable MD013 -->

| Script | Purpose |
| --- | --- |
| `lexdata.py`, `lexindex.py`, `qf.py` | Read `data/` (senses, glosses, forms), the quota status of every sense, the waves, the technical-sense targets (`corpus/curation/tech-senses.tsv`, read by `fill.py` and `written.py` with `--tech`) |
| `textutil.py` | Sentence splitting, the sentence filter and the duplicate key of the builder (`Corpus.Normalize`) |
| `pool.py build` | Builds the harvest pool from the public-domain documents and the CC0 Tatoeba export |
| `faa.py extract` | Extracts the text of the accepted FAA handbooks (credited pages dropped) into the pool documents |
| `curate_new.py export\|apply` | Adds lemmas (labels reviewed by a luna pass) and project senses to `corpus/curation/senses.tsv` |
| `harvest.py candidates WAVE` | Samples candidate sentences for the lemmas of a wave and writes the labeling batches (`--family`, `--only-lemmas`, `--first-batch` add a supplementary batch) |
| `confusion.py WAVE` | Chooses the senses that get extra real anchors from the calibration-split errors |
| `fill.py ingest WAVE`, `fill.py select WAVE [--kind real\|written]` | Reads the labels, picks the rows that fill the deficits and writes the corpus files |
| `written.py export\|ingest\|labeled WAVE` | The top-up by writing: writer batches, validation, blind-label batches |
| `chooser_harvest.py candidates\|select WAVE` | Harvest of chooser rows for the forms that lack a lemma |
| `choosers.py status` | The forms whose chooser rows lack a lemma |
| `coverage.py [WAVE]` | Writes `corpus/coverage/quota-coverage.tsv` and prints the counts |
| `usage_weighted.py LABEL=OUTCOMES.tsv ...` | Diagnostic: re-weights the real test rows of `evaluate --outcomes` files by the harvest share of each sense (an independent usage prior) and prints the weighted fine top-1 and sense-level figures (see the user guide chapter "Accuracy and Model Choice") |
| `prompts/` | The instructions given to the labeler, the writers, the lemma chooser and the label reviewer |

<!-- markdownlint-enable MD013 -->

The pool documents are the ones of the open English part-of-speech corpus (`corpus/open/sources.tsv`) and the FAA
documents of `faa.py`; the sources and their licence basis are in "Sources" of `corpus/README.md`. Wikipedia, Wiktionary,
CC BY-SA text, Universal Dependencies data, news sites, NOS, NASA, the FAA documents with third-party rights (the
Parachute Rigger Handbook, the Aviation Weather Handbook, Aerodynamics for Naval Aviators, the MC-4 manual) and the
Tatoeba CC BY sentences are never used.

Every row written by these scripts is targeted (found or written for one sense or form).
