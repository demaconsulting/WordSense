# Maintainer Tools

These folders hold the maintainer tools of WordSense. They regenerate the word data, the model weights and the
evaluation figures. **None of them is shipped**: the NuGet packages contain only the data and weights that the tools
produced. Library users do not need them.

All commands run from the repository root. The C# builder needs the .NET 10 SDK. The Python scripts need Python 3
(`tools/open-corpus` needs 3.13), and most use only the standard library. `lint.ps1` needs a real Python virtual
environment in `.venv`; an empty `.venv` folder makes it fail without a message, so delete it first. Raw downloads and
scratch files go to the git-ignored folders `cache/` and `work/`.

| Folder | Language | Use |
| --- | --- | --- |
| `WordSense.Builder` | C# (.NET 10) | The regular tool |
| `forms-audit` | Python | Verdicts for `audit-forms` after the form table changes |
| `model-prep` | Python | Derives the BERT encoder files |
| `model-release` | PowerShell | Publishes model files as GitHub release assets (append only) |
| `open-corpus` | Python | Builds the open corpus; two scripts feed the builder's evaluation |
| `quota-fill` | Python | Fills the per-sense quotas of the corpus |
| `tokenizer-fixture` | Python | Regenerates the tokenizer parity fixture |

## WordSense.Builder

**What.** The command-line tool (`tools/WordSense.Builder`, `net10.0`) that creates every committed derived artifact:
the form table, the word image of the core package, the Basic table, the encoder weights, the senses appendix of the
user guide and the model comparison table. It also validates the data and the corpus and measures the models.

**When.** After a change to `data/`, `corpus/` or the models:

- regenerate the word data: `rebuild-forms`, `label-senses`, `compile-image`;
- retrain: `train-basic`, `weights`, `train-pos-head`, `train-tense-head`;
- re-measure: `evaluate`, `evaluate-senses`, `evaluate-allwords`, `evaluate-gold2`, `evaluate-running`,
  `evaluate-suite`, `evaluate-shapes`;
- check the inputs: `validate-data`, `check-leakage`, `tiers`.

**Why.** The shipped artifacts must be reproducible from committed inputs. The same inputs give identical bytes, and
the tests compare the committed outputs with a regeneration.

**Prerequisites.** The .NET SDK. `rebuild-forms` needs the pinned Open English WordNet file in `artifacts/inputs`.
`weights` and `evaluate` for the encoder models need the model files: run `fetch-models` (it downloads the release assets
into the git-ignored `model/<base-model>/v<n>/`). `audit-forms` needs Python
and `pip install lemminflect`. The evaluations on SemCor and the treebank need their git-ignored downloads.

**Commands.** `dotnet run --project tools/WordSense.Builder -c Release -- <command> [--name value ...]`. The commands
are `compile-image`, `rebuild-forms`, `audit-forms`, `generate-facts`, `import-moby`, `weights <modelId>`,
`train-pos-head <modelId>`, `train-tense-head <modelId>`, `fetch-models`,
`evaluate <modelId>`, `evaluate-senses`, `evaluate-allwords`, `allwords-sample`, `allwords-merge`, `gold2-power`,
`gold2-sample`, `gold2-batches`, `gold2-merge`, `evaluate-gold2`, `compare-gold2`, `evaluate-running`,
`evaluate-suite`, `evaluate-shapes`, `check-leakage`, `tiers`, `open-counts`, `evaluate-tagger`, `coverage`,
`train-basic`, `tune-basic`,
`appendix`, `import-oewn`, `import-wn30-counts`, `label-senses`, `seed-inventory` and `validate-data`. Examples:

```text
dotnet run --project tools/WordSense.Builder -c Release -- validate-data
dotnet run --project tools/WordSense.Builder -c Release -- rebuild-forms --oewn artifacts/inputs/english-wordnet-2025.xml.gz
dotnet run --project tools/WordSense.Builder -c Release -- audit-forms --seed 20240601 --output audit.md
dotnet run --project tools/WordSense.Builder -c Release -- evaluate minilm --table docs/user_guide/model-table.md
```

**Documentation.** The options of every command are in the XML documentation of `Program.cs`. The design is in
`docs/design/word-sense/word-sense-builder.md` and `docs/design/word-sense/word-data.md`.

