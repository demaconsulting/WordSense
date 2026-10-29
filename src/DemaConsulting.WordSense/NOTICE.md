# Notice for DemaConsulting.WordSense

## The code and the project data

The code of this package is Copyright DEMA Consulting and is licensed under the MIT license. The data that the project
wrote itself (the closed word lists, the project senses, the readable sense labels and the groups) is
also licensed under the MIT license. The authored inflected forms (`corpus/lexicon/forms-authored.tsv`, written by the
project with language models) are dedicated to the public domain under CC0 1.0.

## The statistical table of the built-in model

The package embeds the table `basic.bin` for `BasicWordSenseModel`. It is a derived statistical table: tag n-gram
counts, word-tag counts and context cue statistics. It is computed from two project sources. The counts and statistics
that it holds are dedicated to the public domain under CC0 1.0 (<https://creativecommons.org/publicdomain/zero/1.0/>), so
the table itself is CC0 1.0:

- the tag counts of the training split of the representative part of the open English part-of-speech corpus of this
  repository (`corpus/open`): the annotations and the selection are ours (CC0 1.0), and the underlying sentences are not
  CC0 as a whole: they are public domain in the United States (Project Gutenberg books published 1930 or earlier, and
  works of US federal agencies) or come from the CC0 export of Tatoeba;
- the context cue statistics of the sentence corpus of this repository (`corpus/anchors` and `corpus/choosers`),
  whose sentences are project-authored (CC0 1.0), public domain in the United States, or Tatoeba sentences under CC0 1.0.

Language models wrote or labeled the annotations of the open corpus and the sentences and labels of `corpus/anchors` and
`corpus/choosers`, and no human has reviewed the labels.
The owner's position on the rights in model outputs is in the repository README, section "Credits and Licenses"
(<https://github.com/demaconsulting/WordSense#credits-and-licenses>).

The table holds four numbers of the word prior of the tagger. The prior itself is computed at run time from the WordNet
3.0 tag counts of the word image, below. No treebank is used for it.

The sentences themselves are not part of the package. No Universal Dependencies treebank, Wikipedia, Wiktionary or other
CC BY-SA text was used to create the table, and no CC BY text was used either (the only CC BY material in the package is
the Open English WordNet data of the word image, below).

## The word image

The package embeds the word image `wordsense.img`. It is a compiled, derived form of the word data of the project and
it is shared under the licenses below. It is compiled from four sources: the Open English WordNet 2025 data (CC BY 4.0,
which carries the Princeton WordNet notice below), the sense frequency counts of WordNet 3.0 (the Princeton WordNet 3.0
license below), the Moby Part-of-Speech List (public domain) and the project-authored
data (the inflected forms authored by the project, CC0 1.0, and the other project data, MIT). The tagged tokens of the
open part-of-speech corpus of the project (annotations CC0 1.0, see above) are used at build time only, as a second
source that
corroborates a generated inflected form; none of its text is embedded. The image holds the lemmas,
the written forms with their analyses, the sense ids, sense keys, labels and groups, the order of the senses of each
lemma, which is the order of Open English WordNet, and the WordNet 3.0 tag count of each sense (a number for each sense,
which the built-in model uses to rank the senses of a lemma and, summed over the lemmas of a spelling, as the word prior
of its part-of-speech tagger; it is never an answer). It does not hold the
glosses, the examples or the relations of the source data.

## Open English WordNet 2025

The sense ids, sense keys, lemmas and written forms of the image are derived from Open English WordNet 2025.

- Source: Open English WordNet 2025 Edition, <https://en-word.net>, <https://github.com/globalwordnet/english-wordnet>.
- License: Creative Commons Attribution 4.0 International (CC BY 4.0), <https://creativecommons.org/licenses/by/4.0/>.
- Changes: the data was imported into tables, extended with project-authored entries and senses, and compiled into a
  binary image. It was changed, and it is not the original Open English WordNet data. Open English WordNet and its
  contributors do not endorse this package.

## WordNet 3.0

The sense usage counts of the word image (the number of times each sense was tagged in the sense-annotated texts of the
WordNet project, from the sense index file `index.sense` of WordNet 3.0, <https://wordnet.princeton.edu>, joined to the
sense keys of Open English WordNet) are data of WordNet 3.0. The package embeds them, in a changed form (a number for
each sense of the catalog, without the other contents of the index), and uses them to rank the senses of a lemma by
usage and to estimate the usual part of speech of a word for the tagger (the sum of the counts of the senses of each
lemma and part of speech that a spelling can be). WordNet 3.0 is used under the following license, reproduced as the
license requires:

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

## Princeton WordNet (the notice that Open English WordNet requires)

Open English WordNet is derived from Princeton WordNet under the WordNet License and developed further under CC BY 4.0;
its `LICENSE.md` says: "You may share and adapt this resource providing attribution is given to both Princeton WordNet
and the Open English Wordnet team." The image therefore carries, with the Open English WordNet data it is derived from,
the notice that Open English WordNet distributes as `WNDB_License.txt` (reproduced as the license requires):

```text
This software and database is being provided to you, the LICENSEE, by
the Open English Wordnet team under the Creative Commons Attribution 4.0
International License (CC-BY 4.0).

Open English Wordnet 2023 Copyright 2023 by the Open English Wordnet team.

Permission to use, copy, modify and distribute this software and
database and its documentation for any purpose and without fee or
royalty is hereby granted, provided that you agree to comply with
the following copyright notice and statements, including the disclaimer,
and that the same appear on ALL copies of the software, database and
documentation, including modifications that you make for internal
use or for distribution.

WordNet 3.1 Copyright 2011 by Princeton University.  All rights reserved.

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

## Moby Part-of-Speech List

The part-of-speech data of the dictionary is derived from the Moby Part-of-Speech List (Moby Part-of-Speech II) by Grady
Ward, <https://www.gutenberg.org/ebooks/3203>, which the author dedicated to the public domain.
