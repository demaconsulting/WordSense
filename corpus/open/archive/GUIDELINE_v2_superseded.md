<!-- markdownlint-disable MD013 MD056 MD060 -->

# Annotation Guideline for the Open English POS Corpus (version 2)

This guideline was written for this corpus and uses no other treebank's documentation. It uses the 17 UPOS tag names
only as labels (names are facts, not text); every definition, rule and example is our own.

**Provenance of the hard-case rules (version 2).** Version 1 of this guideline had a hard-case list that was partly chosen from
error categories observed in an earlier exploratory test against a public treebank. That list is superseded. Section 4 of this version
was rebuilt only from our own pilot data: the 732 tokens on which our two pilot annotators disagreed when they tagged our own
sentences with version 1 (see `guideline-derivation.md`). Section 4 is ordered by how often each kind of disagreement occurred.
Where the two analyses were both defensible the rule picks the traditional-grammar category and gives a simple test.

## 1. Task

You receive sentences that are already split into tokens. For every token give:

1. **UPOS**: exactly one tag from Section 3;
2. **lemma**: the dictionary form (Section 5);
3. **features**: a few morphological features (Section 6), or `_` when none apply.

Never merge, split, drop or re-order tokens. Tokens come from a fixed rule-based tokenizer, so you will see split-off
clitics such as `do` + `n't`, `ca` + `n't`, `she` + `'s`, and punctuation as separate tokens.

**Governing principle.** Tag a word by the job it does in this sentence, not by its spelling, its capitalization, or the class
it usually belongs to. Ask "what is this word doing here?". When two tags seem possible, apply the rules of Section 4;
if none applies, choose the tag that matches the word's function in its immediate phrase.

## 2. The tag set

`NOUN PROPN PRON VERB AUX ADJ ADV ADP CCONJ SCONJ INTJ DET PART NUM PUNCT SYM X`

## 3. Tag definitions

**NOUN** - a common noun: names a kind of thing, person, place, event, substance or idea (`dog`, `idea`, `water`, `km`).
Includes mass nouns, abbreviations for common things (`REM`, `GPS`) and nouns used as modifiers inside a noun phrase (`the *kitchen* table`).

**PROPN** - a name for one particular person, place, organization, brand, work, event, language, weekday, month or holiday
(`Maria`, `Ohio`, `NASA`, `Monday`, `June`, `English`). Every content word of a multiword name is PROPN (`South Korea`); function words
inside it keep their own tag (`Department of Energy` = PROPN ADP PROPN). A title right before a person's name is part of the name (`Dr. Lee`).

**PRON** - a word that stands in for a whole noun phrase: personal (`I, me, you, he, she, it, we, they`), reflexive (`myself`), independent possessive
(`mine, yours, hers, ours, theirs`), question or relative words used alone (`who, whom, which, what`), indefinite compounds (`someone, anything, nobody`),
quantity and demonstrative words that stand alone (4.3), `one` standing for a noun (4.12), existential `there` (4.18).

**VERB** - a word naming an action, event or state that serves as a main verb, plus infinitives, gerunds and participles that behave verbally (4.7, 4.10).

**AUX** - a form of `be` in any use, `have` or `do` when helping another verb, and the modals `can, could, may, might, must, shall, should, will, would, ought` (4.2).

**ADJ** - a word that describes a noun, or is the described state after a linking verb (`red`, `happy`); ordinal numbers (`first, 2nd`); comparatives and
superlatives of adjectives; quantity words before a noun (`many, much, few, little, several, enough, more, most, less, such, other`) (4.11).

**ADV** - a word that modifies a verb, adjective, other adverb or whole clause: manner, degree, time, place, frequency, focus (`quickly, very, too,
only, always, here, there` (locative), `soon, then, also`), place words with no noun phrase after them (4.5), and question words `why`, `how`.

**ADP** - a word that links a noun phrase (or gerund phrase) to the rest of the sentence by showing a relation (`in, on, at, of, for, from, with, by, about,
through, during, to` before a noun phrase) (4.1, 4.5, 4.8, 4.13).

**CCONJ** - a word that joins two equal-status units: `and, or, but, nor, yet`, `plus`, and `&` read as "and"; also the paired joiners `both ... and`, `either ... or`, `neither ... nor`.

