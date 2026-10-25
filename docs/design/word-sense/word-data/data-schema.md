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
- `sense_group(group_id, lemma, pos, label, origin, kind)` and `group_sense(group_id, sense_id)`: the curated groups
  (origin `curated` only; kind `equivalent` or `related`, the `group_kind` vocabulary; the id is `lemma#group:label`,
  versioned per data release and not frozen) and the group of a sense (at most one; a sense needs none). The column
  `kind` is the last column, so a reader by column index of the earlier columns is not affected.
- `sense_frequency(sense_key, tag_count, source)`: the WordNet 3.0 tag count of a sense key; the source is `wn30`.

The tables of the retired class inventory (`coarse_class`, `class_sense`, `class_entry`) and of the per-form sense scope
(`form_sense_scope`), with their `class_kind` vocabulary and the `class` partition kind, are removed (schema version 3);
nothing in the schema refers to a class. Schema version 4 adds the `kind` column of `sense_group` with the
`group_kind` vocabulary and the group id scheme `lemma#group:label`.

#### Key Methods

- `Load(path)`: parses and checks the schema, the sense inventory tables and their vocabularies included; throws
  `InvalidDataException` with `path:line:` on a defect. The `schema_version` must be an integer and equal to the
  supported version (4); another value is rejected with the line of the `schema_version` key.
- Consistency checks: every key, foreign key, partition column, vocabulary and relation inverse refers to something that
  exists; a relation and its inverse store exactly one direction. A relation defect names the line of the relation
  type in the vocabulary, not line 0.
- `WriteFileSplit(path, letters)`: replaces only the `file_split:` line.

#### Error Handling

A missing file, a malformed line, an unknown column type, a dangling reference or an inconsistent relation pair throws
`InvalidDataException`; the validator turns it into a `schema` violation.

#### Dependencies

The .NET base class library.

#### Callers

`OewnImporter`, `DataValidator` and the builder commands.
