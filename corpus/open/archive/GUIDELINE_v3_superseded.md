<!-- markdownlint-disable MD013 MD056 MD060 -->

# Annotation Guideline for the Open English POS Corpus (version 3)

This guideline was written for this corpus and uses no other treebank's documentation. It uses the 17 UPOS tag names only as
labels (names are facts, not text); every definition, rule and example is our own.

**Provenance of the hard-case rules (version 3).** Section 4 was rebuilt **only from our own pilot data**, in two rounds, without
looking at any outside treebank's annotations:
(1) disagreements between the two first-draft annotators on the first pilot draw (732 tokens), which produced version 2;
(2) disagreements on the second pilot draw, tagged with version 2 by gpt-6.1-sol, compared with gemma4:26b (807 of 17,376 tokens, 4.64%) and with
claude-sonnet-5.5 (112 tokens, 0.64%). gpt-6.1-sol helped analyze the second round (its rule proposal is kept in `archive/sol_rule_proposal.md`);
the final wording was edited by the project. Section 4 is ordered by how often each kind of disagreement occurred (counts are the disagreements with gemma4:26b, see
`guideline-derivation.md`). An earlier version 1, whose list was partly chosen from error categories seen in an exploratory test against a public treebank, is
superseded and kept only for the record in `archive/`.

## 1. Task

You receive sentences that are already split into tokens. For every token give:

1. **UPOS**: exactly one tag from Section 3;
2. **lemma**: the dictionary form (Section 5);
3. **features**: a few morphological features (Section 6), or `_` when none apply.

Never merge, split, drop or re-order tokens. Tokens come from a fixed rule-based tokenizer, so you will see split-off clitics such as `do` + `n't`,
`ca` + `n't`, `she` + `'s`, and punctuation as separate tokens.

**Governing principle.** Tag a word by the job it does in this sentence, not by its spelling, its capitalization, or the class it usually belongs to.
Ask "what is this word doing here?". When two tags seem possible, apply the rules of Section 4; if none applies, choose the tag that matches the word's
function in its immediate phrase.

## 2. The tag set

`NOUN PROPN PRON VERB AUX ADJ ADV ADP CCONJ SCONJ INTJ DET PART NUM PUNCT SYM X`

## 3. Tag definitions

**NOUN** - a common noun: names a kind of thing, person, place, event, substance or idea (`dog`, `idea`, `water`, `km`). Includes mass nouns, abbreviations for
common things (`REM`, `GPS`, `HIV`) and nouns used as modifiers inside a noun phrase (`the *kitchen* table`).

**PROPN** - a name for one particular person, place, organization, brand, work, event, language, religion, weekday, month or holiday (`Maria`, `Ohio`, `NASA`, `Monday`,
`Judaism`, `the Holocaust`, `English`). Every content word of a multiword name is PROPN (`South Korea`); function words inside it keep their own tag (`Department of Energy` =
PROPN ADP PROPN). A title right before a person's name is part of the name (`Dr. Lee`).

**PRON** - a word that stands in for a whole noun phrase: personal (`I, me, you, he, she, it, we, they`), reflexive (`myself`), independent possessive (`mine, yours, hers, ours, theirs`),
question or relative words used alone (`who, whom, which, what`), indefinite compounds (`someone, anything, nobody`), quantity and demonstrative words that stand alone (4.2, 4.3),
`one` standing for a noun (4.15), `others`, existential `there` (4.18).

**VERB** - a word naming an action, event or state that serves as a main verb, plus infinitives, gerunds and participles that behave verbally (4.7, 4.12).

**AUX** - a form of `be` in any use, `have` or `do` when helping another verb, and the modals `can, could, may, might, must, shall, should, will, would, ought` (4.8).

**ADJ** - a word that describes a noun, or is the described state after a linking verb (`red`, `happy`); ordinal numbers (`first, 2nd`); comparatives and superlatives of adjectives;
quantity words before a noun (`many, much, few, little, several, enough, more, most, less, such, other`) (4.13).

