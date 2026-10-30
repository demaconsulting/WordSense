# Notice for DemaConsulting.WordSense.MiniLM

## The code and the project data

The code of this package is Copyright DEMA Consulting and is licensed under the MIT license. The data that the project
wrote itself (the readable sense labels) is also licensed under the MIT license.

## The embedded weights

The package embeds the trained weights `weights.bin` for `MiniLMWordSenseModel`. They hold, for each trained lemma, a
numeric classifier on the embeddings of the encoder below (weights, biases and thresholds), together with the lemma, the
ids of its senses (`lemma#label`, the readable labels are project-authored) and the labels of the form choosers. They
are computed from two project-authored sources, one WordNet 3.0 source and the sense ids of Open English WordNet:

- the sentence corpus of this repository (`corpus/anchors`, `corpus/choosers` and the real-text sentences of
  `corpus/realtext` used to calibrate the thresholds). The rows and labels that the project wrote are released under CC0
  1.0 (public domain dedication, <https://creativecommons.org/publicdomain/zero/1.0/>); the corpus as a whole is not
  CC0, because its real sentences are public domain in the United States (Project Gutenberg books published 1930 or
  earlier, and works of US federal agencies) or Tatoeba sentences of the CC0 export. The sentences themselves are not
  part of the package; the weights are statistical summaries of them. Language models wrote or labeled the project rows,
  and no human has reviewed the labels. The owner's position on the rights in model outputs is in the repository README,
  section "Credits and Licenses" (<https://github.com/demaconsulting/WordSense#credits-and-licenses>).
- the sense frequency counts of WordNet 3.0 (the sense index file `index.sense`, <https://wordnet.princeton.edu>), which
  the training uses as a sense usage prior: the tempered logarithm of the usage share of each sense (its tag count) is
  added to the stored bias of the sense. The weights are therefore a modification of data derived from WordNet 3.0, and
  the WordNet 3.0 license below applies to them.
- the Open English WordNet 2025 sense catalog (CC BY 4.0): the weights hold the sense ids of its lemmas (a sense id
  `lemma#label` stands for an Open English WordNet sense, with its sense key in the word image of the core package), so
  they are a work that incorporates Open English WordNet identifiers and are shared under CC BY 4.0 together with the
  MIT license of the code, see the section below.

## The embedded part-of-speech head

The package also embeds `head.bin`, a linear classifier for the part of speech that reads the vectors of the encoder
below for a word and its previous and next word. The file holds numeric weights and biases, the identity of the encoder
file it was trained on, the order of the part-of-speech classes and the digest of its training file. It holds no text.
`MiniLMWordSenseModel` does not use the head.

- It is trained only on the training split of the open part-of-speech corpus of this repository
  (`corpus/open/representative`), and its settings were chosen on the development split of that corpus. The annotations
  and the selection of the corpus are released under CC0 1.0 (`corpus/open/LICENSE`), and its sentences are public
  domain in the United States or CC0 (`corpus/open/sources.tsv`). The English EWT treebank (CC BY-SA 4.0) and every
  other evaluation set are never used to train it or to choose its settings.
- The tags of the corpus were produced by language models (gpt-6.1-sol for every token, and gemma4:26b as a second
  annotator for part of the corpus; no tag was edited by hand), see `corpus/open/README.md`. The license position that
  covers them is stated in the section Credits and Licenses of the
  [README](https://github.com/demaconsulting/WordSense#credits-and-licenses).
- The head is computed from the outputs of the encoder below and is not an Apache-licensed file. The Apache License 2.0
  attribution of the encoder files below is unchanged.

## The encoder model files are not in this package

The package does not contain the encoder model. The files `model_int8.onnx` and `vocab.txt` are downloaded at first use
(or taken from a model directory that the caller gives), verified by SHA-256, from the Git LFS storage of this
repository, <https://github.com/demaconsulting/WordSense>, folder `model/all-MiniLM-L6-v2-int8/`. They are licensed
under the Apache License 2.0 (<https://www.apache.org/licenses/LICENSE-2.0>); the license text and the provenance are
in `model/all-MiniLM-L6-v2-int8/LICENSE` and `model/all-MiniLM-L6-v2-int8/README.md` of the repository.

- Base model: `sentence-transformers/all-MiniLM-L6-v2` (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>),
  Apache License 2.0.
- Changes: the model was exported to ONNX (output `last_hidden_state`) and quantized to int8 with ONNX Runtime dynamic
  quantization. The files were changed, and they are not the original model. The authors of the base model do not
  endorse this package.

## WordNet 3.0

The sense usage prior of the weights is derived from the sense frequency counts of WordNet 3.0. WordNet 3.0 is used
under the following license, reproduced as the license requires:

```text
WordNet Release 3.0

This software and database is being provided to you, the LICENSEE, by
Princeton University under the following license.  By obtaining, using
and/or copying this software and database, you agree that you have
read, understood, and will comply with these terms and conditions.:

Permission to use, copy, modify and distribute this software and
database and its documentation for any purpose and without fee or
royalty is hereby granted, provided that you agree to comply with
the following copyright notice and statements, including the disclaimer,
and that the same appear on ALL copies of the software, database and
documentation, including modifications that you make for internal
use or for distribution.

WordNet 3.0 Copyright 2006 by Princeton University.  All rights reserved.

THIS SOFTWARE AND DATABASE IS PROVIDED "AS IS" AND PRINCETON
UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR
IMPLIED.  BY WAY OF EXAMPLE, BUT NOT LIMITATION, PRINCETON
UNIVERSITY MAKES NO REPRESENTATIONS OR WARRANTIES OF MERCHANT-
ABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE
OF THE LICENSED SOFTWARE, DATABASE OR DOCUMENTATION WILL NOT
INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR
OTHER RIGHTS.

The name of Princeton University or Princeton may not be used in
advertising or publicity pertaining to distribution of the software
and/or database.  Title to copyright in this software, database and
any associated documentation shall at all times remain with
Princeton University and LICENSEE agrees to preserve same.
```

## Open English WordNet 2025

The sense ids of the weights come from Open English WordNet 2025.

- Source: Open English WordNet 2025 Edition, <https://en-word.net>, <https://github.com/globalwordnet/english-wordnet>.
- License: Creative Commons Attribution 4.0 International (CC BY 4.0), <https://creativecommons.org/licenses/by/4.0/>.
  Open English WordNet is derived from Princeton WordNet; the Princeton notice that it requires is reproduced in the
  `NOTICE.md` of the core package `DemaConsulting.WordSense`.
- Changes: the sense ids were joined with project-authored readable labels and used as the classes of the trained
  classifiers; no text of Open English WordNet (glosses, examples) is part of the weights. The data was changed, and it
  is not the original Open English WordNet data. Open English WordNet and its contributors do not endorse this package.

## Related packages

The package depends on `DemaConsulting.WordSense` (core) and `DemaConsulting.WordSense.Onnx`, which carry their own
notices; the core `NOTICE.md` covers the Open English WordNet 2025 data (CC BY 4.0) and the Princeton WordNet notice of
the word image.
