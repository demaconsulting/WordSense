# Coarse class mapping

The tables `coarse_class`, `class_sense` and `class_entry` map the labeled senses of `corpus/senses` (1,521 labels of
744 lemmas) onto the Open English WordNet senses of `data/`. They were produced from a reviewed proposal
(embedding, pronunciation and corpus-sentence evidence; a 80-lemma stratified sample was hand-reviewed with 100%
agreement on the trivial lemmas and 95% on the others) and are authoritative now: edit them by hand.

## Classes

| Kind | Count | Meaning |
| --- | --- | --- |
| `sense_group` | 1,365 | a group of OEWN senses; for 1,315 of them the whole part of speech of the lemma is one class |
| `no_oewn` | 120 | no OEWN sense; a supplementary `dema` entry, synset, gloss and sense stands for it |
| `unmapped` | 80 | the senses of a lemma and part of speech that no other class covers (the explicit "other" bucket) |
| `lemma_split` | 32 | the "lemma" is an inflected form; `class_entry` links the base entries |
| `feature_only` | 4 | `read` and `reread`: past or present is a feature (`Tense=Past`, `Tense=Pres`), not a sense |

The class id is `<lemma>#<label>`; the bucket label is `unmapped-<pos>`.

## Partition rule

When a lemma and part of speech has a class of kind `sense_group`, `no_oewn` or `unmapped`, every sense of that lemma and
part of speech (OEWN and supplementary) is in exactly one class (`validate-data` checks it, and that a sense is in at
most one class). Of the 80 `unmapped` buckets, 72 hold the OEWN senses of a part of speech of a mapped lemma that has no
coarse class at all (for example `advance` as an adjective), and 8 hold senses that a class of the same part of speech
left over.

## Decisions on the reviewed proposal

- `second#second-transfer` (a verb, "secondment") owns `oewn-second__2.41.01..`, which the proposal had put in two
  classes; `second#second-support` keeps `oewn-second__2.41.00..`.
- `slough#slough-swamp` also owns `oewn-slough__1.17.02..` (a hollow filled with mud), because the other senses of the
  same pronunciation entry are in that class.
- A `no_oewn` class gets one supplementary entry per lemma and part of speech (`dema-<lemma>-<pos>`), one synset and one
  sense per class, no sense key, and the coarse `meaning` of the corpus as its gloss. Where the corpus has no meaning,
  the gloss says so. Many meanings are only a pronunciation hint (for example `stress-last`); a real definition can
  replace them later.
- `unmapped` buckets are made only for lemmas that have a `sense_group` or `no_oewn` class; a lemma that is only a
  `lemma_split` or `feature_only` class has none.

## Wrong-lemma forms (to fix in the forms table of the next release)

The corpus lists 19 inflected forms under a lemma that cannot produce them. They are not changed here (the forms table
and the corpus are outside this release); the right lemma, where one exists, is:

| Form | Listed under | Lemma to use |
| --- | --- | --- |
| `conjuncts` | `conjunct` | none derivable from OEWN |
| `pustulates` | `pustulate` | none derivable from OEWN |
| `re-signs`, `re-signed`, `re-sign`, `re-signing` | `resign` | none derivable from OEWN (meaning "sign again") |
| `wicks`, `wick`, `wicking` | `wicked` | `wick` (found in OEWN) |
| `tiered` | `tier` | none derivable from OEWN (the verb is missing) |
| `supposes`, `supposing` | `suppose` | `suppose` (found in OEWN) |
| `blesses`, `blessing` | `bless` | `bless` (found in OEWN) |
| `exited`, `exiting` | `exit` | none derivable from OEWN (the verb is missing) |
| `ironed`, `ironing` | `iron` | none derivable from OEWN |
| `stricken` | `strike` | none derivable from OEWN |

## Supplementary entries

The 120 `dema` entries are MIT licensed (source `dema`). 57 are lemmas that OEWN does not have (mostly derivations with
`re-`, `over-` and `under-`) and 63 are a part of speech that OEWN does not have for the lemma.