**SCONJ** - a word that introduces a subordinate clause: `that` (4.9), `if, because, although, though, while, whereas, unless, whether`, and `than, as, like, before, after, since, until, when, where`
in the uses described in 4.1 and 4.4.

**INTJ** - an utterance by itself or a discourse marker: greetings, replies, exclamations, polite formulas (4.14).

**DET** - a word before a noun that specifies which or how much of it without describing it: articles (`a, an, the`), demonstratives before a noun (`this, that, these,
those`), `some, any, no, every, each, all, both, either, neither, another`, `which, what, whose` before a noun, and the possessive words before a noun
(`my, your, his, her, its, our, their`). Closed list (4.11).

**PART** - the infinitive marker `to` (4.6), the negators `not` and `n't`, and the possessive clitic `'s` or a lone `'` after a plural possessive (4.15).

**NUM** - a cardinal number used to count or measure, in digits or words (`3, 1990, three, hundred, 10:30`, `3.5`), also `million, billion` after a number. Ordinals are ADJ.

**PUNCT** - punctuation marks: `. , ; : ! ? " ( ) [ ] - --` and `...`, quotation marks, and `/` used as a separator.

**SYM** - symbols that are not punctuation: `$ % + = < > @ #`, currency signs, math signs, emoticons.

**X** - a token that is not English or cannot be analyzed: words of a foreign-language phrase, fragments, URLs, e-mail addresses, file names (4.16).

## 4. Hard cases, in order of how often our pilot annotators disagreed

### 4.1 Preposition or subordinating conjunction (`than, as, of, for, about, like, after, by, from, before, with, in, at, without`) - 110 disagreements

- **SCONJ** when the word introduces a clause with its own subject: a finite verb follows the subject, or only an auxiliary of it is left over in a comparison.
  "He left *before* I arrived." "taller *than* I am", "more energy *than* they possess", "*as* he said", "*like* I said", "*after* he ate", "*since* they came", "*until* she calls".
- **ADP** when what follows is a noun phrase, pronoun, number, gerund phrase or adverbial phrase: "taller *than* him", "*as* a zombie", "*like* a bird", "*after* World War II",
  "*for* making dinner", "*of* smoking", "*about* leaving", "*by* stacking the deck", "*without* asking".
- With nothing after it, the word is ADV: "I had seen her *before*."
- `for` + noun phrase + `to` + verb ("*for* him to leave") is ADP.

### 4.2 `be`, `have`, `do` and modals: AUX or VERB - 77 disagreements

- **Every form of `be` is AUX** in every use: progressive and passive helper ("is running", "was taken"), copula ("is happy", "It's in the box"), existential ("There *is* a problem"), and
  the forms `to be`, `being`, `been`, `'s` (= is), `'re`, `'m`. (A noun `being` meaning "existence", as in "a human being", is NOUN.)
- **`have`** is AUX only when it helps another verb form the perfect ("has gone", "had known", "having finished", "*'ve* seen"). It is VERB when it means own, hold, experience, eat, cause or must ("has a car", "had lunch", "have a look", "*have* to leave").
- **`do`** is AUX when it helps in a question, negation, emphasis or ellipsis ("*Did* you see it?", "I *do* not know", "Yes, I *do*."). It is VERB when it is the main verb ("James never *did* it", "*do* homework").
- Modals are AUX. `need` and `dare` are AUX only directly before a bare verb without `to` ("You *need* not worry"); otherwise VERB.
- All other linking verbs (`become, seem, remain, feel, look, get`) are VERB. `going` in "is going to" and `used` in "used to" are VERB; the following `to` is PART.
- `'s`: = is → AUX; = has (perfect helper) → AUX; = has (possess) → VERB; possessive → PART (4.15); in `let's` → PRON.

### 4.3 `this, that, these, those, some, each, all, any, what, another, which, both`: DET or PRON - 68 disagreements

- **DET** when the word modifies a noun in the same phrase: "*that* day", "*some* people", "*what* time", "*each* student", "*all* my lessons", "*these* mangoes".
- **PRON** when the word stands alone as the whole noun phrase: "*These* are hot", "I like *that*", "*some* of them", "*each* of us", "*What* happened?", "*This* is mine".
- A quantifier that floats after the pronoun it quantifies is PRON: "They *all* missed it", "keeping us *both* busy".

