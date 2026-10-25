# Lexicon Provenance

## Source

- **Work**: Moby Part-of-Speech II (`mobypos.txt`)
- **Author**: Grady Ward
- **Licence**: public domain (dedicated by the author)
- **URL**: <https://www.gutenberg.org/ebooks/3203>
- **Input file**: `mobypos.txt`, 3004202 bytes (line feeds; the SHA-256 is taken over that form, so a copy with
  CRLF line ends passes too)
- **Input SHA-256**: `AB516C4918E017D7771DA70766EFB95C6C9B81DCECC97395FE0B61EE5373582D`
- **Not committed**: the raw list; only the derived `lexicon.tsv` is committed
- **Not used**: no data from ASD-STE100 is used or embedded

## Command

`lexicon.tsv` is regenerated from the pinned Moby file by the `import-moby` command of `WordSense.Builder`. The
command refuses a file with another SHA-256 and is not part of the build, because the raw file is not committed:

```text
dotnet run --project tools/WordSense.Builder -c Release -- import-moby --input mobypos.txt
```

The importer reads `corrections.tsv` from the directory of the output (`corpus/lexicon` by default). The `words`
command of the builder reads the committed `lexicon.tsv`; `classes.tsv` and `morphology.tsv` record the class and
rule decisions that the `words` command applies.

## Import Rules

1. The input is read as Latin-1; entries are separated by line feeds and read `word\codes`. A word that is on several
   lines has its codes joined in file order.
2. Only single lower-case words of the letters `a-z` are kept (no spaces, hyphens, digits or capitals).
3. Codes map to parts of speech: `N`, `h` and `p` noun; `V`, `t` and `i` verb (`V` is the ordinary verb flag);
   `A` adjective; `v` adverb; `r` pronoun; `P` preposition; `C` conjunction; `!` interjection; `D`, `I` and `o`
   determiner. A word without a mappable code is dropped (none is).
4. Each part of speech is written once, in the order of its first code in the Moby entry: the first letter is the most
   frequent part of speech of the word, and it is the sense that a model without information answers.
5. A word that is only an adjective and ends in `-ed` or `-ing` is adjective and verb.
   An `-ing` word of five or more letters without a verb code gains the verb sense when its stem
   (plain, plus `e`, or without a doubled final consonant) has a verb code.
6. The corrections replace the set of a word.

No word is dropped because of its part of speech: determiners, conjunction-only words and interjection-only words
stay in the lexicon, and so do the words that a trained lemma or form spells. The precision gate of `classes.tsv` only
decides which parts of speech a model is trained or evaluated to answer.

The output letters are N noun, R pronoun, V verb, J adjective, B adverb, P preposition, C conjunction, I
interjection, D determiner and A auxiliary; their order within a word is the sense order. The dictionary itself has no
auxiliary letter: the Auxiliary sense comes from `auxiliaries.tsv`.

## Corrections

| Word | Set |
| --- | --- |
| comes | V |
| days | N |
| i | R |
| needs | NV |
| says | V |
| the | D |
| a | D |
| an | D |
| his | D |
| my | D |

The evidence of every correction is in `corrections.tsv`. Moby lists `the` and `his` as determiner and adverb or
pronoun, `a` as determiner, noun, verb and preposition, and `an` as determiner and conjunction; the Universal
Dependencies English-EWT treebank labels `the`, `a` and `an` determiner in over 99 percent of their uses. `his` and
`my` are the possessive determiners of the library.

## Class Gate

| Class | Mode |
| --- | --- |
| Noun | answer |
| Pronoun | answer |
| Verb | answer |
| Adjective | answer |
| Adverb | answer |
| Preposition | answer |
| Conjunction | candidate-only |
| Interjection | candidate-only |
| Determiner | candidate-only |

The mode is the outcome of a precision gate: a word that has only that one part of speech must be answered right at
least 95 percent of the time on running text. The gate was measured on the pooled train, dev and test parts of Universal
Dependencies English-EWT (evaluation only; nothing derived from it is committed), with NOUN as Noun, VERB and AUX as
Verb, ADP as Preposition, SCONJ accepted as Preposition or Conjunction, and PROPN, PUNCT, NUM, SYM, X, DET and PART not
scored. The precision of the direct answers was Noun 97.61, Pronoun 99.89, Verb 97.79, Adjective 97.26, Adverb 97.01 and
Preposition 99.86 percent; Conjunction (76.37) and Interjection (86.36) fail the gate. A `candidate-only` class is never
the target of a trained model, but every word of it stays in the lexicon, as the answer for a word that has only that
part of speech and as a candidate for a word that has others. The gate decides which part of speech a model may choose,
never whether a word exists (an earlier version dropped 280 of the 143,992 words for that reason, which was wrong).
Determiner is a part of speech of its own because `the`, `a` and `an` are the most frequent words of English and no
other value fits them.

