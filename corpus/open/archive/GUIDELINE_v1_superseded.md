<!-- markdownlint-disable MD013 MD056 MD060 -->

# Annotation Guideline for the Open English POS Corpus

This guideline was written from scratch for this corpus. It does not copy or paraphrase any other treebank's
documentation. It uses the 17 UPOS tag names only as labels (the names are facts, not text); every definition, rule
and example below is our own. Where the guideline makes a choice between two defensible analyses, the choice is
stated as a rule so that every annotator (human or model) makes it the same way.

## 1. Task

You receive sentences that are already split into tokens. For every token give:

1. **UPOS**: exactly one tag from Section 3;
2. **lemma**: the dictionary form (Section 5);
3. **features**: a small set of morphological features (Section 6), or `_` when none apply.

Never merge, split, drop or re-order tokens. Tokens come from a fixed rule-based tokenizer, so you will see
split-off clitics such as `do` + `n't`, `ca` + `n't`, `she` + `'s`, and punctuation as separate tokens.

**Governing principle.** Tag the word by the job it does in this sentence, not by its spelling, its capitalization,
or the class it usually belongs to. Ask "what is this word doing here?" (naming something, describing a noun,
helping a verb, introducing a clause, ...). When two tags seem possible, apply the specific rules in Sections 3 and 4;
if still undecided, choose the tag that matches the word's function in the immediate phrase.

## 2. The tag set at a glance

`NOUN PROPN PRON VERB AUX ADJ ADV ADP CCONJ SCONJ INTJ DET PART NUM PUNCT SYM X`

## 3. Tag definitions

**NOUN** - a common noun: names a kind of thing, person, place, event, substance or idea (`dog`, `idea`, `water`,
`swimming` in "the swimming of the children", `km`). Includes mass nouns, noun-like abbreviations (`GPS`) and
nouns used as modifiers inside a noun phrase (`the *kitchen* table`).

**PROPN** - a name for one particular person, place, organization, brand, work, event, language, weekday, month or
holiday (`Maria`, `Ohio`, `NASA`, `Monday`, `June`, `English`, `Christmas`). Words that are part of a multiword name are
all PROPN, except function words inside it (`the` DET, `of` ADP): "Department of Energy" = PROPN ADP PROPN.
A title directly before a person's name is part of the name: `President Lincoln`, `Mr.` `Smith`, `Dr.` `Lee` are all PROPN.
**Capitalization alone never makes a word a PROPN** (see 4.9).

**PRON** - a word that stands in for a noun phrase instead of modifying a noun: personal (`I, me, you, he, she, it,
we, they`), reflexive (`myself`), possessive (`my, your, his, her, its, our, their, mine, yours, hers, ours, theirs`),
relative or question words that are the subject/object of their clause (`who, whom, whose, which, what`),
indefinite and negative compounds (`someone, anything, nobody, nothing, everyone`), a demonstrative or quantifier
that stands alone (`This is mine`, `Some were late`, `I want that`), generic `one` ("one should rest"), the
substitute `one` ("the red one"), existential `there` (4.7), and the `'s` in `let's`.

**VERB** - a word naming an action, event or state that serves as the main verb of its clause, plus infinitives,
gerunds and participles that behave verbally (Section 4.3). Main-verb uses of `be`-like linking verbs other than
`be` itself (`become, seem, remain, feel, look`) are VERB. Existential `be` is VERB (4.7). Full-verb `have` and `do`
are VERB (4.1).

**AUX** - a verb that only helps another verb or links a subject to its description: modals (`can, could, may,
might, must, shall, should, will, would, ought`), `be` as helper or as copula, `have` as perfect helper, `do` as
helper in questions, negation or emphasis (4.1, 4.2).

**ADJ** - a word that describes a noun or is the described state after a linking verb (`red`, `happy`, `the broken
window`, `interested`), including ordinal numbers (`first, 2nd`), comparative/superlative adjectives, and quantity
words used with nouns (`many, much, few, little, several, enough, more, most, less`, 4.8).

**ADV** - a word that modifies a verb, adjective, other adverb or whole clause: manner, degree, time, place,
frequency, focus (`quickly, very, too, only, always, here, there` (locative), `soon, ago, then, also, even`),
plus the question/relative adverbs `when, where, why, how` and their `-ever` forms in **every** use, including when they
open a subordinate clause ("*when* I arrived"). Also quantity words that modify a verb/adjective/adverb (4.8) and
place words used on their own without an object (`He went *outside*`, `Come *in*`).