## forms-audit

**What.** Python scripts (standard library) that prepare the batches of audit verdicts for an adjudicator model and
ingest its answers into `tools/WordSense.Builder/audit/adjudications.tsv`, the file that the `audit-forms` gate reads
(`export.py audit|recheck`, `audit_ingest.py check|final|recheck`, the prompt of the adjudicator).

**When.** After a change regenerates the form table and `audit-forms` draws sampled rows that the checker
(`lemminflect`) disagrees with and that have no verdict yet.

**Why.** The gate needs our own verdict for every flagged row, never the checker's form, and every verdict is recorded.
The method of the authored forms (`corpus/lexicon/forms-authored.tsv`) is in `data/PROVENANCE.md`.

**Prerequisites.** Python, `pip install lemminflect` (the checker; nothing ships from it) and a language-model agent as
adjudicator. The batches, the hidden keys and the answers live in a folder outside the repository, chosen with `--home`
or the environment variable `FORMS_AUDIT_HOME` (default `artifacts/forms-audit`, git-ignored, for a dry run only).

**More.** `tools/forms-audit/README.md`.

## model-prep

**What.** `make-bert-encoder.py` derives the encoder-only ONNX file of `bert-base-cased` v1 from the upstream int8
masked-language-model export: it cuts the graph at the output of the last encoder layer, exposes it as
`last_hidden_state` and drops the language-model head. The weights are unchanged. `make-bert-static-encoder.py` derives
`bert-base-cased` v2: it cuts the upstream fp32 export the same way, converts it to operator set 13, quantizes it
statically (per-channel int8 weights, the pinned ranges of `calib-ranges.json` measured over the calibration set
`calib.tsv`), finalizes the graph and adds the taps `hidden_9` to `hidden_11`. It checks the SHA-256 of every step and of
the result. `make-calib.py` draws `calib.tsv` from the train split of the open corpus and checks the result.

**When.** Only to derive an encoder file anew. The weights of the package are valid only for the exact pinned file.
`.github/workflows/model-prep.yaml` (run by hand) rebuilds the v2 file on a hosted runner from the pinned upstream
revision and fails on any difference. To pin another revision, set the two
upstream values in `make-bert-static-encoder.py` back to the placeholder `TODO-PIN-...` and run the workflow with the
input `bootstrap`: it resolves the revision, prints it with the SHA-256 of the upstream file and states that its output
is unpinned; pin both values (`--accept-unpinned-upstream` is only for that run) and run the workflow again without
`bootstrap`.

**Why.** The upstream files expose only the language-model output, and WordSense needs the hidden states; the static
quantization gives the same answers on every x86 CPU (see `model/bert-base-cased/v2/model-int8-static.md`).

**Prerequisites.** v1: Python with the `onnx` package and the upstream file (Xenova/bert-base-cased,
`onnx/model_int8.onnx`). v2: the packages pinned in `tools/model-prep/requirements.txt`
(`python -m pip install -r tools/model-prep/requirements.txt`), the upstream revision and the tokenizer of
`model/bert-base-cased/v1/` (`fetch-models`).

**Command.** v1: `python tools/model-prep/make-bert-encoder.py <upstream-model_int8.onnx> <model-int8-dynamic.onnx>`.
v2: `python tools/model-prep/make-bert-static-encoder.py <model-int8-static.onnx> --upstream-revision <revision>`, or
`--fp32 <cut graph at opset 13>` to start after the download.

**More.** The provenance, the steps and the SHA-256 values are in `model/bert-base-cased/README.md` and the documents of
each version. The result is published with `tools/model-release`.

## model-release

**What.** `publish-model.ps1` publishes the files of a model as the assets of the GitHub release
`models/<base-model>/v<n>`, with a `.sha256` sidecar per asset, the documents of the release (`README.md` and
`model-<quantization>.md`) and a body that links to them. Releases are append only.

**When.** For a new model, a new version of a model or a new quantization, and to add documents or rewrite the body of an
existing release (`-UpdateBody`).

**Why.** The library downloads the model files from these assets, and the pins in the packages are checked against them.

**Prerequisites.** PowerShell and an authenticated `gh` with write access to the repository.