**ADV** - a word that modifies a verb, adjective, other adverb or whole clause: manner, degree, time, place, frequency, focus (`quickly, very, too, only, always, here, there` (locative),
`soon, then, also`), place words with no noun phrase after them (4.6), and the question words `why`, `how`.

**ADP** - a word that links a noun phrase (or gerund phrase) to the rest of the sentence by showing a relation (`in, on, at, of, for, from, with, by, about, through, during, to` before a
noun phrase) (4.4, 4.6, 4.11).

**CCONJ** - a word that joins two equal-status units: `and, or, but, nor, yet`, `plus`, explanatory `for` between two independent statements, and `&` read as "and"; also the paired joiners
`both ... and`, `either ... or`, `neither ... nor`.

**SCONJ** - a word that introduces a subordinate clause: `that` (4.14), `if, because, although, though, while, whereas, unless, whether`, and `than, as, like, before, after, since, until,
when, where, so` in the uses described in 4.4 and 4.9.

**INTJ** - an utterance by itself or a discourse marker: greetings, replies, exclamations, polite formulas (4.20).

**DET** - a word before a noun that specifies which or how much of it without describing it: articles (`a, an, the`), demonstratives before a noun (`this, that, these, those`), `some,
any, no, every, each, all, both, either, neither, another`, `which, what, whose` before a noun, and the possessive words before a noun (`my, your, his, her, its, our, their`). Closed list (4.13).

**PART** - the infinitive marker `to` (4.10), the negators `not` and `n't`, and the possessive clitic `'s` or a lone `'` after a plural possessive (4.19).

**NUM** - a cardinal number used to count or measure, in digits or words (`3, 1990, three, hundred, 10:30`, `3.5`), also `million, billion` after a number. Ordinals are ADJ.

**PUNCT** - punctuation marks: `. , ; : ! ? " ( ) [ ] - --` and `...`, quotation marks, and `/` used as a separator (4.16).

**SYM** - symbols that are not punctuation: `$ % + = < > @ #`, currency signs, math signs, emoticons.

**X** - a token that is not English or cannot be analyzed: words of a foreign-language phrase, fragments, URLs, e-mail addresses, file names (4.17).

## 4. Hard cases, in order of how often our pilot annotators disagreed

### 4.1 Noun or adjective - 82 disagreements (13 between strong annotators)

Ask whether a dictionary lists the word, in this sense, as an adjective. If yes: **ADJ** ("*military* base", "*official* statement", "*same*", "*past* events", "*male* workers", "*dead* wood").
An ordinary noun placed before another noun stays **NOUN** ("*salt* water", "*steel* structure", "*winter* storms", "*surface* water", "*animal* shelters", "*refugee* camp", "*television* show",
"*kitchen* table", "*health* risks"). Words formed from an adjective or participle that name a class of people without a noun are **NOUN** after `the` ("the *unknown*", "the *rich*", "the *marginalized*").
After a form of `be`, a word is ADJ if `very` or `so` could precede it ("so *fun*"), otherwise NOUN ("great *fun*").

### 4.2 `this, that, these, those, some, each, all, any, what, another, which, both, her`: DET or PRON - 77 disagreements

- **DET** when the word modifies a noun that is expressed in the same phrase: "*that* day", "*some* people", "*what* time", "*each* student", "*all* my lessons", "*these* mangoes", "*her* book".
- **PRON** when the word stands alone as the whole noun phrase: "*These* are hot", "I like *that*", "*some* of them", "*each* of us", "*What* happened?", "*This* is a fire exit". A modifier or
  relative clause that follows does not count as a noun: "*That* which remains" is PRON.
- A quantifier that floats after the pronoun it quantifies is PRON: "They *all* missed it", "keeping us *both* busy"; `others`, `plenty` and `none` standing for people or things are PRON ("signaled the *others*", "*plenty* of ideas").

### 4.3 `more, much, little, less, most, next, right, far, long, cold, firm, thin, very, any`: ADJ, ADV or PRON - 59 + 14 + 14 disagreements

