# Maintainer Tools

The folders below are the maintainer tools of WordSense. They regenerate the word data, the model weights and the
evaluation figures, and they built the corpora. **None of them is shipped**: the NuGet packages contain only the data and
weights that these tools produced. They are used by maintainers who rebuild data or models, not by users of the library.

All commands run from the repository root. The C# builder needs the .NET 10 SDK; the Python scripts need Python 3
(`tools/open-corpus` states Python 3.13), and most use only the standard library. Raw downloads and scratch files go to
git-ignored folders (`cache/`, `work/`) and are never committed.

| Folder | Language | Status |
| --- | --- | --- |
| `WordSense.Builder` | C# (.NET 10) | Maintained: the regular tool |
| `forms-audit` | Python | Maintained; run when `audit-forms` needs verdicts after the form table changes |
| `model-prep` | Python | One-off; rerun only when the BERT encoder file is derived again |
| `open-corpus` | Python | Built the open corpus once; two scripts still feed the builder's evaluation |
| `quota-fill` | Python | Maintained for further quota waves |
| `tokenizer-fixture` | Python | Maintained; run when its sentences or the tokenizer change |

## WordSense.Builder

**What.** The non-shipped command-line tool (`tools/WordSense.Builder`, `net10.0`) that creates every committed derived
artifact: the form table, the word image of the core package, the Basic table, the encoder weights, the senses appendix
of the user guide and the model comparison table. It also validates the data and the corpus, and measures the models.

**Who and when.** A maintainer, after a change to the data in `data/`, the corpus in `corpus/` or the models: to
regenerate the word data (`rebuild-forms`, `label-senses`, `compile-image`), to retrain (`train-basic`, `weights`), to
re-measure (`evaluate`, `evaluate-senses`, `evaluate-allwords`, `evaluate-running`, `evaluate-suite`) and to check the
inputs (`validate-data`,
`check-leakage`, `tiers`).

**Why.** The shipped artifacts must be reproducible from committed inputs: the same inputs give identical bytes, and the
tests compare the committed outputs with a regeneration.

**Prerequisites.** The .NET SDK. `rebuild-forms` needs the pinned Open English WordNet file in `artifacts/inputs`;
`weights` and `evaluate` for the encoder models need the model files of `model/` (Git LFS); `audit-forms` needs Python and
`pip install lemminflect`; the evaluations on SemCor and the treebank need their git-ignored downloads.

**Commands.** `dotnet run --project tools/WordSense.Builder -c Release -- <command> [--name value ...]`. The commands are
`compile-image`, `rebuild-forms`, `audit-forms`, `generate-facts`, `import-moby`, `weights <modelId>`, `evaluate <modelId>`,
`evaluate-senses`, `evaluate-allwords`, `allwords-sample`, `allwords-merge`, `evaluate-running`, `evaluate-suite`,
`check-leakage`, `tiers`, `open-counts`, `evaluate-tagger`,
`coverage`, `train-basic`, `tune-basic`, `appendix`, `import-oewn`, `import-wn30-counts`, `label-senses`,
`seed-inventory` and `validate-data`. Examples:

```text
dotnet run --project tools/WordSense.Builder -c Release -- validate-data
dotnet run --project tools/WordSense.Builder -c Release -- rebuild-forms --oewn artifacts/inputs/english-wordnet-2025.xml.gz
dotnet run --project tools/WordSense.Builder -c Release -- audit-forms --seed 20240601 --output audit.md
dotnet run --project tools/WordSense.Builder -c Release -- evaluate minilm --table docs/user_guide/model-table.md
```

**Documentation.** It has no README of its own. The options of every command are in the XML documentation of
`Program.cs`, the units are designed in `docs/design/word-sense/word-sense-builder.md` and the word-data subsystem in
`docs/design/word-sense/word-data.md`.

## forms-audit

**What.** Python scripts (standard library) that prepare the batches of the audit verdicts for an adjudicator model and
ingest its answers into `tools/WordSense.Builder/audit/adjudications.tsv`, the file that the `audit-forms` gate reads
(`export.py audit|recheck`, `audit_ingest.py check|final|recheck`, the prompt of the adjudicator).

**Who and when.** A maintainer, when a change regenerates the form table and `audit-forms` draws sampled rows that the
checker (`lemminflect`) disagrees with and that have no verdict yet. The one-time authoring of the CC0 file of inflected
forms that replaced the third-party inflection database, `corpus/lexicon/forms-authored.tsv`, is finished; its method is
recorded in `data/PROVENANCE.md` and its scripts were removed.

**Why.** The gate needs our own verdict for every flagged row, never the checker's form, and every verdict is recorded.

**Prerequisites.** Python, `pip install lemminflect` (the checker; nothing ships from it) and a language-model agent as
adjudicator. The batches, the hidden keys and the answers live in a folder outside the repository, chosen with `--home`
or the environment variable `FORMS_AUDIT_HOME` (default `artifacts/forms-audit`, git-ignored, for a dry run only).

