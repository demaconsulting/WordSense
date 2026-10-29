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
| `blindcheck.py export\|judge\|apply WAVE [--kind real\|written\|shape]` | Blind check of the rows a wave selected: a labeler that never sees the gold label (senses in another order) labels every row again (`--every N` takes a sample); the rows it does not confirm go to a second labeler; `apply` keeps a row that either confirms, relabels one that both put in the same other sense of the lemma (never a shape row: it is removed), removes the rest, and writes `chk-report-<kind>.tsv`. The work folders are `chk-*` and `chk2-*` for real rows and `wchk-*` and `wchk2-*` for written and shape rows. Run it after `fill.py select` (real) and before the written top-up |
| `written.py export\|ingest\|labeled WAVE` | The top-up by writing: writer batches (`--only-lemmas` as in `harvest.py`), validation, blind-label batches. `ingest` also reads the shape batches of `shapes.py` (three columns, with the shape) and checks the shape of each sentence |
| `shapes.py fit-export\|fit-apply\|export\|select\|status` | Shape coverage of the noun/verb lemmas (see "Shape coverage" below) |
| `chooser_harvest.py candidates\|select WAVE [--only-lemmas FILE]` | Harvest of chooser rows for the forms that lack a lemma (`--only-lemmas`: the forms that lack one of these lemmas; run it after the fine-sense rows of the wave are selected, because the rows of shared forms become chooser rows) |
| `choosers.py status` | The forms whose chooser rows lack a lemma |
| `coverage.py [WAVE]`, `coverage.py --only-lemmas FILE` | Writes `corpus/coverage/quota-coverage.tsv` and prints the counts. `WAVE` (a rank wave) and `--only-lemmas` (a file of lemmas, for the stage 1 waves) also rewrite the rows of `quota-unfilled.tsv` for those lemmas; the rows of other lemmas stay. When `corpus/curation/shape-fit.tsv` exists it also writes the shape report `corpus/coverage/shape-coverage.tsv` and prints the shape units met and short |
| `shortfix.py check\|export\|merge WAVE STAGE` | Short answers of the labeling agents: lists the batches whose answer file lacks items, writes `<in>-fix` batches with only the missing items, and merges their answers (`STAGE`: `real-sol`, `real-sonnet`, `chk`, `chk2`, `wchk`, `wchk2`, `wr-label`). `fill.py agree`, `blindcheck.py judge` and `apply` and `written.py labeled` refuse to run while an answer is short |
| `usage_weighted.py LABEL=OUTCOMES.tsv ...` | Diagnostic: re-weights the real test rows of `evaluate --outcomes` files by the harvest share of each sense (an independent usage prior) and prints the weighted fine top-1 and sense-level figures (see the user guide chapter "Accuracy and Model Choice") |
| `prompts/` | The instructions given to the labeler, the writers (`sentence-writer.md`, and `sentence-writer-shape.md` for the shape rows), the lemma chooser, the label reviewer, the shape-fit judge (`shape-fit.md`) and the group proposer and reviewer (`group-proposer.md` proposes the near-duplicate groups of a lemma and part of speech from the full glosses; `group-reviewer.md` judges them blind, from the glosses only) |

<!-- markdownlint-enable MD013 -->

## Quotas and document roles

**Stages.** The waves are numbered in blocks of 50 per stage: waves 1 to 49 are the base stage, waves 50 to 99 stage 1
(`quota-w50-*` to `quota-w55-*`), and so on. The stage of a lemma is the stage of the highest `quota-w<wave>` file that
holds one of its anchors (the wave counts as its leading digits, so `quota-w4c-*` is wave 4; other files such as
`batch-1.tsv` do not count). The builder uses the stage in two places (`Corpus.StagesOf`, see the builder design): the
real-text calibration of the thresholds is fitted and applied per stage, so a stage never moves the thresholds of an
earlier one, and the cue tables of the basic model learn from the sense anchors of the base stage plus all the form-chooser
rows (the chooser rows of every stage count, the sense anchors of a later stage do not). A wave of a new stage
takes the next block of numbers.

A sense needs rows by its tier, from the WordNet 3.0 tag count (`lexindex.QUOTA`): **C** (5 or more tags) 20 anchors and
5 held-out rows, **U** (1 to 4 tags, and every project sense) 10 and 3, **R** (no tag) 5 and 2. An R sense of a lemma
with 15 or more senses needs 3 and 1 (`lexindex.quota_of`).