### 4.4 `when, where, as, so, since` with clauses - 67 disagreements

- `when`, `where`, `while`: **SCONJ** when they open an adverbial clause ("*When* he returned, ...", "A freeze occurs *when* the temperature drops", "Stay *where* you are").
  **ADV** when they ask a question, direct or indirect ("*When* did you go?", "I asked *when* he left") or follow a noun as a relative adverb ("the day *when* ...").
- `why`, `how`: always ADV ("*How* do remotes work?", "*How* well it sounds!").
- `as`: SCONJ before a clause or ADP before a noun phrase (4.1); the **first** `as` of an equal comparison ("*as* tall as") is ADV.
- `so`: ADV ("*so* nice", "*So* we left"). In "*so that*" (purpose) both words are SCONJ.
- `since`: SCONJ before a clause (4.1), ADP before a time phrase ("since Monday"), ADV alone.

### 4.5 `more, much, little, less, next, right, far, long, very, any`: ADJ or ADV - 45 disagreements

Look at what the word modifies. **ADJ** when it modifies a noun ("*more* energy", "*next* week", "*little* time") or describes the subject after a linking verb.
**ADV** when it modifies a verb, an adjective, another adverb or the whole clause ("*much* better", "*more* quickly", "I like it *more*", "*any* clearer", "*right* on top", "What happens *next*?",
"*more* than 300 geysers", "*Very* likely"). **PRON** when it stands alone as the whole noun phrase ("*Much* was lost", "*most* of them").

### 4.6 `to`

**PART** only as the infinitive marker directly before a verb ("want *to* go", "is going *to* get"). Everywhere else ADP, including before a gerund or noun phrase ("talking *to* my cat",
"look forward *to* seeing you", "from first *to* last"). - 21 disagreements

### 4.7 Noun or adjective - 43 disagreements

Ask whether a dictionary lists the word, in this sense, as an adjective. If yes: **ADJ** ("*military* base", "*official* statement", "*classical* music", "*own*", "*opposite* side").
An ordinary noun placed before another noun stays **NOUN** ("*salt* water", "*refugee* camp", "*television* show", "*minority* opinions", "*health* risks"). An adjective used as a noun after `the`
is NOUN ("the *unknown*", "the *rich*"). After a form of `be`, a word is ADJ if `very` or `so` could precede it ("so *fun*"), otherwise NOUN ("great *fun*").

### 4.8 Place words: ADP or ADV (`back, about, down, up, around, in, out, over, through, near, inside, together`) - 33 disagreements

**ADP** only when the word has a noun phrase right after it ("*around* this time", "*down* the street", "*through* the door"). With no noun phrase after it the word is **ADV**, including the particle of a phrasal verb:
"came *back* from holiday", "give *up*", "turn it *off*", "look *around*", "come *in*".

### 4.9 `that`

**SCONJ** when it opens a complement clause ("I know *that* she left"). **PRON** when it is a relative word that is the subject or object of its clause ("the book *that* I read") or a demonstrative standing alone
("*That* is nice", "I like *that*"). **DET** before a noun ("*that* day"). - 16 disagreements

### 4.10 Participles: VERB or ADJ (`hooked, trapped, operating, dismayed, added, observed, encouraged`) - 25 disagreements

**ADJ** when the form describes a property or state and could be preceded by `very`, `so` or `quite` ("a *tired* man", "very *interested*", "got *hooked* on drugs", "I am *trapped*", "*operating* modes", "the *broken* window").
**VERB** when it has an object, agent (`by ...`) or adverb of its own, or forms a perfect or passive with an auxiliary and describes an event ("was *broken* by the child", "the man *standing* there", "*Having* finished, she left",
"is *making* a face").

### 4.11 Quantity words and the closed DET list (`such, other, many, most, few, all, no, both, either`) - 28 disagreements