Look at what the word modifies. **ADJ** when it modifies a noun ("*more* energy", "*next* week", "*little* time") or describes the subject or object after a linking or resultative verb ("stands *firm*", "spread too *thin*", "keep it *cold*").
**ADV** when it modifies a verb as manner or time, or an adjective, another adverb or the whole clause ("*much* better", "*more* comfortable", "*more* quickly", "I like it *more*", "*any* clearer", "*right* on top", "What happens *next*?",
"*more* than 300", "*Very* likely"). **PRON** when it stands for a quantity or amount without a noun ("*Much* was lost", "*most* of them", "We don't have enough for *more*", "learn *more* about it" = more information).
Test for PRON versus ADV: can you replace it with "more things / information" (PRON) or only with "to a greater degree" (ADV)?

### 4.4 Preposition or subordinating conjunction (`than, as, before, after, until, since, like, because, for, of, about, by`) - 58 disagreements

- **SCONJ** when the word introduces a clause with its own subject: a finite verb follows the subject, or only an auxiliary of it is left over in a comparison.
  "He left *before* I arrived." "taller *than* I am", "more energy *than* they possess", "*as* he said", "*like* I said", "*after* he ate", "*since* they came", "*until* she calls". Look at the word's *own* complement, not at a verb that comes later inside a noun phrase.
- **ADP** when what follows is a noun phrase, pronoun, number, gerund phrase or adverbial phrase: "taller *than* him", "*as* a vegan", "*like* a bird", "*after* World War II", "*before* sex", "*before* giving a speech", "*until* long after the war",
  "*for* making dinner", "*of* smoking", "*about* leaving", "*by* stacking the deck", "*without* asking".
- With nothing after it the word is ADV: "I had seen her *before*."
- `but` meaning "except" is ADP ("Everyone *but* Tom"). `for` joining two independent statements ("..., *for* he was tired") is CCONJ; `for` + noun phrase is ADP.

### 4.5 Proper noun or common noun - 54 disagreements

**PROPN** only for names (Section 3): people, places, organizations, brands, religions and philosophies ("*Judaism*"), historical events ("the *Holocaust*"), the `Internet`. Capitalization alone never makes a word PROPN: sentence starts, headings, `Chapter`, shouted words,
and decorative capitals in poetry ("Lakes of Light") are tagged by function. Species and generic groups are NOUN ("magpies", "ravens"). Members of a people or religion are **NOUN** ("a *Jew*", "the *Americans*", "the *Europeans*"). Language names are PROPN ("speaks *English*").
Abbreviations follow the class of what they stand for: `NASA`, `UNESCO` PROPN; `REM`, `LSD`, `GPS`, `GIS`, `HIV`, `AMOC` NOUN.

### 4.6 Place words: ADP or ADV (`in, about, around, over, off, down, up, back, out, through, near, inside, together`) - 51 disagreements

**ADP** when the word has a noun phrase after it ("*around* this time", "*down* the street", "*over* a hundred miles") or its noun phrase is moved away and still belongs to it ("the house he lived *in*", "What are you looking *for*?").
Otherwise **ADV**: the particle of a phrasal verb ("came *back* from holiday", "give *up*", "turn it *off*", "log *back* in", "take *in* carbon dioxide") and a free place or degree word ("look *around*", "come *in*").

### 4.7 Participles: VERB or ADJ (`packed, caught, freezing, associated, broken, done, ruling, growing, dressed`) - 50 disagreements

Ask: is an event happening, or is a condition being described?
**VERB** when an event or process is asserted: it has an object, an agent (`by ...`) or a manner adverb of its own, or forms a perfect or progressive with an auxiliary, or a reduced clause ("The vase was *broken* by the child", "the man *standing* there",
"*Having* finished, she left", "is *making* a face", "*rapidly developing* storms", "get it *done* well").
**ADJ** when it describes a state and could be preceded by `very`, `so` or `quite`: "a *tired* man", "got *hooked* on drugs", "I am *trapped*", "*operating* modes", "the yard was *littered* with car parts", "*associated* costs", "the *broken* window".
A passive without an agent that describes a result is ADJ ("The door is *closed*").

### 4.8 `be`, `have`, `do` and modals: AUX or VERB - 46 disagreements