**Command.** `& tools/model-release/publish-model.ps1 -BaseModel <id> -Version <n> -File '<asset>=<path>', ...
[-UpdateBody] [-DryRun]`

**More.** `tools/model-release/README.md`.

## open-corpus

**What.** The Python scripts (`tools/open-corpus/scripts`) that build the open English part-of-speech corpus
`corpus/open` (CC0 annotations and selection over public-domain and CC0 sentences): harvest and extraction of the
documents, sentence selection
with recorded seeds, annotation batches and their ingestion, consensus, validation, statistics, and the evaluation
against the convention-mapped treebank. The raw annotator outputs are not kept in the repository.

**When.** To extend or reproduce the corpus. Two scripts are in regular use: `prepare_tagger_eval.py` writes the
convention-mapped treebank files that `evaluate-running` and `evaluate-tagger` read, and `convention_map.py` is its
mapping.

**Why.** The Basic tagger is trained from our own corpus (CC0 annotations), not from a treebank. The treebank is used
only to
evaluate.

**Prerequisites.** Python 3.13 with `pip install -r tools/open-corpus/requirements.txt` (`lxml`, `pypdf`). The harvest
needs a network and the annotation steps need language-model agents. Downloads go to `tools/open-corpus/cache/` and
scratch to `tools/open-corpus/work/` (both git-ignored). The builder command `open-counts` makes the tag counts.

**Commands.** Section 10 of `corpus/open/README.md` has the recipe, for example
`python tools/open-corpus/scripts/prepare_tagger_eval.py` (run from `corpus/open` as described there). The tests of the
validation and the statistics scripts run with `python -m unittest discover -s tools/open-corpus/tests` (standard
library only).

## quota-fill

**What.** Python scripts (standard library only; `pypdf` for `faa.py`) that fill the per-sense quotas of the encoder
corpus with real and with written sentences: the pool of public-domain text, the harvest of candidate sentences, the
batches for the labeler and writer models and their ingestion, selection, coverage reports (the sense quotas and the
sentence-shape coverage, `shapes.py`), the addition of lemmas to
the curation and a diagnostic that re-weights the real test rows by an independent usage prior (`usage_weighted.py`).

**When.** To extend the training corpus, for example with further lemmas, and to regenerate `corpus/coverage`.

**Why.** Every sense needs enough training sentences for the encoder weights, and every sentence must be public domain
or CC0. Each sentence is recorded with its source, and `CorpusLicenseTests` fails on a source that is not allowed. The
list is in `tools/WordSense.Builder/ProvenanceSources.cs` and the evidence in "Sources" of `corpus/README.md`.

**Prerequisites.** Python (`pypdf` for `faa.py`). The work files (sentence pool, batches, answers) and the downloaded
Tatoeba CC0 export live in the git-ignored `tools/quota-fill/cache/` (`cc0.tar.bz2`, `quota/`: `pool.tsv`,
`documents.tsv`, `sources.tsv`, `suite.tsv`, `text/`, `work/`). The downloaded FAA PDFs are read from
`tools/quota-fill/cache/faa` (or the folder in the environment variable `FAA_SOURCE_DIR`).

**More.** `tools/quota-fill/README.md` has every script with its usage, for example `harvest.py candidates WAVE`,
`fill.py ingest WAVE` and `coverage.py [WAVE]`. The method is in `corpus/README.md`.

## tokenizer-fixture

**What.** `generate_fixture.py` records the WordPiece ids and UTF-16 offsets that the Hugging Face `tokenizers` library
produces with `model/all-MiniLM-L6-v2/v1/tokenizer.json` (the `tokenizer.json` asset of the release
`models/all-MiniLM-L6-v2/v1`; the library does not download it) for the sentences of `sentences.txt`.

**When.** When `sentences.txt` or the tokenizer changes.

**Why.** The unit tests compare the C# `WordPieceTokenizer` with this fixture
(`test/DemaConsulting.WordSense.Onnx.Tests/Fixtures/tokenizer-parity.json`), so a tokenizer regression is caught.

**Prerequisites.** Python with `pip install tokenizers`.

**Command.** `python tools/tokenizer-fixture/generate_fixture.py`

**More.** `tools/tokenizer-fixture/README.md`.
