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
does not have; they are described by the commits that add them.
