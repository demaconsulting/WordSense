# Legacy corpus (awaits relabeling)

This folder holds the sentences of the former class-labeled corpus, from before the switch to Open English WordNet
(OEWN) fine senses. Their rows carry the **legacy class labels** (a part of speech or a pronunciation class such as
`noun`, `verb` or `weapon`), not fine sense ids, so they are **not used to train or evaluate the fine-sense weights** of
the encoder models. They await relabeling to fine senses (steps D1 and D2 of the switch plan: a deterministic relabel
of the rows whose class has one fine sense, a judged relabel of the others); until then the new corpus in `../anchors`,
`../holdout` and `../provenance` (the first reviewed batch of 20 lemmas) is the only training data of the encoder
models.

<!-- markdownlint-disable MD013 -->

| Folder | Content | Used by |
| ------ | ------- | ------- |
| `anchors/`, `holdout/` | Legacy training and evaluation sentences, `lemma`, `label`, `sentence` (3 columns) | `train-basic` and `tune-basic` (the Basic model learns analysis labels rolled up from the legacy labels) |
| `senses/` | The retired class inventory: `lemma`, `label`, `pos`, `meaning`, `respelling` | `train-basic`, `tune-basic` (through `LegacySenseInventory`) |
| `forms/` | The retired form-to-lemma map with the labels each form can take | `train-basic` (legacy label check) |
| `realtext/` | Real sentences for evaluation only (never trained on), legacy labels | nothing until relabeled |
| `provenance/` | Source, license, URL and contributor of every real sentence of the legacy corpus | the license record of the legacy rows |

<!-- markdownlint-enable MD013 -->

The Basic model is not retrained in this version, but its builder commands still read this folder so that its table
stays reproducible (`train-basic` reproduces the committed `basic.bin` byte for byte). The folder is deleted when the
relabeled rows have replaced it.

Every file is UTF-8 without a byte-order mark, with LF line endings. The row format and the validation rules of the
legacy files are those described in `../README.md` under "Sense Files and Forms File (legacy)".
