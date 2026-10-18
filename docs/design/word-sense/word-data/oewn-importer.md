### OewnImporter Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`OewnImporter` creates the tables once from the pinned Open English WordNet 2025 LMF file. After the import the tables
are edited by hand; the importer never overwrites them unless told to.

#### Data Model

The importer streams the XML (DTD ignored, no resolver; plain or gzip input) and writes the tables `source`, `entry`,
`synset`, `gloss`, `example`, `sense`, `synset_relation`, `sense_relation`, `pronunciation` and `id_map`.

Derived columns: `entry.homograph_no` (0 when the lemma and part of speech is unique, else 1..n by the numeric tail of
the OEWN entry id); entry part of speech `s` becomes `a`; `synset.head_synset_id` from the `similar` relation of a
satellite synset; `sense.sense_key` from the sense id tail; `gloss.seq`; `entry.token_count` and `head_idx` (a
heuristic: the first token of a verb, the token before the first preposition of a noun, else the last token).
Hyponym-type relations are stored as their inverse (the one direction of the schema) and deduplicated; symmetric
relations are stored once with the smaller identifier first. Text is cleaned (tab and line break to a space, NFC) and
the number of cleaned cells is reported.

Left out and documented in `data/PROVENANCE.md`: subcategorization, adjective position, syntactic behaviour, ILI
definitions and links, `dc:source` and `Form`.

#### Key Methods

- `Import(input, output, schemaPath, expectedSha256, splitThreshold, overwrite)`: checks the SHA-256 against the pin,
  parses, derives, chooses the split plan, writes the tables in sorted order and updates the file split line.
  Returns an `ImportResult` with the counts.

#### Error Handling

A wrong hash, malformed XML, no lexicon, an unknown relation type, a satellite without head, a missing referenced
synset or existing tables without `overwrite` throws before any file is written.

#### Dependencies

`DataSchema`, `DataLayout`, `System.Xml`.

#### Callers

The `import-oewn` command.