## Morphology Rules

| Rule | State |
| --- | --- |
| Plural | enabled |
| ThirdPerson | enabled |
| PastTense | enabled |
| Gerund | enabled |
| Comparative | enabled |
| Superlative | enabled |
| Adverb | enabled |
| Irregular | enabled |
| Quality | enabled |
| Agent | enabled |
| Manner | enabled |

The evidence of every rule is in `morphology.tsv` and in the coverage and audit sections of `data/PROVENANCE.md` and the
design of the form generator.

## Inflection Data

- `irregular-forms.tsv`: hand-written closed list of irregular English forms, general knowledge, no third-party
  data. License: same as the repository. (A list of irregular forms derived from a treebank used to sit beside it as a
  test oracle; it was removed together with the treebank counts, because the treebank is licensed CC BY-SA 4.0 and
  nothing derived from it may stay in the repository.)
- `irregular-closed.tsv`, `contractions.tsv`, `form-overrides.tsv`: the sources of the form generator, hand-written
  from general knowledge.
- `forms-authored.tsv` and `forms-authored-provenance.tsv`: the inflected forms (regular and irregular) of the lemmas
  that the generator could not otherwise corroborate, written for this project and dedicated to the public domain
  (`CC0 1.0 (project-authored)`). One model (gpt-5.6-luna) wrote each form from the lemma, part of speech and
  category alone; a second model (gpt-6.1-sol) answered the same tasks blind; the sidecar records both models, the
  verdict (`agree`, `regular-accepted`, `adjudicated`) and the licence per row. They replace the third-party inflection
  database that earlier tables used (its derived list `agid-irregular.tsv` and its notice file are removed); the method,
  the independence measures and the audit are in `data/PROVENANCE.md` and `tools/forms-audit/README.md`.
- `form-corrections.tsv`: our corrections of generated analyses that the forms audit found wrong (typos of the OEWN
  forms, misspellings of a former source, wrong features), each with evidence (the text of older rows names the former
  third-party database; it is history, free text that no tool reads). LemmInflect, the dev-time
  checker of the audit, is not a source of it: the audit only flags rows, and nothing ships from the checker.
- `form-frozen.tsv`: the reviewed analyses of the tables that were bootstrapped from the legacy corpus builder, the
  explicit input of the form generator (`frozen` rows with their rank, entry, features, source, review flag and scope
  classes; `closed` rows that mark the closed-class spellings). It was derived once from the reviewed table of the
  commit before the generator existed and is edited only by hand; the generator never reads its own output, which is
  what makes `rebuild-forms` reproducible (see `data/PROVENANCE.md`). No licensed source other than the ones named there.
- `additions.tsv`: hand-curated words missing from the dictionary (days, months, nationalities, British spellings,
  `coverage` gaps, and the verb `torque` that technical instructions use, evidence `technical`; no STE data is used).
  License: same as the repository.
- `own-adjectives.tsv`: the participial and attributive adjectives (`damaged`, `drained`, `drilled`, `alloyed`, `advance`,
  `beat`, `chance`, `sign`, `stretch`) whose Open English WordNet adjective entry keeps its own analysis beside a reviewed
  verb analysis of the same spelling (read by `rebuild-forms`; the other 105 such entries are left out on purpose, see
  `docs/design/word-sense/word-data/form-generator.md`). License: same as the repository.
- `auxiliaries.tsv`: hand-written closed list of the English auxiliary verbs (modals and the forms of be, have and
  do), general knowledge, no licensed data. The `words` command appends an Auxiliary sense after the existing senses
  of each listed word (after the expansion, so no inflected form gains it). License: same as the repository.

## Closed-Only Flag of `form-frozen.tsv`

`form-frozen.tsv` has 801 `closed` rows for 743 spellings. 450 of the rows, for 423 spellings, carry the flag `only`;
the other 351 rows, for 320 spellings, do not. The flag is per spelling (no spelling mixes flagged and unflagged rows).

