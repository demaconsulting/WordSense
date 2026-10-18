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

#### Key Methods

- `Load(path)`: parses and checks the schema; throws `InvalidDataException` with `path:line:` on a defect.
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
