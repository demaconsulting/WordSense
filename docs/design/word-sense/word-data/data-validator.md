### DataValidator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataValidator` checks the committed tables so that hand edits keep the data consistent.

#### Data Model

`ValidationReport`: the violations listed (file, line, rule, message), the total number of violations, the row count
of every table and `IsValid`. Rule tags: schema, file-set, header, cell-count, format, cell, sort, empty-file,
placement, unique, foreign-key, relation, entry, synset, sense, example, partition. At most 20 violations are listed
per rule; the total is still counted.

#### Key Methods

- `Validate(dir)`: loads the schema, checks the file set (a missing folder of a partitioned table counts as an empty
  table), then each file (format and cells), then the keys, foreign keys, sort order and placement, the relation
  conventions and the cross-table rules (satellite heads, an entry and a synset each have a sense and a gloss, sense
  positions 1..n, part-of-speech fit, token counts and head indexes, examples naming senses of their own synset, and the
  partition rule of the classes).

#### Error Handling

The validator reports instead of throwing: a schema that cannot load is one `schema` violation; an unreadable file is a
violation of that file.

#### Dependencies

`DataSchema`, `DataLayout`.

#### Callers

The `validate-data` command and the test of the committed data.