**ADP** - a word that links a noun phrase to the rest of the sentence by showing a relation (`in, on, at, of, for,
from, with, by, about, through, during, to` before a noun phrase), plus the particle of a phrasal verb (4.6).
When a word like this is followed by a clause with its own verb, it is SCONJ instead (4.5).

**CCONJ** - a word that joins two equal-status units: `and, or, but, nor, yet` (joining), `plus` (joining), `&` meaning "and".
Correlative pairs (`both ... and`, `either ... or`): the joiners are CCONJ; `both`/`either` themselves are DET or
PRON by 4.8 if they have such a job, otherwise CCONJ.

**SCONJ** - a word that introduces a subordinate clause and makes it depend on another: `that` (4.4), `if,
because, although, though, while, whereas, unless, whether, as` (before a clause), `since, until, before, after,
so that, than` (before a clause), and prepositions followed by a verb-headed clause (4.5).

**INTJ** - a word or short phrase that works as an utterance by itself or a discourse marker: greetings, answers,
exclamations, polite formulas (`hello, hi, oh, wow, ouch, yes, no` (as an answer), `okay` (as an answer),
`please` (polite marker), `thanks` (as an expression), `sorry` (as an expression), `well` (as an opener)). See 4.10.

**DET** - a word that comes before a noun and specifies which or how much of it, without describing it: articles
(`a, an, the`), demonstratives before a noun (`this, that, these, those`), `some, any, no, every, each, all, both,
either, neither, another`, and question/relative words before a noun (`which book, what time, whose idea`).
Possessive words are **not** DET: they are PRON (see PRON).

**PART** - a small grammatical word that does not fit another class: the infinitive marker `to` before a verb
(4.5), the negators `not` and `n't`, and the possessive clitic `'s` (and a lone `'` after a plural possessive).

**NUM** - a cardinal number used to count or measure, in digits or words (`3, 1990, three, hundred, 10:30`, `3.5`), also
`million, billion` right after a number (`3 million`). Ordinals are ADJ (or ADV in "First, mix the flour"). `one` as a count is
NUM; `one` as a pronoun is PRON.

**PUNCT** - punctuation marks: `. , ; : ! ? " ' ( ) [ ] - -- ...` and `/` as a separator.

**SYM** - symbols that are not punctuation: `$ % + = < > @ #`, the copyright sign, currency signs, math signs,
emoticons, and `&` when it does not mean "and".

**X** - a token that cannot be analyzed as English words: foreign words in running English, typos that cannot be
repaired, garbled fragments, bare file names or URLs, single letters used as labels with no other class.

## 4. Conventions for frequent hard cases

### 4.1 Auxiliary or main verb: `be`, `have`, `do`, modals

Decide by whether the word helps another verb (or links to a description) or carries the meaning itself.

| Use | Tag | Example |
| --- | --- | --- |
| `be` as progressive or passive helper | AUX | "She *is* sleeping." "It *was* eaten." |
| `have` as perfect helper | AUX | "They *have* arrived." "I *had* known." |
| `do` as helper (questions, negation, emphasis) | AUX | "*Did* you see it?" "I *do* not know." "I *do* like it." |
| Modals | AUX | "You *must* go." "It *would* help." "She *'ll* come." (`'ll` = will) |
| `have` meaning own, hold, experience, eat, get | VERB | "She *has* a car." "We *had* lunch." "He *has* a feeling." |
| `have to` / `has to` (obligation) | VERB (`have`), `to` PART | "We *have* *to* leave." |
| `do` as full verb | VERB | "He *did* his homework." "What are you *doing*?" |
| `need` / `dare` without `to` and with a bare verb (rare modals) | AUX | "You *need* not worry." |
| `need` / `dare` with a noun or `to` | VERB | "I *need* help." "I *need* to go." |
| `be going to` + verb, `used to` + verb | `going`, `used` VERB; `to` PART | "It *is* *going* *to* rain." |

A helper keeps its AUX tag even when its verb is left out: "Yes, I *do*." "She can swim and he *can* too."

### 4.2 Copula

The verb `be` linking a subject to a noun, adjective, adverb or prepositional description is AUX in every form
(`am, is, are, was, were, be, been, being, 's, 're, 'm`): "The sky *is* blue." "He *was* a teacher." "It *'s* in the
box." "To *be* honest, ...". Other linking verbs are VERB: "She *became* a doctor." "It *seems* fine." "It *got* dark."

### 4.3 Participles and gerunds: VERB or ADJ or NOUN

Tag **VERB** when the form works as a verb. Test: it takes an object, an agent (`by ...`), an adverb of manner or a
time phrase; or it forms a tense or passive with an auxiliary; or it heads a participle clause.
"The vase was *broken* by the child." "The man *standing* there is my uncle." "*Having* finished, she left."
"A letter *written* in 1990."