- **Every form of `be` is AUX** in every use: progressive and passive helper ("is running", "was taken"), copula ("is happy"), existential ("There *is* a problem"), and the forms `to be`, `being`, `been`, `'s` (= is), `'re`, `'m`. (A noun `being` meaning "existence", as in "a human being", is NOUN.)
- **`have`** is AUX only when it helps another verb form the perfect ("has gone", "had known", "having finished", "*'ve* seen", "having been given"). It is VERB when it means own, hold, experience, eat, cause or must ("has a car", "had lunch", "have a look", "*have* to leave", "had it *inscribed*").
- **`do`** is AUX when it helps in a question, negation, emphasis or ellipsis ("*Did* you see it?", "I *do* not know", "Yes, I *do*."). It is VERB when it is the main verb ("I *did* it for you", "*do* homework").
- Modals are AUX. `need` and `dare` are AUX only directly before a bare verb without `to` ("You *need* not worry"); otherwise VERB.
- All other linking verbs (`become, seem, remain, feel, look, get`) are VERB. `going` in "is going to" and `used` in "used to" are VERB; the following `to` is PART.
- `'s`: = is -> AUX; = has (perfect helper) -> AUX; = has (possess) -> VERB; possessive -> PART (4.19); in `let's` -> PRON.

### 4.9 `when, where, as, so, since, once` with clauses - 36 disagreements

- `when`, `where`, `while`, `once`: **SCONJ** when they open an adverbial clause ("*When* he returned, ...", "A freeze occurs *when* the temperature drops", "Stay *where* you are"). **ADV** when they ask a question, direct or indirect ("*When* did you go?", "I asked *where* he lived") or follow a noun as a relative adverb ("the years *when* ...", "a place *where* ...").
- `why`, `how`: ADV ("*How* do remotes work?", "*How* well it sounds!").
- `as`: SCONJ before a clause or ADP before a noun phrase (4.4); the **first** `as` of an equal comparison ("*as* tall as") is ADV.
- `so`: **SCONJ** when it states a purpose ("*so* that we can go", and also without `that`: "close it *so* we can sleep"); **ADV** otherwise, including "*so* nice" and sentence-opening "*So*, ...".
- `since`: SCONJ before a clause (4.4), ADP before a time phrase ("since Monday"), ADV alone.

### 4.10 `to` - 28 disagreements

**PART** only as the infinitive marker before a verb, even with a word between ("want *to* go", "is going *to* get", "to *quickly* leave"). Everywhere else ADP, including before a gerund or noun phrase ("talking *to* my cat", "look forward *to* seeing you", "from first *to* last", "*to* Europeans").

### 4.11 Demonyms and names used as modifiers - 27 disagreements

Words derived from names and used to describe a noun are **ADJ** ("*American* mangoes", "*Indian* food", "*English* words", "*Austrian* German", "*Kabyle* lessons"). A name that is itself part of a larger name stays PROPN ("*Northern* Arizona", "*South* Korea", "*U.S.* Army", "*Big* Ben").

### 4.12 `-ing` words: NOUN or VERB - 20 disagreements

**NOUN** when it names an activity or thing and an article, possessive, adjective or plural precedes it, or `of` follows it, or it is a fixed modifier or compound ("the *building*", "students' *learning*", "the *signing* of the treaty", "*fishing* nets", "*playing* cards", "below *freezing*", "state of *being*").
**VERB** with `VerbForm=Ger` when it has an object or adverb of its own, or stands as a bare predicate ("risks of *smoking*", "like *talking* to my cat", "*Smoking* kills", "*Understanding* the risks takes time"). A word mentioned as a word ("the word *need*") is NOUN.

### 4.13 Quantity words and the closed DET list (`such, other, many, most, few, all, no, both, either, the`) - 15 + 6 + 8 disagreements

