### DataValidator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataValidator` checks the committed tables so that hand edits keep the data consistent.

#### Data Model

`ValidationReport`: the violations listed (file, line, rule, message), the total number of violations, the row count
of every table and `IsValid`. Rule tags: schema, file-set, header, cell-count, format, cell, sort, empty-file,
placement, unique, foreign-key, relation, entry, synset, sense, example, partition, label, stability, group,
legacy-map. At most 20 violations are listed per rule; the total is still counted.

#### Key Methods

- `Validate(dir, baseline)`: loads the schema, checks the file set (a missing folder of a partitioned table counts as an
  empty table), then each file (format and cells), then the keys, foreign keys, sort order and placement, the relation
  conventions and the cross-table rules (satellite heads, an entry and a synset each have a sense and a gloss, sense
  positions 1..n, part-of-speech fit, token counts and head indexes, and examples naming senses of their own synset),
  and then the rules of the sense inventory. The baseline is the data folder of the last release (optional).
- Sense rules (`label`, `stability`, `sense`): every sense has exactly one label; the labels of the senses of a
  lemma are unique and made of lower-case ASCII letters, digits and hyphens; no sense id equals the id of a group (the
  two share one namespace); a label that was published is neither changed nor reused for another sense unless
  `sense_label_map` records the change; a project sense has no sense key and an authored gloss. The published labels are
  those of the `sense_label` table of the baseline, which the check compares with the current table (without a baseline
  only the retired ids are checked); an id that `sense_label_map` retires must not appear as a current id.
- Group rules (`group`, `partition`, `unique`): the senses of a curated lemma are partitioned among its groups (every
  sense in exactly one group); each group has one lemma, one part of speech and no two respellings; a lemma that is not
  curated has exactly one default group for each of its parts of speech, labeled with the part of speech name and
  `-group`.
- Legacy map rules (`legacy-map`): every row names an existing group or sense of the stated level, and no old class id
  appears twice.

#### Error Handling

The validator reports instead of throwing: a schema that cannot load is one `schema` violation; an unreadable file is a
violation of that file; a baseline that cannot be read is a `stability` violation.

#### Dependencies

`DataSchema`, `DataLayout`.

#### Callers

The `validate-data` command and the test of the committed data.