Tag **ADJ** when the form just describes a property or state and could be modified by `very`, `so` or `quite`, or
compared: "a *tired* man", "very *interested*", "an *interesting* story", "the *broken* window", "a *growing* problem",
"*closed* doors" (state, no agent), "The shop is *closed*."

Gerunds (`-ing` forms in noun position) are **VERB** when they take their own object or adverb ("*Eating* apples is
healthy", "He likes *swimming* fast"), and **NOUN** when they behave like ordinary nouns: after an article, possessive or
plural, or followed by `of` ("the *building*", "his *drawings*", "the *signing* of the treaty").

### 4.4 `that`

- **SCONJ** when it introduces a clause: "I know *that* she left." "so *that* we can go."
- **PRON** when it is a relative word that is the subject/object of its clause ("the book *that* I read", "the
  dog *that* barked") or a demonstrative standing alone ("*That* is nice." "I like *that*.").
- **DET** when it directly precedes a noun: "*that* book".
- **ADV** when it means "so" before an adjective: "not *that* bad".

### 4.5 `to`, and prepositions before clauses

- `to` before a bare verb is **PART**: "I want *to* go." "in order *to* win." Elsewhere `to` is **ADP**: "went *to* the
  store", "from 9 *to* 5", and also before a gerund or noun phrase ("looking forward *to* seeing you").
- Any adposition-like word (`for, of, about, with, by, in, on, at, from, without, instead of`, ...) that is followed
  directly by a **clause or verb phrase** with its own verb is **SCONJ**; followed by a noun phrase it is **ADP**.
  "She is good *at* math" (ADP) / "She is good *at* solving puzzles" (SCONJ). "He apologized *for* the delay" (ADP) /
  "He apologized *for* being late" (SCONJ). "I thought *about* it" (ADP) / "I thought *about* leaving" (SCONJ).
  "*For* him to leave now would be rude" (SCONJ). "The idea *of* going" (SCONJ). "a cup *of* tea" (ADP).
- `before, after, since, until, till, while, as, like, than` followed by a clause are SCONJ; followed by a noun
  phrase they are ADP (`as`, `like`, `than`: ADP before a noun phrase); standing alone they are ADV: "I had seen her
  *before*." "*Before* he left, ..." (SCONJ) "*before* lunch" (ADP).
- `with` + noun phrase is ADP even when the noun phrase carries a participle: "*with* the door closed".

### 4.6 Particles: `up`, `out`, `off`, `on`, `in`, `down`, `away`, `over`, `back`

When the word completes a phrasal verb (the verb and the word form a unit whose meaning is not just the sum), it is **ADP**:
"give *up*", "turn *off* the light", "find *out*", "carry *on*", "pick it *up*", "set *out* to".
When it is a free direction or place adverb with no unit meaning, it is **ADV**: "She walked *out* slowly." "Come *in*." "The
plane flew *up*." When it takes a noun phrase it is ADP: "*up* the hill", "*out* of the house" (`out` ADP before `of`, and `of` ADP).

### 4.7 Existential `there`, locative `there`

- **PRON** when it only fills the subject slot of an existence statement: "*There* is a problem." "*There* were three cars."
  The verb `be` after existential `there` is **VERB**: "There *is* a problem." (VERB), "There *'s* hope" (`'s` VERB, lemma `be`).
- **ADV** when it means "at that place": "Put it *there*." "*There* it is." (the `is` there is AUX: locative copula).

### 4.8 Quantity words: `many, much, more, most, few, less, little, several, enough, some, any, all`

- Words of quantity that modify a noun or stand alone as its replacement are **ADJ**: `many` `much` `few` `little`
  `several` `enough` `more` `most` `less` (e.g. "*many* people", "*much* time", "*more* books", "*most* of them", "*a few* days"),
  and `such` ("*such* a day").
- When they modify a verb, adjective or adverb they are **ADV**: "*much* better", "*more* quickly", "the *most*
  beautiful", "I like it *more*", "*too* much" (`too` ADV, `much` ADV when it modifies a verb, ADJ before a noun).
- **DET** is reserved for: `a, an, the, this, that, these, those, some, any, no, every, each, all, both, either, neither,
  another` and `which/what/whose` before a noun. If one of these stands alone as a whole noun phrase ("Some were late",
  "I want some", "each of us", "all of them"), tag it **PRON** (PronType per Section 6).
- `no` as an answer is INTJ; `no` before a noun is DET.

### 4.9 Capitalized words that are not proper nouns

Capital letters mark sentence starts, headings, shouted words and emphasis. Ignore them. Tag as if the text were in
ordinary lowercase unless the word is part of a real name.
"*Keep* out" (VERB ADV, heading), "*Hidden* costs" (ADJ NOUN), "*Who* is there?" (PRON AUX ADV), "*A* tree fell" (DET at a sentence start).
A headline written in title-case is **not** a name, so its words are tagged by function. Only the title of a specific
work cited as a name (a book, film, song) is PROPN throughout. All-caps words are tagged by function ("STOP" VERB,
"NASA" PROPN). Common acronyms follow the class of what they stand for (`NASA` PROPN, `GPS` NOUN, `ATM` NOUN).
Days, months, holidays, languages and religions are PROPN. Nationality words are ADJ when they modify a noun ("the
*French* army") and PROPN when they name a language or people ("*French*", "the *Americans*").

### 4.10 Interjections and polite formulas

- `thanks` / `thank you` as a bare expression is INTJ ("*Thanks* for your help." "*Thank* you!" = INTJ PRON); as a noun
  ("many *thanks*") it is NOUN; as a clause with its own subject ("I *thank* you") `thank` is VERB.
- `please` as a politeness marker is INTJ ("*Please* sit down."); as a verb ("it *pleases* me") VERB.
- `sorry` is INTJ as a stand-alone apology ("*Sorry*!"), ADJ in "I am *sorry*".
- `yes`, `no` (answers), `hello`, `hi`, `goodbye`, `oh`, `ah`, `wow`, `well` (as an opener), `okay` (as an opener or reply) are INTJ;
  `okay` after a linking verb is ADJ ("That's *okay*"). `right` as a tag question or reply ("*Right*?") is INTJ.

### 4.11 Punctuation versus symbols

Sentence punctuation, quotes, brackets, dashes, hyphens standing alone, slashes and ellipses are **PUNCT**.
Symbols with meaning of their own (`$ % + = < > @ #` and emoticons) are **SYM**. `&` is CCONJ when it reads as
"and" ("Tom & Jerry"), SYM otherwise. A symbol inside a multiword token (`AT&T`) is part of that token.

### 4.12 Other short rules

- `not`, `n't`: PART (lemma `not`). `never`, `no longer`: ADV.
- Possessive `'s` and a plural possessive's lone `'`: PART (lemma `'s`). `'s` standing for `is` or `has` is AUX (or VERB when
  it is full-verb `has`, or existential `is`); `let's`: `'s` is PRON (lemma `we`).
- Possessive pronouns (`my` ... `theirs`) are PRON with `Poss=Yes`, not DET.
- `so` is ADV (including "so" before a clause meaning "therefore"), except in `so that` where it is SCONJ together with `that`; `as` before a clause is SCONJ.
- `all`, `just`, `only`, `even`, `also`, `too`, `very`, `quite`, `almost`, `really`: ADV (`all` before a noun is DET).
- `one`: count = NUM, replacement = PRON ("the blue *one*"), generic person = PRON ("*one* never knows").
- Fixed expressions: tag each word by its own function in the expression ("*in* *spite* *of*": ADP NOUN ADP;
  "*as* *well* *as*": ADV ADV SCONJ/CCONJ by whether a clause or phrase follows; use your best judgment).
- Contraction stems get the tag of the word they stand for: `ca` (can) AUX, `wo` (will) AUX, `sha` (shall) AUX, `ai` (be)
  AUX, `do` AUX or VERB, then `n't` PART; `gonna` VERB, `gotta` VERB, `wanna` VERB.
- Titles of address: `Mr.`, `Mrs.`, `Ms.`, `Dr.`, `St.` before a name are PROPN.
- A bare list label, URL, file name or code fragment is X (or SYM/PUNCT if that is what it is).

## 5. Lemma rules

- Use the lowercase dictionary form, except for PROPN lemmas, which keep their capitalization, and `I`, which stays `I`.
- Verbs: base form (`ran` -> `run`, `is` -> `be`, `n't` -> `not`). Nouns: singular (`mice` -> `mouse`). A noun that
  occurs only in the plural keeps its plural (`scissors`, `thanks` as NOUN -> `thanks`).
- Adjectives and adverbs in the comparative or superlative: the plain form (`better` ADJ -> `good`, `fastest` -> `fast`,
  `more` and `most` -> `more` and `most` unchanged when they are quantity words).
- Pronouns: personal pronouns map to their nominative form except that case forms keep their identity: `me` -> `I`, `him` -> `he`,
  `them` -> `they`, `us` -> `we`; possessive adjectives-like pronouns keep their own form (`my` -> `my`, `his` -> `his`);
  reflexives keep their form (`myself`).
- Contraction pieces: `'s` (is) -> `be`, `'s` (has) -> `have`, `'s` possessive -> `'s`, `'re`/`'m` -> `be`, `'ve` -> `have`, `'ll` -> `will`,
  `'d` -> `would` or `have` (`had`) by sense, `n't` -> `not`, `ca` -> `can`, `wo` -> `will`, `sha` -> `shall`, `ai` -> `be`.
- Numbers keep their form (`1990`, `three`); punctuation and symbols: lemma = form.
- X tokens: lemma = the form in lowercase.

## 6. Morphological features

Write features as `Name=Value` pairs joined by `|` in alphabetical order of the name, or `_` when none apply. **Do not
invent other features.** When a feature does not clearly apply, leave it out.

| Feature | Meaning (our words) | Values | Used on |
| --- | --- | --- | --- |
| `Number` | grammatical number of the form | `Sing` one / mass, `Plur` more than one | NOUN, PROPN, PRON (`I` Sing, `we` Plur), DET (`this` Sing, `these` Plur), finite VERB/AUX when the subject number is clear |
| `Person` | who the word refers to or agrees with | `1`, `2`, `3` | PRON (`I`/`we` 1, `you` 2, `he`/`she`/`it`/`they` 3), finite VERB/AUX when the subject is clear |
| `Tense` | time of a verb form | `Pres`, `Past` | finite VERB/AUX; participles (`Past` for `-ed`/`-en` forms, `Pres` for `-ing` participles) |
| `VerbForm` | shape of the verb | `Fin` finite (has a subject, shows tense or mood), `Inf` bare infinitive, `Part` participle, `Ger` gerund | VERB, AUX |
| `Mood` | speaker attitude of a finite verb | `Ind` statement or question, `Imp` command, `Sub` subjunctive (`if I *were*`) | finite VERB/AUX; leave out on modals |
| `Degree` | comparison of a gradable word | `Cmp` comparative (`taller`, `more`), `Sup` superlative (`tallest`, `most`) | ADJ, ADV (plain forms get no feature) |
| `PronType` | kind of pronoun-like word | `Prs` personal/possessive/reflexive, `Dem` demonstrative, `Int` question word, `Rel` relative word, `Art` article, `Ind` indefinite, `Neg` negative, `Tot` total/each | PRON, DET, and ADV (`when, where, why, how`: `Int` in questions, `Rel` otherwise) |
| `Poss` | possessive pronoun | `Yes` | PRON (`my, your, his, her, its, our, their, mine, yours, hers, ours, theirs, whose`) |

Allowed per UPOS (anything else is deleted by the validator): NOUN/PROPN `Number`; PRON `Number Person PronType Poss`;
DET `Number PronType`; VERB/AUX `VerbForm Tense Person Number Mood`; ADJ `Degree`; ADV `Degree PronType`. The other tags take no features.

Verb examples: `walks` -> `Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin`; `walked` (finite) ->
`Mood=Ind|Tense=Past|VerbForm=Fin`; `to walk` -> `walk` `VerbForm=Inf`; `walking` (participle) -> `Tense=Pres|VerbForm=Part`;
`walking` (gerund) -> `VerbForm=Ger`; `broken` (verbal) -> `Tense=Past|VerbForm=Part`; "Open the door." -> `Open`
`Mood=Imp|VerbForm=Fin`; `would` -> `VerbForm=Fin`.

## 7. Mapping to the WordSense collapsed tag set

WordSense works with a collapsed 12-tag set. Each corpus line carries the UPOS and the collapsed tag.

| UPOS | Collapsed | Name |
| --- | --- | --- |
| NOUN, PROPN | `N` | noun |
| PRON | `R` | pronoun |
| VERB | `V` | verb |
| ADJ | `J` | adjective |
| ADV | `B` | adverb |
| ADP | `P` | adposition |
| CCONJ, SCONJ | `C` | conjunction |
| INTJ | `I` | interjection |
| DET | `D` | determiner |
| AUX | `A` | auxiliary |
| PART | `T` | particle |
| NUM | `U` | number |
| PUNCT, SYM, X | `O` | other |

## 8. Output format (primary annotator)

One line per token, tab separated: `sent_id`, `idx`, `form`, `lemma`, `UPOS`, `feats`. `idx` counts from 0, `form` repeats the
input token exactly. Example:

```text
S1 0 She she PRON Number=Sing|Person=3|PronType=Prs
S1 1 has have VERB Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin
S1 2 a a DET Number=Sing|PronType=Art
S1 3 car car NOUN Number=Sing
S1 4 . . PUNCT _
```