**Effect.** `FormGenerator.ReadLegacy` collects the spellings of the `closed` rows with `only` into `ClosedOnly`. The
`closed` rows are used in either case: a `closed` row is added unless the spelling already has an analysis of that part
of speech, in which case the existing analysis stands for it (the file header says the same). In the merge step of
`FormGenerator.Run`, a spelling in
`ClosedOnly` that has at least one closed-class analysis keeps only (a) its closed-class analyses (part of speech `u`,
`p`, `c`, `i`, `d` or `x`), (b) its `hand` and `irregular_list` analyses, (c) a `moby_reverse` analysis whose part of
speech the dictionary itself lists for the spelling and (d) its `llm` analyses; every other generated open-class
analysis of the spelling is suppressed. A spelling without the flag keeps its corroborated generated analyses of other
entries beside the closed-class one. Clearing `only` therefore allows open-class analyses (`does` as the plural of
`doe`, `many` as an adjective) and never removes an analysis; a spelling that has no closed-class analysis is not
affected by the flag.

**How it was derived, once.** Before the generator read `form-frozen.tsv`, the generator at commit `12a2108` (PR #9,
the first `FormGenerator.cs`) applied a rule to the old form table at run time: a spelling whose old analyses were all
closed-class (the entries of the old rows have part of speech `u`, `p`, `c`, `i`, `d` or `x`; a row whose entry is
unknown is ignored) "keeps no open-class analysis". Commit `61926eb` (PR #11) made the generator read the committed file
instead and wrote `form-frozen.tsv` in one step, with all 801 `closed` rows and their flags; the file has not changed in
its `closed` rows since (commit `8604bb1` only added the `or` frozen row and dropped one `supposed` row). The script
that wrote the file was never committed: the code of the rule exists only in `FormGenerator.cs` at `12a2108`
(`ranks.All(r => ClosedPos.Contains(r.Pos, ...))`, near the comment "A word that the old table held with closed-class
analyses only") and in the commit `61926eb` diff that replaced it with `ClosedOnly`.

**Reproduction (partial).** The exact table that the one-time step read was not committed, so the flags cannot be
reproduced exactly from history. Applying the rule to the form table of `12a2108` (the parent of `61926eb`) gives the
flag of most, but not all, of the 423 flagged spellings (two independent replays gave 397 and 415, depending on how the
table of that commit was regenerated), and no unflagged `closed` spelling is closed-class only there. The other 53
closed-class-only spellings (contractions such as `ain't` and `it's`) have no `closed` row, so they carry no flag. The
flags that the replays do not produce were set by the one-time step for reasons that are not recorded.

**Cleared by hand: none found.** No `closed` spelling is closed-class only in the old table and still unflagged. The 320
unflagged spellings are unflagged because the old table also gave each of them at least one open-class analysis (a
noun, verb, adjective or adverb; for example `does` and `has` as verbs, `bags` and `order` as nouns and verbs, `many`
and `other` as adjectives and nouns), not because someone cleared a flag. (`others` is not an example of the flag: it
has no `closed` row.) To list the analyses of a spelling, join the unflagged `closed` rows with `data/entry/*.tsv` and
`data/form/*.tsv` at `12a2108`.

**Flagged although the old table held a verb.** Among the flags the rule does not explain are `am`, `been`, `did`,
`had`, `is`, `was`, `were` and `would`: they carry `only` although the old table also held a verb analysis for each
(the auxiliary forms of `be`, `do`, `have` and `will`). Their verb analyses survive through the provenances that the
flag keeps (`hand`, `irregular_list` and listed `moby_reverse`, for example `am` and `would`), so the flag suppresses
only the extra generated analyses; `has` and `does`, the same kind of auxiliary forms, are unflagged. The commit
messages give no reason, so it is not a documented decision. Review them as data like any other flag.

**Changing it.** The rule is not re-run. A change of the flag is a reviewed edit of the `flags` column of
`form-frozen.tsv`, made directly in that file and reviewed as data; the generator reads the file as given.

## Counts

| Step | Words |
| --- | ---: |
| Entries in the input | 233356 |
| Single lower-case words | 143992 |
| Dropped: no mappable code | 0 |
| Participle and gerund words that gained the verb sense | 8533 |
| Words replaced by a correction | 10 |
| Words with the determiner sense | 103 |
| **Words in `lexicon.tsv`** | **143992** |
