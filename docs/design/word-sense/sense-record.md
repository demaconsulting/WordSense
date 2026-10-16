# SenseRecord Unit Design

## Overview

The SenseRecord unit holds the immutable values that describe an answer, the validation of sense definitions, the
abstention rule and the text splitter. It consists of `PartOfSpeech`, `Pronunciation`, `WordDetails`,
`WordAnalysis`, `AnswerBasis`, `SenseDefinition`, `AbstainRule` and `TextSplitter`.

## Data Model

- `PartOfSpeech`: an enumeration of eight values, in numeric order: noun (0), pronoun (1), verb (2), adjective (3),
  adverb (4), preposition (5), conjunction (6), interjection (7). The numeric values are part of the table format
  (one byte per sense, one bit per part of speech in the dictionary), so they never change. Context answers are noun,
  verb, adjective or adverb. Dictionary answers can also be pronoun or preposition; conjunction and interjection
  appear only in the candidates of a word that has more than one part of speech. There is no determiner value:
  determiner words (such as `the`, `a`, `his`) are not in the dictionary and stay unknown.
- `Pronunciation(Meaning, Respelling?)`: a record. `Meaning` is a short lower-case identifier that names the
  meaning of a heteronym (for example `weapon` or `bend` for `bow`). `Respelling` is an optional plain respelling
  of the pronunciation in the library's own convention: syllables separated by hyphens, stress in capitals, no
  phonetic alphabet (for example `REK-urd` for the noun `record` and `rih-KORD` for the verb).
- `WordDetails(Lemma, PartOfSpeech, Pronunciation?)`: a record. `Lemma` is the dictionary base form of the word
  (`record` for `records`). `Pronunciation` is null for every lemma that is not a heteronym, so it is always null for a
  dictionary answer. One instance exists per sense of an entry instance, created when the owning `LemmaEntry` is
  constructed, so callers may compare answers by reference (context lemmas) or by value (always).
- `AnswerBasis`: an enumeration saying where an analysis came from: `None` (0, the word is unknown), `Context` (1, the
  encoder and the lemma's trained classifier chose the sense) and `Dictionary` (2, the table alone decided: the lemma
  has one sense, or has several senses and no classifier).
- `WordAnalysis(Word, Details?, Margin, Abstained, RunnerUp?, RunnerUpMargin, Basis, Candidates)`: a readonly record
  struct returned for each classified word. `Word` is the word as the caller gave it. `Details` is the best answer, or
  null when the word is unknown, the classifier abstained or a dictionary word has several senses. `Margin` is the
  score of the best candidate minus the score of the runner-up (a score difference, not a probability, comparable only
  within one lemma). `RunnerUp` is the second-best candidate and `RunnerUpMargin` its score minus the third-best score
  (0 for a two-sense lemma). `Abstained` is true when the word is known but has no single answer: the margin is below
  the lemma's threshold, or a dictionary word has several senses and no classifier. `Basis` tells which of the cases
  applies, and `Candidates` lists the possible answers of the word's lemma (never null from the library: empty for an
  unknown word, equal to the lemma's candidates otherwise; a word narrowed by morphology has the one derived sense).
  Field values per case:

  | Case | Basis | Details | Abstained | Margin, RunnerUp, RunnerUpMargin | Candidates |
  | --- | --- | --- | --- | --- | --- |
  | Unknown word | `None` | null | false | 0, null, 0 | empty |
  | One sense | `Dictionary` | the sense | false | 0, null, 0 | that sense |
  | Morphology-narrowed word | `Dictionary` | the derived sense | false | 0, null, 0 | that one derived sense |
  | Several senses, no classifier | `Dictionary` | null | true | 0, null, 0 | all senses |
  | Several senses, classifier answers | `Context` | best sense | false | reported | all senses |
  | Several senses, classifier abstains | `Context` | null | true | reported | all senses |

  The margin fields are meaningful only for `Context`; callers test `Basis` first. `default(WordAnalysis)` has a null
  `Candidates`, which the library never returns.
- `SenseDefinition(Pos, Meaning?, Respelling?)`: a record as stored in the table. The meaning is a lower-case
  identifier of at most `MaxMeaningLength` = 32 characters (a lower-case ASCII letter followed by lower-case
  ASCII letters, digits or `-`; a trailing newline is rejected). The respelling uses ASCII letters, hyphens and
  apostrophes, at most `MaxRespellingLength` = 32 characters, and needs a meaning. An empty string is not valid;
  null means absent. `Validate()` throws `ArgumentException` for an undefined part of speech, an invalid meaning or
  respelling, or a respelling without a meaning.

## Methods

- `LemmaEntry` set validation (in the table unit, using `SenseDefinition`): the senses of a lemma either all carry a
  meaning or none does; a lemma with meanings (a heteronym) needs at least two distinct meanings; a
  (part of speech, meaning) pair is unique; the same meaning always has the same respelling; distinct meanings
  have distinct respellings. A lemma without meanings has no pronunciation on any answer.
- `AbstainRule.Abstains(margin, threshold)`: the single abstention predicate shared by every classification path;
  it is `!(margin >= threshold)`, so a NaN margin or a NaN threshold abstains.
- `TextSplitter.SplitSegments(text)`: words, whitespace runs and single other characters, tiling the
  text. A word is a run of letters, digits and combining marks (non-spacing and spacing), and may contain
  an internal apostrophe (`'` or U+2019) or hyphen that is directly followed by a word character, so
  `don't`, `well-known` and `x1` are each one word; a trailing apostrophe or hyphen is its own tile.
  Surrogate pairs are kept together as one character. `SplitWords` returns the non-space tiles.

## Error Handling

- `SenseDefinition.Validate` throws `ArgumentException` for an invalid definition.
- `AbstainRule` and `TextSplitter` do not throw for well-formed input; an abstention is an analysis without
  details, not an error.

## Interactions

Used by `WordSenseClassifier`, `WordSenseTable` and `LemmaEntry` (the answer types and `SenseDefinition`); has no
dependencies on other units. `AbstainRule` and `TextSplitter` are internal helpers used by `WordSenseClassifier`
and by `WordSenseBuilder` (the builder reaches them through the assembly's internals-visible declaration). The
`WordSenseToken` records that the classifier returns are built from the `TextSplitter` segments; the type belongs
to the classifier unit.