- **DET** is only: `a, an, the, this, that, these, those, some, any, no, every, each, all, both, either, neither, another`, `which, what, whose` before a noun, and the possessive words (`my, your, his, her, its, our, their`) before a noun.
- Other quantity and degree words (`such, other, many, most, few, several, enough, much, more, less`) are **ADJ** before a noun.
- `no` and `all` before an adjective, adverb or prepositional phrase are **ADV** ("*no* better", "*no* less than", "life is *all* about work", "*all* alone").
- `either`, `both`, `neither` in a pair with `or` / `and` / `nor` are CCONJ ("*either* this *or* that"); `either` before a noun is DET ("*either* way").

### 4.12 `one`, and plural round numbers - 12 + 7 disagreements

- `one` is **NUM** when it counts a noun right after it ("*one* teacher", "*one* part varies"). It is **PRON** when it stands for a noun or is generic ("the red *one*", "*one* of the men", "*one* should rest").
- Plural round numbers and decades used as nouns are **NOUN** ("*hundreds* of people", "in the *1980s*"). Cardinals that count are NUM ("two hundred", "3 million").

### 4.13 Participial prepositions and gerunds as nouns - 7 + 15 disagreements

- `including, according (to), concerning, regarding, following, given` used as prepositions are **ADP** ("*including* the pilot", "*according* to the report").
- An `-ing` word is **NOUN** when an article, possessive, adjective or plural form precedes it, or `of` follows it ("the *building*", "students' *learning*", "the *signing* of the treaty", "*playing* cards", "state of *being*").
  It is **VERB** with `VerbForm=Ger` when it has an object or adverb of its own, or stands bare ("risks of *smoking*", "like *talking* to my cat", "*Smoking* kills").

### 4.14 Interjections - 6 disagreements

INTJ for greetings, replies, exclamations and polite formulas: `hello, oh, wow, yes` and `no` as a reply, `please` as a politeness marker, `thanks`, `pray` as a polite opener, `help!` as a cry ("Please sit down", "*Thanks* for coming", "*Help!*").
The same words are ordinary words elsewhere: `no` + noun is DET, `help me` is VERB, `thanks` in "many thanks" is NOUN.

### 4.15 Possessive and contracted `'s`, plural possessive apostrophe - 8 disagreements

- Possessive `'s` and the lone `'` after a plural possessive are **PART** ("Maria *'s* report", "the dogs *'* bowls"). Quotation marks are PUNCT.
- `'s` meaning is/has: see 4.2.

### 4.16 Foreign words and fragments - 39 disagreements

Every token of a phrase in another language is **X** ("mea culpa", "avec les hommes"), as are broken fragments ("p ennyworth"), URLs and file names. Established loanwords in English dictionaries (`jazz, cafe`) are ordinary English.
A foreign word that is itself the name of something is PROPN when part of a name.

### 4.17 Proper nouns, demonyms and capitalization - 69 disagreements (NOUN/PROPN 38, ADJ/PROPN 31)

- **PROPN** only for names (see Section 3). Capitalization alone never makes a word PROPN: sentence starts, headings, `Chapter`, shouted words are tagged by function. A Roman numeral naming a chapter is NUM.
- Species and generic groups are NOUN in lower case ("magpies", "church" as an institution in general). Members of a people are **NOUN** ("a *Jew*", "the *Americans*"). Language names are PROPN ("speaks *English*").
- Words derived from names and used to describe a noun are **ADJ** ("*American* mangoes", "*Indian* food", "*English* lessons", "*Kabyle* lessons"). Parts of a multiword name stay PROPN ("*South* Korea").
- Abbreviations follow the class of what they stand for: `NASA` PROPN, `REM`, `LSD`, `GPS` NOUN.

### 4.18 `there`

**PRON** when it only fills the subject slot of an existence statement ("*There* is no noise"; the following `be` is AUX, 4.2). **ADV** when it means "at that place" ("Put it *there*", "*There*, they can see the falls").

### 4.19 Other short rules

- `not` and `n't` are PART (lemma `not`); `never` is ADV.
- `but` is CCONJ ("but" meaning "only" is ADV).
- Ordinals are ADJ (ADV in "First, mix the flour"). `second` in "a second opinion" is ADJ.
- Contraction stems get the tag of the word they stand for: `ca` AUX (can), `wo` AUX (will), `sha` AUX (shall), `ai` AUX (be).

## 5. Lemma rules