- **DET** is only: `a, an, the, this, that, these, those, some, any, no, every, each, all, both, either, neither, another`, `which, what, whose` before a noun, and the possessive words (`my, your, his, her, its, our, their`) before a noun.
- Other quantity and degree words (`such, other, many, most, few, several, enough, much, more, less`) are **ADJ** before a noun ("*most* ponds", "*other* years", "*such* illusions").
- `no` and `all` before an adjective, adverb or prepositional phrase are **ADV** ("*no* better", "*no* more proof", "*no* less than", "life is *all* about work", "*all* alone"); `the` before a comparative ("*the* worse", "*the* more the better") is ADV.
- `either`, `both`, `neither` in a pair with `or` / `and` / `nor` are CCONJ ("*either* this *or* that"); `either` before a noun is DET ("*either* way").

### 4.14 `that` - 15 disagreements

**PRON** when it is a relative word that is the subject or object of its clause (a word is missing in the clause without it: "the corpus *that* becomes", "the book *that* I read") or a demonstrative standing alone ("*That* does n't seem true", "I like *that*").
**SCONJ** when it opens a clause that is complete without it ("I know *that* she left", "the ways *that* pesticides are transported"). **DET** before a noun ("*that* day").

### 4.15 `one` and other numbers - 14 + 5 disagreements

- `one` is **NUM** when it counts or contrasts with other numbers, with or without a following noun ("*one* teacher", "*one* or two of them", "*one* part varies"). It is **PRON** when it stands for a noun or is generic ("the red *one*", "*one* of the men" when no counting is meant, "*one* should rest").
- Plural round numbers and decades used as nouns are **NOUN** ("*hundreds* of people", "*thousands* of years", "in the *1980s*"). Exact cardinals count: NUM ("two hundred", "18 million").

### 4.16 Punctuation or symbol - 15 disagreements

Marks that structure text are **PUNCT**: `: ; , . ! ? ( ) [ ] " --` and `/` between alternatives. Symbols that stand for a quantity, operator or currency are **SYM**: `% $ + = < >`.

### 4.17 Foreign words and fragments - 31 disagreements

Every token of a phrase in another language is **X** ("mea culpa", "avec les hommes"), as are broken fragments ("p ennyworth", "month-to"), URLs and file names. Established loanwords in English dictionaries (`jazz, cafe`) are ordinary English. A letter used as a speaker label or list label
("A :", "B :") is **PROPN**; a letter mentioned as a letter ("the letter D") is NOUN.

### 4.18 `there`

**PRON** when it only fills the subject slot of an existence statement ("*There* is no noise"; the following `be` is AUX, 4.8). **ADV** when it means "at that place" ("Put it *there*", "*There*, they can see the falls").

### 4.19 Contracted and possessive `'s`; `not`; small words - 7 disagreements

- Possessive `'s` and the lone `'` after a plural possessive are **PART** ("Maria *'s* report", "the dogs *'* bowls"). Quotation marks are PUNCT. `'s` meaning is/has: see 4.8.
- `not` and `n't` are PART (lemma `not`); `never` is ADV. `but` is CCONJ (ADP when "except", 4.4).
- Ordinals are ADJ (ADV in "First, mix the flour"). `second` in "a second opinion" is ADJ.
- Contraction stems get the tag of the word they stand for: `ca` AUX (can), `wo` AUX (will), `sha` AUX (shall), `ai` AUX (be).
- A name used as a verb is VERB ("you B&Q it"); a capitalized activity is not automatically a name.
- Idioms keep the tags of their parts: "at *last*", "of *course*" (NOUN).
- Participial prepositions `including, according (to), concerning, regarding, following, given` used as prepositions are **ADP**.
- Function words keep their tag inside titles ("Ask a Teacher", "This and That").

### 4.20 Interjections and discourse markers

INTJ for greetings, replies, exclamations and polite formulas: `hello, oh, wow, yes` and `no` as a reply, `please` as a politeness marker, `thanks`, `pray`, `help!` as a cry, `right?` as a tag question, `NB` ("*Please* sit down", "*Thanks* for coming", "*Help!*", "I know, *right*?").
The same words are ordinary words elsewhere: `no` + noun is DET, `help me` is VERB, `thanks` in "many thanks" is NOUN, `well done` is ADV ADJ.

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
