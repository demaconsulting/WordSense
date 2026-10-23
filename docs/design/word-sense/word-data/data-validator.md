### DataValidator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataValidator` checks the committed tables so that hand edits keep the data consistent.

#### Data Model

`ValidationReport`: the violations listed (file, line, rule, message), the total number of violations, the row count
of every table and `IsValid`. Rule tags: schema, file-set, header, cell-count, format, unreadable, cell, sort, empty-file,
placement, unique, foreign-key, relation, entry, synset, sense, example, label, stability, group. The emission is
capped: at most 20 violations are listed per rule (`MaxPerRule`), in discovery order, while the total counts every
violation, listed or not, so `IsValid` is false exactly when the total is above zero. A defect is therefore always
counted once, and listed unless its rule already listed 20.

#### Key Methods

- `Validate(dir, baseline)`: loads the schema, checks the file set (a missing folder of a partitioned table counts as an
  empty table), then each file (format and cells), then the keys, foreign keys, sort order and placement, the relation
  conventions and the cross-table rules (satellite heads, an entry and a synset each have a sense and a gloss, sense
  positions 1..n, part-of-speech fit, token counts and head indexes, and examples naming senses of their own synset),
  and then the rules of the sense inventory. The baseline is the data folder of the last release (optional).
- Part-of-speech fit (`sense`): the part of speech of a sense is the one of its entry, and it must fit the part of speech
  of its synset in Open English WordNet: equal, except that an adjective entry fits an adjective (`a`) or an adjective
  satellite (`s`) synset. The ss_type digit of the sense key (`lemma%ss_type:...`: 1 noun, 2 verb, 3 adjective, 4 adverb,
  5 adjective satellite) must name the part of speech of the synset of the sense, and a key without a digit from 1 to 5
  is reported; a project sense has no key and nothing to compare.
- Sense rules (`label`, `stability`, `sense`): a data folder with no label, group or id map yet (a fresh import) has no
  inventory, and the rules below are not applied to it. Every sense of a lemma of one token, and every sense without a
  sense key, has exactly one label; the labels of the senses of a
  lemma are unique and made of lower-case ASCII letters, digits and hyphens; no sense id equals the id of a group (a
  defensive check: the marker of a group id, below, is not a label character, so only a malformed label can collide); a
  label that was published is neither changed nor reused for another sense unless
  `sense_label_map` records the change; a project sense (label origin `project`) has no sense key and an authored gloss,
  and a sense without a sense key has the origin `project`. The published labels are
  those of the `sense_label` table of the baseline, which the check compares with the current table (without a baseline
  only the retired ids are checked); an id that `sense_label_map` retires must not appear as a current id. Only sense
  labels are compared: group ids are versioned per data release and are not frozen, so the baseline check never reads
  or compares a group, and `sense_label_map` is about senses only.
- Group rules (`group`, `unique`, `cell`): the id of a group is `lemma#group:label`: the id must carry the marker
  `group:` after the `#`, its lemma part must be the lemma of the group, and the label is the slug after the marker
  (lower-case ASCII letters, digits and single hyphens), unique in its lemma; a group has at least two senses but not all
  senses of its lemma and part of speech (the part of speech is the next level, so such a group is redundant); each group
  has one lemma, one part of speech and no two respellings; a sense is in at most one group; the origin of a group is
  `curated` and its kind is `equivalent` or `related` (both required and checked against the schema vocabularies by the
  cell rule). A sense needs no group, so there is no partition rule. The class and scope rules of the retired inventory
  are gone; an entry without a sense is accepted only for a closed-class word or when a carried analysis refers to it.
  The admission evidence of a group is curation metadata, checked by the curation import and not by the validator.

#### Error Handling

The validator reports instead of throwing: a schema that cannot load (invalid data, an I/O failure or a denied access) is
one `schema` violation; a directory that cannot be listed (the data folder or the folder of a partitioned table; the
listing is read to the end inside the guard, so a failure while it is enumerated is caught as well; the listing itself,
not an existence probe, tells a missing folder, which is an empty table for a partitioned table, from a folder that
exists but is denied, which is a violation) is a `file-set`
violation that names the directory; a table file that cannot be read (an I/O failure or a denied access) is an
`unreadable` violation of that file, and in each case the validation of the rest of the data continues (the missing rows
of an unreadable file can cause follow-up reference violations); a baseline that cannot be read is a `stability`
violation; duplicated keys (two rows with one
group id, two senses with one label) are reported by the key rules, and the inventory rules keep checking with the first
row of the key instead of failing. A cell of the features column must list each feature name once (sorted, unique).

#### Dependencies

`DataSchema`, `DataLayout`.

#### Callers

The `validate-data` command and the test of the committed data.
