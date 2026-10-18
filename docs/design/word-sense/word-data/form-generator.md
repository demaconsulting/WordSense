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
block the analyses of other entries: the entry whose own lemma is an -ing spelling (the noun `tracing` beside the gerund
of `trace`) and the forms of the irregular lists (`lives` as the plural of `life`) are appended after the frozen
analyses. Other reviewed spellings stay as reviewed (`bowed` is only the bend verb).

Rank policy: the rank of an analysis is a frequency prior, not the answer for a sentence. A gerund of a verb leads with
the verb analysis because that is the most frequent use of an -ing form; a gerund noun that a sense label needs, the
adjective and the noun entry of the same spelling follow it. `walking` is a verb gerund, an adjective (a walking stick)
and a noun, and all analyses are present; choosing among them is the job of the tagger. The policy is tested
deliberately and separately from the linguistic facts (`Data_IngForm_LeadsWithVerb`), and the facts assert only that
the analyses exist.

#### Lexicon facts

The facts of the word table are checked from two files. `lexicon-facts.tsv` holds the hand-observed rows: words seen
failing in a book comparison, kept as regression guards, labeled `hand-observed`. Because those words were chosen by
failures, the second file `lexicon-facts-generated.tsv` holds source-derived rows that the `generate-facts` command
derives mechanically: every irregular past, participle, plural, comparative and superlative form of the closed and
hand-written lists whose lemma is an entry (`source-irregular-closed`, `source-irregular-hand`); the -s, -ed and -ing
forms of a sample of 100 regular verbs and the plural of a sample of 100 regular nouns (`source-regular-verb`,
`source-regular-noun`) with their possessives (`source-possessive`); every contraction of the closed list
(`source-contraction`); and a sample of 100 noun and 100 adjective entries spelled like an -ing form
(`source-ing-entry`). The samples rank each lemma by the SHA-256 hash of the seed and the lemma and take the lowest
hashes; the default seed is 20260101, so the file is repeatable (`generate-facts --output FILE`). A regular form that
a rule derives but the corroboration rule leaves out is counted and reported, not asserted. The committed file has
1,446 rows (about 79 KB). Every -ing noun and adjective entry of the dictionary (2,891 checked) is verified, not only
the sample, and the command fails when one lacks its own analysis.

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

The `rebuild-forms` command; `FactGenerator` (the `generate-facts` command) reads the same sources and the shipped
word table.
