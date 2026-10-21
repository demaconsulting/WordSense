# Notice for DemaConsulting.WordSense

## The code and the project data

The code of this package is Copyright DEMA Consulting and is licensed under the MIT license. The data that the project
wrote itself (the closed word lists, the project senses, the readable sense labels, the groups and the respellings) is
also licensed under the MIT license.

## The statistical table of the built-in model

The package embeds the table `basic.bin` for `BasicWordSenseModel`. It is a derived statistical table: tag n-gram
counts, word-tag counts and context cue statistics. It is computed from two project-authored sources, both released under
CC0 1.0 (public domain dedication, <https://creativecommons.org/publicdomain/zero/1.0/>), so the table itself is CC0 1.0:

- the tag counts of the training split of the representative part of the open English part-of-speech corpus of this
  repository (`corpus/open`): the annotations are ours, and the underlying sentences are public domain in the United
  States (Project Gutenberg books published 1930 or earlier, and works of US federal agencies) or come from the CC0 export
  of Tatoeba;
- the context cue statistics of the sentence corpus of this repository (`corpus/anchors` and `corpus/choosers`),
  whose sentences are project-authored (CC0 1.0), public domain in the United States, or Tatoeba sentences under CC0 1.0.

The sentences themselves are not part of the package. No Universal Dependencies treebank, Wikipedia, Wiktionary or other
CC BY-SA text was used to create the table, and no CC BY text was used either (the only CC BY material in the package is
the Open English WordNet data of the word image, below).

## The word image

The package embeds the word image `wordsense.img`. It is a compiled, derived form of the word data of the project and
it is shared under the licenses below. The image holds the lemmas, the written forms with their analyses, the sense
ids, sense keys, labels and groups, and the order of the senses of each lemma. It does not hold the glosses, the
examples or the relations of the source data.

## Open English WordNet 2025

The sense ids, sense keys, lemmas and written forms of the image are derived from Open English WordNet 2025.

- Source: Open English WordNet 2025 Edition, <https://en-word.net>, <https://github.com/globalwordnet/english-wordnet>.
- License: Creative Commons Attribution 4.0 International (CC BY 4.0), <https://creativecommons.org/licenses/by/4.0/>.
- Changes: the data was imported into tables, extended with project-authored entries and senses, and compiled into a
  binary image. It was changed, and it is not the original Open English WordNet data. Open English WordNet and its
  contributors do not endorse this package.

## WordNet 3.0

The order of the senses of a lemma is derived from the sense frequency counts of WordNet 3.0 (the sense index file
`index.sense`, <https://wordnet.princeton.edu>). WordNet 3.0 is used under the following license, reproduced as the
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

## Automatically Generated Inflection Database (AGID)

Some irregular inflected forms of the image are derived from the Automatically Generated Inflection Database (AGID),
version 2016.01.19, Kevin Atkinson, <http://wordlist.aspell.net/other-dicts/>. AGID is read at build time only; its
notices are reproduced below. They are the notice of its author, the notice of the WordNet database from which its
part-of-speech data is taken, and the notice of the UK Advanced Cryptics Dictionary, one of its word lists.

Permission notice of AGID:

```text
Copyright 2000-2014 by Kevin Atkinson

Permission to use, copy, modify, distribute and sell this database,
the associated scripts, the output created form the scripts and its
documentation for any purpose is hereby granted without fee,
provided that the above copyright notice appears in all copies and
that both that copyright notice and this permission notice appear in
supporting documentation. Kevin Atkinson makes no representations
about the suitability of this array for any purpose. It is provided
"as is" without express or implied warranty.
```

Princeton University WordNet notice carried by AGID:

```text
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

WordNet 1.6 Copyright 1997 by Princeton University.  All rights reserved.

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

The UK Advanced Cryptics Dictionary notice carried by AGID:

```text
Copyright (c) J Ross Beresford 1993-1999. All Rights Reserved.

The following restriction is placed on the use of this
publication: if The UK Advanced Cryptics Dictionary is used
in a software package or redistributed in any form, the
copyright notice must be prominently displayed and the text
of this document must be included verbatim.

There are no other restrictions: I would like to see the
list distributed as widely as possible.
```

## Moby Part-of-Speech List

The part-of-speech data of the dictionary is derived from the Moby Part-of-Speech List (Moby Part-of-Speech II) by Grady
Ward, <https://www.gutenberg.org/ebooks/3203>, which the author dedicated to the public domain.