- Lowercase dictionary form, except PROPN lemmas keep their capitalization and `I` stays `I`.
- Verbs: base form (`ran` -> `run`, `is` -> `be`, `n't` -> `not`). Nouns: singular (`mice` -> `mouse`); a noun used only in the plural keeps it (`scissors`).
- Comparative and superlative adjectives and adverbs: the plain form (`better` ADJ -> `good`, `fastest` -> `fast`); `more` and `most` unchanged.
- Pronouns: case forms map to the nominative (`me` -> `I`, `him` -> `he`, `them` -> `they`, `us` -> `we`); demonstratives map to the singular (`these` -> `this`, `those` -> `that`); possessive words and reflexives keep their form.
- Contraction pieces: `'s` (is) -> `be`, `'s` (has) -> `have`, `'s` possessive -> `'s`, `'re`/`'m` -> `be`, `'ve` -> `have`, `'ll` -> `will`, `'d` -> `would` or `have`, `n't` -> `not`, `ca` -> `can`, `wo` -> `will`, `sha` -> `shall`, `ai` -> `be`; `'s` in `let's` -> `we`.
- Numbers keep their form; punctuation and symbols: lemma = form; X tokens: lemma = the form in lowercase.

## 6. Morphological features

Write `Name=Value` pairs joined by `|` in alphabetical order of the name, or `_` when none apply. **Do not invent other features.** When a feature does not clearly apply, leave it out.

| Feature | Meaning | Values | Used on |
| --- | --- | --- | --- |
| `Number` | grammatical number of the form | `Sing`, `Plur` | NOUN, PROPN, PRON (`I` Sing, `we` Plur), DET (`this` Sing, `these` Plur), finite VERB/AUX when the subject number is clear |
| `Person` | whom the word refers to or agrees with | `1`, `2`, `3` | PRON (`I`/`we` 1, `you` 2, `he`/`she`/`it`/`they` 3), finite VERB/AUX when the subject is clear |
| `Tense` | time of a verb form | `Pres`, `Past` | finite VERB/AUX; participles (`Past` for `-ed`/`-en` forms, `Pres` for `-ing` participles) |
| `VerbForm` | shape of the verb | `Fin` finite, `Inf` bare infinitive, `Part` participle, `Ger` gerund | VERB, AUX |
| `Mood` | attitude of a finite verb | `Ind` statement or question, `Imp` command, `Sub` subjunctive | finite VERB/AUX; leave out on modals |
| `Degree` | comparison | `Cmp` comparative, `Sup` superlative | ADJ, ADV (plain forms get no feature) |
| `PronType` | kind of pronoun-like word | `Prs` personal/possessive/reflexive, `Dem`, `Int` question word, `Rel` relative word, `Art` article, `Ind` indefinite, `Neg` negative, `Tot` total/each | PRON, DET, ADV (`how`, `why`, `when`, `where` as question words: `Int`; relative: `Rel`) |
| `Poss` | possessive | `Yes` | PRON (`mine, yours, hers, ours, theirs, whose`) and DET (`my, your, his, her, its, our, their`) |

Allowed per UPOS (anything else is deleted by the validator): NOUN/PROPN `Number`; PRON `Number Person PronType Poss`; DET `Number PronType Poss`; VERB/AUX `VerbForm Tense Person Number Mood`; ADJ `Degree`; ADV `Degree PronType`. Other tags take no features.

Verb examples: `walks` -> `Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin`; `walked` (finite) -> `Mood=Ind|Tense=Past|VerbForm=Fin`; `to walk` -> `walk` `VerbForm=Inf`;
`walking` (participle) -> `Tense=Pres|VerbForm=Part`; `walking` (gerund) -> `VerbForm=Ger`; `broken` (verbal) -> `Tense=Past|VerbForm=Part`; "Open the door." -> `Open` `Mood=Imp|VerbForm=Fin`; `would` -> `VerbForm=Fin`.

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

One line per token, tab separated: `sent_id`, `idx`, `form`, `lemma`, `UPOS`, `feats`. `idx` counts from 0, `form` repeats the input token exactly. Example:

```text
S1 0 She she PRON Number=Sing|Person=3|PronType=Prs
S1 1 has have VERB Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin
S1 2 a a DET Number=Sing|PronType=Art
S1 3 car car NOUN Number=Sing
S1 4 . . PUNCT _
```
