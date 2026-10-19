### OewnImporter Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`OewnImporter` creates the tables once from the pinned Open English WordNet 2025 LMF file. After the import the tables
are edited by hand; the importer never overwrites them unless told to. It also imports the pinned WordNet 3.0 sense
frequency counts into the `sense_frequency` table.

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

Sense frequency counts: the pinned WordNet 3.0 `index.sense` file has one line per sense key (`sense_key synset_offset
sense_number tag_count`). The importer joins the lines to the `sense` table on the sense key and writes one
`sense_frequency` row for every sense that has a key: the tag count of its line, or 0 when the file has no line for the
key; the source column is `wn30`. A line whose key is not a sense of the data is counted and reported, not written. The
counts are used under the WordNet license notice, which `data/PROVENANCE.md`, the data README and the README repeat.

#### Key Methods

- `Import(input, output, schemaPath, expectedSha256, splitThreshold, overwrite)`: checks the SHA-256 against the pin,
  parses, derives, chooses the split plan, writes the tables in sorted order and updates the file split line.
  Returns an `ImportResult` with the counts.
- `ImportCounts(input, data, expectedSha256)`: checks the SHA-256 of the counts file against the pin before reading it,
  reads the `sense` table of `data`, joins the counts on the sense key and writes `sense_frequency` in sorted order.
  Returns the number of senses with a count, with count 0 and the number of unmatched lines.

#### Error Handling

A wrong hash, malformed XML, no lexicon, an unknown relation type, a satellite without head, a missing referenced
synset, a previous release in which two current senses derive the same key, or existing tables without `overwrite`
throws before any file is written. `ImportCounts` throws in the same way for a wrong hash, and for a line that does not
have four fields or whose count is not a non-negative integer, so no table is written from a damaged file.

#### Dependencies

`DataSchema`, `DataLayout`, `System.Xml`.

#### Callers

The `import-oewn` and `import-wn30-counts` commands.
