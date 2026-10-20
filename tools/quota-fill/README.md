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
| `harvest.py candidates WAVE` | Samples candidate sentences for the lemmas of a wave and writes the labeling batches |
| `fill.py ingest WAVE`, `fill.py select WAVE [--kind real\|written]` | Reads the labels, picks the rows that fill the deficits and writes the corpus files |
| `written.py export\|ingest\|labeled WAVE` | The top-up by writing: writer batches, validation, blind-label batches |
| `chooser_harvest.py candidates\|select WAVE` | Harvest of chooser rows for the forms that lack a lemma |
| `choosers.py status` | The forms whose chooser rows lack a lemma |
| `coverage.py [WAVE]` | Writes `corpus/coverage/quota-coverage.tsv` and prints the counts |
| `placeholder_glosses.py` | Detection and replacement of placeholder glosses of project senses (see `corpus/curation/README.md`) |
| `prompts/` | The instructions given to the labeler, the writer, the lemma chooser and the gloss writer |

<!-- markdownlint-enable MD013 -->

The pool documents are the ones of the open English part-of-speech corpus: Project Gutenberg books published 1930 or
earlier (header, footer and licence stripped), VOA Learning English staff-written articles and pages of USGS, NPS and
the National Weather Service (each states that its material is in the public domain), and the Tatoeba sentences of the
CC0 export. Wikipedia, Wiktionary, CC BY-SA text, Universal Dependencies data, news sites, NOS, FAA, NASA and the
Tatoeba CC BY sentences are never used.

Every row written by these scripts is targeted (found or written for one sense or form) and its provenance row says so.
