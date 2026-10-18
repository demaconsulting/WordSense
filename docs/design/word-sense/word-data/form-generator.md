### FormGenerator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`FormGenerator` writes the `form` and `form_sense_scope` tables, the inflected spellings of the dictionary entries and
their analyses (lemma, part of speech, features, provenance), and a statistics report. `InflectionRules` holds the
spelling rules; `AgidImporter` and `IrregularSources` read the closed lists and the inflection database.

#### Data Model

Sources, each recorded as the provenance of a row:

- curated: the Open English WordNet forms (`oewn_form`), the closed and hand lists (`irregular_list`, `hand`,
  `contraction`), and rows carried from earlier tables (`carried`);
- generated: regular rules (`rule`, `rule_variant`), the inflection database of the dictionary words (`agid`) and the
  reversed Moby word list (`moby_reverse`).

A generated row is written only when it is corroborated: a regular -s, -ed or -ing form of a real lemma passes on rule
and base alone; any other row needs a second source (the word list that lists the spelling, or the inflection database
that attests it for that lemma and part of speech). Everything else is dropped and counted.

Frozen rows (carried from earlier tables) are kept only when the rules can still derive them; a frozen spelling does not
block the analyses of other entries. A gerund of a verb leads with the verb analysis; a gerund noun that a sense label
needs follows it.

#### Key Methods

- `Run(data, corpus, oewn, overwrite, agid)`: reads the entries and sources, generates, prunes and writes the tables;
  returns the statistics.
- `Corroborated` / `Prune`: the two-source rule.
- `ListRank`: the order of the analyses of a spelling.

#### Error Handling

Missing inputs or existing tables without `overwrite` throw before any file is written.

#### Dependencies

`DataSchema`, `DataLayout`, `InflectionRules`, `AgidImporter`, `IrregularSources`.

#### Callers

The `rebuild-forms` command.
