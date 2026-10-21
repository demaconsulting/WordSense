# Notice for DemaConsulting.WordSense.Basic

## The code

The code of this package is Copyright DEMA Consulting and is licensed under the MIT license.

## The statistical table

The package embeds the table `basic.bin`. It is a derived statistical table: tag n-gram counts, word-tag counts and
context cue statistics. It is computed from two project-authored sources, both released under CC0 1.0 (public domain
dedication, <https://creativecommons.org/publicdomain/zero/1.0/>):

- the tag counts of the training split of the representative part of the open English part-of-speech corpus of this
  repository (`corpus/open`): the annotations are ours, and the underlying sentences are public domain in the United
  States (Project Gutenberg books published 1930 or earlier, and works of US federal agencies) or come from the CC0 export
  of Tatoeba;
- the context cue statistics of the sentence corpus of this repository (`corpus/anchors` and `corpus/choosers`),
  whose sentences are project-authored (CC0 1.0), public domain in the United States, or Tatoeba sentences under CC0 1.0.

The sentences themselves are not part of the package. No Universal Dependencies treebank, Wikipedia, Wiktionary or other
CC BY or CC BY-SA text was used to create the table.
