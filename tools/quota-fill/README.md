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
| `curate_new.py apply\|export --lemmas FILE`, `curate_new.py groups FILE` | `apply` appends the senses of the listed lemmas (a file with one lemma per line, or a TSV with the lemma first) to `corpus/curation/senses.tsv`, with the labels of the data (a reviewed label from `work/curate/out` replaces one); `export` writes the label-review batches; `groups` appends reviewed groups to `groups.tsv` and marks their members in `senses.tsv` |
| `harvest.py candidates WAVE` | Samples candidate sentences for the lemmas of a wave and writes the labeling batches (`--family`, `--only-lemmas`, `--first-batch` add a supplementary batch; `--only-lemmas` takes a file of lemmas or a comma-separated list and replaces the wave plan; `--tier C\|U\|R` makes the wave every lemma with a short sense of that tier, named e.g. `4c`, `4u` or `4r` (a wave id with a letter is a tier wave, and the commands that select by tier (`harvest.py candidates` and `status`, `fill.py select`, `written.py export`) refuse it without `--tier`); `fill.py select` and `written.py export` take the same `--tier`; `--faa-tech-only` on `harvest.py candidates` and `fill.py select` keeps FAA text to the lemmas and senses of `corpus/curation/tech-senses.tsv`) |
| `confusion.py WAVE` | Chooses the senses that get extra real anchors from the calibration-split errors |
| `fill.py ingest WAVE`, `fill.py agree WAVE`, `fill.py select WAVE [--kind real\|written] [--only-lemmas FILE]` | Reads the labels, picks the rows that fill the deficits and writes the corpus files. `agree` merges two blind labelers (`work/w<WAVE>/out-sol` and `out-sonnet`, the same batch files) into `out/`: a candidate keeps its label only when both name the same sense with high confidence, and `agreement.tsv` counts the agreement by lemma. `select` reads every `work/w*/labeled.tsv`, so a new wave always gives `--only-lemmas` (a file of lemmas or a comma-separated list): only those lemmas are filled and every other lemma stays as it is |
| `blindcheck.py export\|judge\|apply WAVE [--kind real\|written]` | Blind check of the rows a wave selected: a labeler that never sees the gold label (senses in another order) labels every row again (`--every N` takes a sample); the rows it does not confirm go to a second labeler; `apply` keeps a row that either confirms, relabels one that both put in the same other sense of the lemma, removes the rest, and writes `chk-report-<kind>.tsv`. The work folders are `chk-*` and `chk2-*` for real rows and `wchk-*` and `wchk2-*` for written rows. Run it after `fill.py select` (real) and before the written top-up |
| `written.py export\|ingest\|labeled WAVE` | The top-up by writing: writer batches (`--only-lemmas` as in `harvest.py`), validation, blind-label batches |
| `chooser_harvest.py candidates\|select WAVE [--only-lemmas FILE]` | Harvest of chooser rows for the forms that lack a lemma (`--only-lemmas`: the forms that lack one of these lemmas; run it after the fine-sense rows of the wave are selected, because the rows of shared forms become chooser rows) |
| `choosers.py status` | The forms whose chooser rows lack a lemma |
| `coverage.py [WAVE]`, `coverage.py --only-lemmas FILE` | Writes `corpus/coverage/quota-coverage.tsv` and prints the counts. `WAVE` (a rank wave) and `--only-lemmas` (a file of lemmas, for the stage 1 waves) also rewrite the rows of `quota-unfilled.tsv` for those lemmas; the rows of other lemmas stay |
| `shortfix.py check\|export\|merge WAVE STAGE` | Short answers of the labeling agents: lists the batches whose answer file lacks items, writes `<in>-fix` batches with only the missing items, and merges their answers (`STAGE`: `real-sol`, `real-sonnet`, `chk`, `chk2`, `wchk`, `wchk2`, `wr-label`). `fill.py agree`, `blindcheck.py judge` and `apply` and `written.py labeled` refuse to run while an answer is short |
| `usage_weighted.py LABEL=OUTCOMES.tsv ...` | Diagnostic: re-weights the real test rows of `evaluate --outcomes` files by the harvest share of each sense (an independent usage prior) and prints the weighted fine top-1 and sense-level figures (see the user guide chapter "Accuracy and Model Choice") |
| `prompts/` | The instructions given to the labeler, the writers, the lemma chooser, the label reviewer and the group proposer and reviewer (`group-proposer.md` proposes the near-duplicate groups of a lemma and part of speech from the full glosses; `group-reviewer.md` judges them blind, from the glosses only) |

<!-- markdownlint-enable MD013 -->

## Quotas and document roles

A sense needs rows by its tier, from the WordNet 3.0 tag count (`lexindex.QUOTA`): **C** (5 or more tags) 20 anchors and
5 held-out rows, **U** (1 to 4 tags, and every project sense) 10 and 3, **R** (no tag) 5 and 2. An R sense of a lemma
with 15 or more senses needs 3 and 1 (`lexindex.quota_of`).

The role of a real document is per lemma (`qf.doc_role`). One document in four is an evaluation document (chosen by a
hash of the document, `qf.is_eval_document`): it supplies held-out rows only, whatever other lemmas it supplies anchors
for, and every other document supplies anchors only. Within one lemma a document never supplies both: an evaluation
document that already has an anchor of the lemma supplies no held-out row of it, and a document that is not an
evaluation document and already has a held-out row of the lemma and no anchor supplies no anchor
(`qf.real_documents_by_lemma` joins the provenance rows to the lemmas of the rows).

The pool documents are the ones of the open English part-of-speech corpus (`corpus/open/sources.tsv`) and the FAA
documents of `faa.py`; the sources and their licence basis are in "Sources" of `corpus/README.md`. Wikipedia, Wiktionary,
CC BY-SA text, Universal Dependencies data, news sites, NOS, NASA, the FAA documents with third-party rights (the
Parachute Rigger Handbook, the Aviation Weather Handbook, Aerodynamics for Naval Aviators, the MC-4 manual) and the
Tatoeba CC BY sentences are never used.

Every row written by these scripts is targeted (found or written for one sense or form). Every selection skips a
sentence that is already in the corpus, the suite, the open corpus (any split) or the example sentences of Open English
WordNet, using the normalization of `check-leakage` (`qf.corpus_keys`). `fill.py select` rewrites the whole
`quota-w<wave>-*.tsv` files, so never rerun it for a wave that is already in the corpus: the real held-out rows were
corrected in place after a blind relabeling (166 relabeled, 87 removed), and a rerun would silently undo that.
