# Legacy corpus (Basic model only)

This folder holds the sentences of the former class-labeled corpus, from before the switch to Open English WordNet
(OEWN) fine senses. Their rows carry the **legacy class labels** (a part of speech or a pronunciation class such as
`noun`, `verb` or `weapon`), not fine sense ids, so they do **not** train or evaluate the fine-sense weights of the
encoder models. They were relabeled to fine senses (a relabel by rule where a class has one fine sense, a judged relabel
of the others, see "Relabeled Legacy Corpus" in `../README.md`); the relabeled rows are in `../anchors`, `../holdout`
and `../realtext` (`relabel-*.tsv`) and their origin in `../provenance/relabel.tsv`.

<!-- markdownlint-disable MD013 -->

| Folder | Content | Used by |
| ------ | ------- | ------- |
| `anchors/`, `holdout/` | Legacy training and evaluation sentences, `lemma`, `label`, `sentence` (3 columns) | `train-basic` and `tune-basic` (the Basic model learns analysis labels rolled up from the legacy labels) |
| `senses/` | The retired class inventory: `lemma`, `label`, `pos`, `meaning`, `respelling` | `train-basic`, `tune-basic` (through `LegacySenseInventory`) |
| `forms/` | The retired form-to-lemma map with the labels each form can take | `train-basic` (legacy label check) |
| `realtext/` | Real sentences for evaluation only (never trained on), legacy labels | nothing (the relabeled copy is `../realtext/relabel-realtext.tsv`) |
| `provenance/` | Source, license, URL and contributor of every real sentence of the legacy corpus | the license record of the legacy rows |
| `excluded-cc-by-sa/` | The legacy rows from CC BY-SA 4.0 sources (Wikipedia, Wiktionary, the UD English treebank), in the legacy row format, with their provenance | nothing; they are not relabeled and never train an encoder model |

<!-- markdownlint-enable MD013 -->

The Basic model is not retrained in this version except to match the word image, but its builder commands still read
this folder so that its table stays reproducible (`train-basic` reproduces the committed `basic.bin` byte for byte).
The folder is therefore kept whole, **including the rows that `excluded-cc-by-sa/` lists**: the files of `anchors/`,
`holdout/` and `realtext/` are the unchanged input of the Basic table, and the CC BY-SA rows in them are statistics
inputs of that table only (a decision for the owner: retrain Basic without them, or keep it as it is). The folder is
deleted when the Basic model no longer needs it.

`excluded-cc-by-sa/` has `anchors.tsv`, `holdout.tsv` and `realtext.tsv` (3-column legacy rows: `lemma`, `label`,
`sentence`) and `provenance.tsv` (the provenance rows of the original sidecars).

Every file is UTF-8 without a byte-order mark, with LF line endings. The row format and the validation rules of the
legacy files are those described in `../README.md` under "Sense Files and Forms File (legacy)".