**Size budget.** A wave is sized against the weights budget of 25 MB for `BertBaseCased` and 15 MB for `MiniLM` (decimal
bytes of the `weights.bin` files). Each anchored sense adds about 1.4 KB to the BERT weights and 0.7 KB to the MiniLM
weights, so train once with the planned lemmas and read the two sizes before adding more.
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

## Shape coverage

A word that is both a noun and a verb (`check`, `call`, `survey`) is easy to misread when its anchors lack a common
sentence shape: `Do a quick check.` reads as a verb when no anchor has a noun after a light verb. `shapes.py` writes
anchors in seven shapes (`SHAPES` holds the rule and an example of each):

| Shape | Part of speech | The target word is |
| --- | --- | --- |
| `n-obj-light` | Noun | the object of a light verb (do, make, run, perform, take, give, have) after a determiner |
| `n-compound-head` | Noun | the head of a noun compound, after a modifier noun |
| `n-modifier` | Noun | a noun that modifies the noun after it (skipped for a lemma that has an adjective sense) |
| `n-subject-initial` | Noun | the first word of the sentence and its subject, with no article |
| `n-measure` | Noun | a noun in a measurement or quantity frame with a number |
| `v-imperative-initial` | Verb | the first word of an instruction, in the base form |
| `v-after-modal` | Verb | in the base form after a modal or the infinitive marker `to` |

**Shape-fit table.** Not every sense is natural in every shape. `corpus/curation/shape-fit.tsv` (`sense_id`, `shapes`)
lists, for every noun and verb sense of the lemmas in scope, the shapes of its own part of speech in which it is used
naturally. A judge model fills it from the gloss with `prompts/shape-fit.md`: `shapes.py fit-export --lemmas FILE` writes
the judge batches to `work/shapefit/in`, and `shapes.py fit-apply` reads `work/shapefit/out/*.tsv` into the table (a
shape of the other part of speech or an unknown shape is dropped).

**Quota.** A unit is a lemma, a part of speech and a shape with at least one fitting sense (a fitting sense has the shape
in the table and at least 3 anchors). Every unit needs at least 2 kept written rows (`MIN_ROWS`). The most frequent two
fitting senses share the rows of a unit; a sense gets at most 3 rows of a shape.

**Provenance.** A shape row is an ordinary written anchor. Its provenance `source_key` carries the shape,
`quota-w<wave>-shape:<shape>`, and the rows are in `quota-w<wave>-shape.tsv` of `anchors`, `provenance` and
`choosers/anchors`. The status counts the rows of a unit from these keys. The wave decides the stage of the lemmas
(`Corpus.StagesOf`): 49 for the base stage, 99 for stage 1, 110 for stage 2.

**Pipeline.**

1. `shapes.py export WAVE --lemmas FILE [--short]` writes the writer batches of the units that have no row (`--short`: the
   units below the minimum, a second round) to `work/w<WAVE>/wr-in`. Each entry names the sense, the shape and its rule.
2. The writers follow `prompts/sentence-writer-shape.md` and answer `sense<TAB>shape<TAB>sentence`.
3. `written.py ingest WAVE` validates the sentences as for any written row and also checks the shape mechanically
   (`shapes.check_shape`: the position and the neighbors of the braced word, for example a light verb and a determiner
   before the target of `n-obj-light`). The sense is left to the blind label.
4. The blind labelers label the sentences (`written.py labeled WAVE`), then `shapes.py select WAVE` keeps the rows
   whose label equals the intended sense with high confidence, skipping a duplicate of the corpus and a near copy (token
   Jaccard 0.8) of a corpus row or of a sentence of the shape probe set. Never rerun it for a checked wave.
5. `blindcheck.py export|judge|apply WAVE --kind shape` checks the kept rows blind. A row that is not confirmed is
   removed, never relabeled.

`shapes.py status [--lemmas FILE]` prints the units met and short for each shape, and `coverage.py` writes them to
`corpus/coverage/shape-coverage.tsv` (`lemma`, `part_of_speech`, `shape`, `fitting_senses`, `written_rows`, `status`).

The held-out shape probe set `corpus/shapes/probe.tsv` measures the effect (`WordSense.Builder evaluate-shapes`). It is
never trained on, and no row written here may repeat one of its sentences (`qf.corpus_keys`).
