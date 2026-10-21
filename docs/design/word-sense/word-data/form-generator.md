### FormGenerator Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`FormGenerator` writes the `entry` and `form` tables, the inflected spellings of the dictionary entries and their analyses
(lemma, part of speech, features, provenance), and a statistics report. The candidate senses of a word come from the
entries of its analyses, so the tables carry no sense scope (the `form_sense_scope` table and the class tables that fed
it are retired, and the generator reads and writes neither). `InflectionRules` holds the
spelling rules; `AuthoredForms`, `OpenCorpusAttestation` and `IrregularSources` read the closed and hand lists, the forms
written for the project and the tagged tokens of the open corpus.

#### Data Model

Sources, each recorded as the provenance of a row:

- curated: the Open English WordNet forms (`oewn_form`), the closed and hand lists (`irregular_list`, `hand`,
  `contraction`), and rows carried from earlier tables (`carried`);
- authored: the forms written for the project (`corpus/lexicon/forms-authored.tsv`, CC0 1.0): the primary forms are
  `llm`, the variants `rule_variant`; see `data/PROVENANCE.md` and `corpus/lexicon/PROVENANCE.md` for how they were
  written and verified;
- generated: regular rules (`rule`, `rule_variant`) and the reversed Moby word list (`moby_reverse`).

A generated row is written only when it is corroborated: a regular -s, -ed or -ing form of a real lemma passes on rule
and base alone; any other row needs a second source (the word list that lists the spelling, or the authored forms or the
tagged tokens of the open corpus that attest it for that lemma and part of speech). Everything else is dropped and counted.
The authored forms are authoritative like the hand and closed lists: a word with authored forms of a kind keeps the
regular spelling of that kind only when the authors name it, the reverse rules of the dictionary do not trace a spelling
to a lemma whose authored forms leave it out (`foots` is no plural of `foot`), and a decision of both authors that a form
does not exist (the sidecar `forms-authored-provenance.tsv`) stops the rule that would generate it. A lemma ending in
`-men` whose singular in `-man` is a word is a plural already and gets no plural by rule (`bagmen`).

The regular rules (`InflectionRules`) carry these decisions, each with a unit test; they came from comparing the rule
output with the authored forms and from the forms audit:

- The plural of a compound of man or woman is `-men` or `-women` (`icewomen`), except for the words that only end in
  `-man` (`human`, `german`, `roman`, `ottoman`, `talisman`, `shaman`).
- Consonant doubling treats `qu` as one consonant (`equipped`, `quipped`); a compound of a monosyllabic verb doubles
  (`wiretapped`, `suntanned`); the doubled alternative stays only for the listed verbs.
- A noun in `-quy` takes `-quies` (`obsequies`); a compound of a noun in `-o`, `-f` or `-fe` takes the plural of its
  last noun (`superheroes`, `werewolves`, `midwives`), with exceptions (`lowlife`, `behalf`, `handkerchief`).
- Of the verbs in `-ie` only `die`, `hie` and the verbs in `-tie`, `-lie` and `-vie` change to `-ying`; the others add
  `-ing` (`birdieing`, but `hogtie` gives `hogtying`).
- A spelling in `-s` of a verb is a third person, never a past (`chevies`, `gasses`, `quizzes`); a spelling of the base
  itself is not a past; a phrase whose head is an irregular verb has no regular past (`goed ahead`).
- The invariant plurals of the units `hertz`, `kilohertz`, `megahertz` and `gigahertz`, of `bourgeois` and of
  `offspring` are in `irregular-closed.tsv`.
- A verdict of both authors that a form does not exist (`NONE`) stops the regular rule for that lemma and kind. The
  authors judge rare regular forms of mass, rare or defective words invalid; no objective signal in the committed data
  (written forms, the Moby list, the open corpus) separates those forms from valid ones, so none is kept.

Forms that only an earlier third-party inflection database attested (plurals spelled like the singular such as `salmon`
or `trout`, and variants that the authors do not reproduce) are not in the table on purpose: adding them from the old
file would copy it back.

Coverage: expanding every dictionary word to its inflected forms raised the share of known running words in a sample of
86 Project Gutenberg texts (13.2 million tokens) from 92.1% to 95.9% (97.1% without the apostrophe tokens, which are
unknown by design). What remains unknown is names, fragments of hyphenated or line-broken words, French words, eye
dialect and misspellings. The rules deliberately generate no dialect forms (`knowed`), no archaic verb endings (`-est`,
`-eth`), no contractions outside the closed list and no derivations in `-ity`, `-ous`, `-ive`, `-ation`, `-ment` or
`-able`: the dictionary has the common ones, and a rule for each would add many non-words. The `coverage` command of the
builder repeats the measurement on a folder of text files.

Frozen rows are the reviewed analyses of earlier tables. They are a committed explicit input, `corpus/lexicon/form-frozen.tsv`,
and the generator never reads its own output: the tables are a function of the entry tables, that file, the lexicon
lists, the corrections file, the authored forms and the pinned Open English WordNet file. A frozen spelling with the
flag `open` takes the
generated analyses of other entries after the frozen ones; a spelling without it stays as reviewed (`bowed` is only the
bend verb); a closed-class spelling with the flag `only` keeps only its closed-class analyses. Frozen rows count as
`carried` in the provenance statistics. The class column of a frozen row (the class ids of the retired inventory) is not
read. A closed word that the dictionary lists only as an open-class word is given its analysis by a frozen row of its
project entry (`or` has the conjunction `dema-or-c` first, followed by the noun and adjective analyses of the
dictionary).

An -ing noun does not get a plural spelled like its singular (the mass nouns `bowling`,
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

- `Run(data, corpus, oewn, output, oewnSha256)`: reads the entries, the frozen input and the sources,
  generates, prunes and writes the tables to `output` (the data directory by default); returns the statistics.
- `Corroborated` / `Prune`: the two-source rule.
- `ListRank`: the order of the analyses of a spelling.

#### Error Handling

A missing input, or an Open English WordNet file whose SHA-256 differs from its pin, throws
before any file is written. A malformed authored file (a level that is not 0 or 1, an unknown rule) throws
`InvalidDataException` with the lemma. A reviewed input (`form-frozen.tsv`) with a row that has the wrong number of
columns, or
with two rows of one kind that repeat a spelling, an entry and features (the same analysis twice, whatever the other
columns say), throws `InvalidDataException` with the file and the line of the repeat and of the first row; the
generator does not dedupe silently, because a repeated analysis is an error of the reviewed input.

#### Dependencies

`DataSchema`, `DataLayout`, `InflectionRules`, `AuthoredForms`, `OpenCorpusAttestation`, `IrregularSources`.

#### Callers

The `rebuild-forms` command; `FactGenerator` (the `generate-facts` command) reads the same sources and the shipped
word table.