**More.** `tools/forms-audit/README.md` (inputs, commands and outputs).

## model-prep

**What.** `make-bert-encoder.py`, one Python script that derives the encoder-only ONNX file of `bert-base-cased-int8` from
the upstream masked-language-model export: it cuts the graph at the output of the last encoder layer, exposes it as
`last_hidden_state` and drops the language-model head. No weight changes.

**Who and when.** A maintainer, once, when the BERT encoder file was produced; again only if it has to be derived anew
(the weights of the package are valid only for the exact pinned file).

**Why.** The upstream file exposes only the language-model output, and WordSense needs the final hidden states.

**Prerequisites.** Python with the `onnx` package and the upstream file (Xenova/bert-base-cased, `onnx/model_int8.onnx`).

**Command.** `python tools/model-prep/make-bert-encoder.py <upstream-model_int8.onnx> <encoder_int8.onnx>`

**More.** It has no README. The provenance and the SHA-256 values are in `model/bert-base-cased-int8/README.md`.

## open-corpus

**What.** The Python scripts (`tools/open-corpus/scripts`) that built the open English part-of-speech corpus `corpus/open`
(CC0 annotations over public-domain text): harvest and extraction of the documents, sentence selection with recorded
seeds, annotation batches and their ingestion, consensus, validation, statistics, and the evaluation against the
convention-mapped treebank. The raw annotator outputs are not kept in the repository.

**Who and when.** A maintainer, when the corpus has to be extended or reproduced, and for two scripts that stay in use:
`prepare_tagger_eval.py` (writes the convention-mapped treebank files that `evaluate-running` and `evaluate-tagger` read)
and `convention_map.py`.

**Why.** The Basic tagger is trained from our own CC0 corpus and not from a treebank, so the corpus and the tools that
made it must be reproducible; the treebank is used only to evaluate.

**Prerequisites.** Python 3.13 with `pip install -r tools/open-corpus/requirements.txt` (`lxml`, `pypdf`); a network for
the harvest, and the annotation steps need language-model agents. Downloads go to `tools/open-corpus/cache/` and scratch
to `tools/open-corpus/work/` (both git-ignored). The tag counts are made by the builder command `open-counts`.

**Commands and more.** It has no README of its own: the recipe, with the real commands, is in section 10 of
`corpus/open/README.md`, for example `python tools/open-corpus/scripts/prepare_tagger_eval.py` (run from `corpus/open` as
described there).

## quota-fill

**What.** Python scripts (standard library only; `pypdf` for `faa.py`) that fill the per-sense quotas of the encoder
corpus with real and with written sentences: the pool of public-domain text, the harvest of candidate sentences, the
batches for the labeler and writer models and their ingestion, selection, coverage reports, the addition of lemmas to
the curation and a diagnostic that re-weights the real test rows by an independent usage prior (`usage_weighted.py`).

**Who and when.** A maintainer extending the training corpus, for example for the lemmas beyond the top 200 (see "Planned
Improvements" in the root README), and when `corpus/coverage` has to be regenerated.

**Why.** Every sense needs enough training sentences for the encoder weights, and the sentences must be public domain or
CC0: every sentence is recorded with its source, and `CorpusLicenseTests` fails on a source that is not allowed (the
list is in `tools/WordSense.Builder/ProvenanceSources.cs`, the evidence in "Sources" of `corpus/README.md`).

**Prerequisites.** Python (`pypdf` for `faa.py`). The work files (sentence pool, batches, answers) and the downloaded
Tatoeba CC0 export live in the git-ignored `tools/quota-fill/cache/` (`cc0.tar.bz2`, `quota/`: `pool.tsv`,
`documents.tsv`, `sources.tsv`, `suite.tsv`, `text/`, `work/`); the downloaded FAA PDFs are read from
`tools/quota-fill/cache/faa` (or the folder in the environment variable `FAA_SOURCE_DIR`).

**More.** `tools/quota-fill/README.md` (every script with its usage, e.g. `harvest.py candidates WAVE`,
`fill.py ingest WAVE`, `coverage.py [WAVE]`); the method is in `corpus/README.md`.

## tokenizer-fixture

**What.** `generate_fixture.py` records the WordPiece ids and UTF-16 offsets that the Hugging Face `tokenizers` library
produces with `model/all-MiniLM-L6-v2-int8/tokenizer.json` for the sentences of `sentences.txt`.

**Who and when.** A maintainer, whenever `sentences.txt` or the tokenizer changes; otherwise never.

**Why.** The unit tests compare the C# `WordPieceTokenizer` with this fixture
(`test/DemaConsulting.WordSense.Onnx.Tests/Fixtures/tokenizer-parity.json`), so a tokenizer regression cannot go unnoticed.

**Prerequisites.** Python with `pip install tokenizers`.

**Command.** `python tools/tokenizer-fixture/generate_fixture.py`

**More.** `tools/tokenizer-fixture/README.md`.
