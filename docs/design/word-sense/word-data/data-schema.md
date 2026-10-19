### DataSchema Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataSchema` reads `data/schema.yaml`, the machine-readable description of the word data, so that the importer and the
validator work from one definition.

#### Data Model

- `DataSchema`: schema version, data release, file split letters, vocabularies, synset and sense relation types
  (inverse, symmetric, stored), the feature vocabulary and the tables.
- `Table`: name, columns (name, type, nullable), primary key, unique keys, foreign keys, partition column and sort.
- `SchemaYaml`: a reader for the restricted YAML subset the schema uses (block mappings and sequences, flow sequences of
  scalars, comments). Anything else is an error that names the line.

The committed schema declares, besides the tables imported from OEWN, the tables and vocabularies of the sense
inventory:

- `sense_label(sense_id, label, origin)`: the readable label of every sense, unique per lemma; the origin is
  `curated`, `generated` or `project`.
- `sense_label_map(old_id, new_id, release)`: the renames and retirements of ids; an old id is never reused.
- `sense_group(group_id, lemma, pos, label, origin)` and `group_sense(group_id, sense_id)`: the coarse groups and the
  one group of each sense.
- `sense_frequency(sense_key, tag_count, source)`: the WordNet 3.0 tag count of a sense key; the source is `wn30`.
- `sense_respelling(sense_id, respelling)`: the respelling of a sense.

The tables of the retired class inventory (`coarse_class`, `class_sense`, `class_entry`) and of the per-form sense scope
(`form_sense_scope`) stay in the schema until the word image is compiled from the new tables; they are not part of the
sense inventory.

#### Key Methods

- `Load(path)`: parses and checks the schema, the sense inventory tables and their vocabularies included; throws
  `InvalidDataException` with `path:line:` on a defect.
- Consistency checks: every key, foreign key, partition column, vocabulary and relation inverse refers to something that
  exists; a relation and its inverse store exactly one direction.
- `WriteFileSplit(path, letters)`: replaces only the `file_split:` line.

#### Error Handling

A missing file, a malformed line, an unknown column type, a dangling reference or an inconsistent relation pair throws
`InvalidDataException`; the validator turns it into a `schema` violation.

#### Dependencies

The .NET base class library.

#### Callers

`OewnImporter`, `DataValidator` and the builder commands.
