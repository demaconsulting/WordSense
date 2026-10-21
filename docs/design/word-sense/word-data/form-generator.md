### FormGenerator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`FormGenerator` writes the `entry` and `form` tables, the inflected spellings of the dictionary entries and their analyses
(lemma, part of speech, features, provenance), and a statistics report. The candidate senses of a word come from the
entries of its analyses, so the tables carry no sense scope (the `form_sense_scope` table and the class tables that fed
it are retired, and the generator reads and writes neither). `InflectionRules` holds the
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

Frozen rows are the reviewed analyses of earlier tables. They are a committed explicit input, `corpus/lexicon/form-frozen.tsv`,
and the generator never reads its own output: the tables are a function of the entry tables, that file, the lexicon
lists, the corrections file and the two pinned external sources. A frozen spelling with the flag `open` takes the
generated analyses of other entries after the frozen ones; a spelling without it stays as reviewed (`bowed` is only the
bend verb); a closed-class spelling with the flag `only` keeps only its closed-class analyses. Frozen rows count as
`carried` in the provenance statistics. The class column of a frozen row (the class ids of the retired inventory) is not
read. A closed word that the dictionary lists only as an open-class word is given its analysis by a frozen row of its
project entry (`or` has the conjunction `dema-or-c` first, followed by the noun and adjective analyses of the
dictionary).

An -ing noun does not get a plural spelled like its singular from the inflection database (the mass nouns `bowling`,
`catling`, `feeling`); the plural of the rules is generated instead. The invariant names (`herring`, `offspring`, `ling`,
`whiting`) keep theirs.

A spelling that an earlier reviewed analysis carries (`damaged` as the past participle of `damage`) keeps the reviewed
analyses exactly, with two kinds of own entry added after them: an -ing entry, and an adjective entry whose lemma is the
spelling when `corpus/lexicon/own-adjectives.tsv` names it (`damaged` as the adjective of `a damaged seal`, `drained`,
`drilled`, `alloyed`). The list is short on purpose: Open English WordNet has 114 such participial adjective entries
whose analysis a reviewed verb row had hidden, but giving all of them an analysis made the common participles (`based`,
`covered`, `changed`, `driven`) compete with the verb in the real-text evaluation (the fine top-1 fell about two points,
because the corpus labels those forms with the verb senses only), so only the adjectives that technical text uses are
listed. Verbs that the dictionary lacks come from `corpus/lexicon/additions.tsv` (`torque` is an added verb: `torque`,
`torques`, `torqued`, `torquing` are generated like those of any verb, as surface-only analyses).

Rank policy: the rank of an analysis is a frequency prior, not the answer for a sentence. The order is deterministic: the
position of the part of speech in the Moby letters of the lemma, then explicit tie-breaks on lemma, entry and features,
so no rank is taken from an earlier table. A gerund of a verb leads with
the verb analysis because that is the most frequent use of an -ing form; the adjective and the noun entry of the same
spelling follow it. `walking` is a verb gerund, an adjective (a walking stick)
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

- `Run(data, corpus, oewn, agid, output, oewnSha256, agidSha256)`: reads the entries, the frozen input and the sources,
  generates, prunes and writes the tables to `output` (the data directory by default); returns the statistics.
- `Corroborated` / `Prune`: the two-source rule.
- `ListRank`: the order of the analyses of a spelling.

#### Error Handling

A missing input, or an Open English WordNet or inflection-database file whose SHA-256 differs from its pin, throws
before any file is written.

#### Dependencies

`DataSchema`, `DataLayout`, `InflectionRules`, `AgidImporter`, `IrregularSources`.

#### Callers

The `rebuild-forms` command; `FactGenerator` (the `generate-facts` command) reads the same sources and the shipped
word table.
