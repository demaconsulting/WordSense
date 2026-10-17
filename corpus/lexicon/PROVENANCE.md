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
interjection and D determiner; their order within a word is the sense order.

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

The evidence of every rule is in `morphology.tsv` and in `docs/build_notes/inflection-coverage.md`.

## Inflection Data

- `irregular-forms.tsv`: hand-written closed list of irregular English forms, general knowledge, no third-party
  data. License: same as the repository.
- `irregular-ud.tsv`: derived by `ud-irregular` from UD English-EWT train and development (pairs seen twice,
  dictionary-filtered; the test part is never read). License: CC BY-SA 4.0 (see `corpus/ud/PROVENANCE.md`).
- `additions.tsv`: hand-curated words missing from the dictionary (days, months, nationalities, British spellings,
  `coverage` gaps). License: same as the repository.

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
