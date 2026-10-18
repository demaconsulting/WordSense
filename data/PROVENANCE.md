# Provenance of the word data

## Source

- **Open English WordNet 2025 Edition** (OEWN), <https://github.com/globalwordnet/english-wordnet>, <https://en-word.net>.
- License: **Creative Commons Attribution 4.0 (CC BY 4.0)**. The tables derived from it keep that license; credit
  Open English WordNet when you redistribute them.
- File: `english-wordnet-2025.xml.gz` (the LMF file of the release; **not committed**), SHA-256
  `9CA6D1DCB75F822FDD66617F7D9DA48142ACE38DD544D6AD5E2FECA1674AD3FE`, checked by `import-oewn` against the pin
  `OewnImporter.PinnedSha256`.
- Imported with the `import-oewn` command of `tools/WordSense.Builder` (split threshold 8000 rows per first letter).

## Imported

Every lexical entry, sense, synset, definition, example, synset relation, sense relation and pronunciation of the file:
135,969 entries, 185,129 senses, 107,519 synsets, 107,524 glosses, 49,596 examples, 117,405 synset relations,
72,290 sense relations and 43,534 pronunciations (the first stage of this data; see the commits for later additions).

## Derived columns

- `entry.entry_id` is the OEWN entry id; `homograph_no` is 0 when the lemma and part of speech is unique, else 1..n by
  the numeric tail of the id; the part of speech `s` of a satellite entry is stored as `a`.
- `entry.token_count` and `head_idx` are a heuristic (first token of a verb; token before the first preposition of a
  noun; last token otherwise) and can be corrected by hand.
- `synset.head_synset_id` comes from the `similar` relation of a satellite synset; `sense.sense_key` from the sense id.
- Relations: the hyponym-type direction rows of OEWN (`hyponym`, `holo_*`, `has_domain_*`, ...) are stored as their
  inverse and deduplicated; symmetric relations are stored once. The importer reported no missing inverse and no self
  relation for the 2025 file.
- Text: tab or line break in a cell is replaced by a space, text is NFC (11 cells were cleaned).

## Left out

Subcategorization frames, adjective position, syntactic behaviour, ILI definitions and links (`ili`, `ILIDefinition`),
`dc:source`, and the `Form` element. The id map is empty.

## Project additions

Rows with `source_id` `dema` (MIT, project-authored) are added to cover words and senses that Open English WordNet
does not have: 120 entries, each with one synset, sense and gloss. See [MAPPING.md](MAPPING.md) for how they and the
coarse classes were made.

## Forms (written forms and their analyses)

The `form` and `form_sense_scope` tables were bootstrapped once with `bootstrap-forms` from the word table that the
legacy corpus builder produced (`corpus/lexicon`, `corpus/forms`, `corpus/senses`, the Moby-derived dictionary and the
generated inflections): 338,070 spellings, 364,444 analyses (rows of source `dema`, MIT) that stand for the 364,454
answers of the legacy table (ten answers share a row through its scope), and 4,627 scope rows. The tables are
authoritative now: edit them by hand.

- A dictionary word that has no Open English WordNet entry for its lemma and part of speech gets a new entry of source
  `dema` and the id `dema-<lemma>-<pos>`, with no sense (167,583 entries). These entries exist only so that forms
  refer to a lemma; they are not WordNet data.
- `features` are inferred from the regular English endings (`Number=Plur`, `Person=3+Tense=Pres`, `VerbForm=Ger`,
  `Degree=Cmp`, ...) unless a class link states them; a row that is inferred, or that has no class, has
  `reviewed` 0. A row of a labeled class has `reviewed` 1. Irregular forms without a class can have empty features.
- The corpus listed 19 inflected forms under a lemma that cannot produce them (see MAPPING.md). `wick`, `wicks` and
  `wicking` now use a verb `wick`, and `re-sign`, `re-signs`, `re-signed` and `re-signing` a verb `re-sign` (new `dema`
  entries, `reviewed` 0, the class of the legacy answer kept). `supposes`, `supposing`, `blesses` and `blessing` use
  `suppose` and `bless` through the class link of their lemma-split class. For `conjuncts`, `pustulates`, `tiered`,
  `exited`, `exiting`, `ironed`, `ironing` and `stricken` the lemma of the legacy answer was already a valid lemma of
  an entry (OEWN or `dema`), so the analysis uses it unchanged.
- An `unmapped` class is never an answer. A lemma-split class with several linked entries of the right part of speech
  uses the entry whose lemma the class label names, else the alphabetically first lemma.
