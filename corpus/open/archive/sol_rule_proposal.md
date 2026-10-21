<!-- markdownlint-disable MD013 MD056 MD060 -->

# Proposed Section 4: Pilot hard cases

**G/S** means Gemma / strong–strong disagreements, not errors. Groups rank by G, covering every G ≥ 10 pair and every strong pair. X pairs are combined below. Repeated templates/alignment shifts inflate raw counts; neither annotator is presumed correct.

Sentence quotations preserve the supplied tokens, without index prefixes.

## 4. Hard cases

### 4.1 Noun category versus description — ADJ/NOUN, 82/13

P0029: `The Molotov-Ribbentrop Pact was n't just a non-aggression pact .`

**Rule:** Ordinary noun modifiers remain NOUN; established adjectival senses are ADJ. Nominalized adjectives naming classes without expressed nouns are NOUN.
**Test:** Category/material/activity, or descriptive property?
**Examples:** `steel structure`: NOUN; `cold temperatures`: ADJ; `the marginalized`: NOUN.

### 4.2 Noun-dependent versus independent reference — DET/PRON, 77/1

P0042: `This is the fire exit .`

**Rule:** Use DET with an expressed noun head, PRON when the word supplies nominal reference itself.
**Test:** Identify the noun; a following modifier alone does not count.
**Examples:** `whose remains`: DET; `This is`: PRON; `his own` in P0810: PRON.

### 4.3 Property versus manner/degree — ADJ/ADV, 59/15

P0180: `He stands firm in grace .`

**Rule:** Entity properties and resulting states are ADJ; manner, degree, and temporal modifiers are ADV.
**Test:** Does it characterize an entity or modify an action/degree?
**Examples:** `stands firm`: ADJ; `spread too thin`: ADJ; `more comfortable`: ADV; `day-to-day` in P0612: ADV.

### 4.4 Phrase versus subordinate clause — ADP/SCONJ, 58/1

P0146: `As a vegan , I keep kosher by default .`

**Rule:** Use ADP with nominal, gerund, or adverbial complements; SCONJ with clauses having their own subjects.
**Test:** Inspect its own complement, not a later embedded verb.
**Examples:** `before sex`: ADP; `before giving a speech`: ADP; `until long after` in P0800: ADP.

### 4.5 Common category versus identifying name — NOUN/PROPN, 54/8

P0205: `Judaism includes the belief that everyone is born innocent .`

**Rule:** Identifying names are PROPN; generic categories, technical common-noun abbreviations, and decorative capitalization are NOUN.
**Test:** Named entity or kind?
**Examples:** `Judaism`: PROPN; `Internet`: NOUN; `AMOC`: NOUN; `Lakes of Light`: NOUN ADP NOUN.

### 4.6 Preposition versus particle/adverb — ADP/ADV, 51/14

P0021: `Log out and log back in .`

**Rule:** Prepositions remain ADP with displaced/shared complements; verb particles and degree modifiers are ADV.
**Test:** Reconstruct the complement; can the verb's object precede the particle?
**Examples:** `worked for` in P0379: ADP; `log back in`: ADV; `take in carbon dioxide`: ADV; `over a hundred`: ADV.

### 4.7 Participial event versus state — ADJ/VERB, 50/15

P0046: `The yard was littered with car parts .`

**Rule:** Asserted events, progressives, and perfects use VERB; descriptions without event evidence use ADJ, including ambiguous stative passives.
**Test:** Happening, or condition?
**Examples:** `was littered`: ADJ; `was covered`: ADJ; `get done well`: VERB; `rapidly developing storms`: VERB.

### 4.8 Helping versus lexical verb — AUX/VERB, 46/1

P0347: `I did it for you .`

**Rule:** Verbal be is always AUX; have/do are AUX only in auxiliary constructions, otherwise VERB.
**Test:** Perfect/support, or possession/action/causation?
**Examples:** `did it`: VERB; `having been given`: AUX; `have inscribed` in P0912: VERB, causative.

### 4.9 Linker versus adverb — ADV/SCONJ, 36/5

P0696: `So , in what way is bottled artesian well water different from other well water ?`

**Rule:** Purpose so is SCONJ, discourse/inferential so ADV; relative/interrogative when/where are ADV, adjunct-clause introducers SCONJ.
**Test:** Purpose “in order that,” antecedent/question, or adjunct?
**Examples:** `so we can prevent`: SCONJ; `So ,`: ADV; `years when`: ADV; `When someone`: SCONJ.

### 4.10 Foreign text, fragments, and citations — X/lexical tags, 31/8

G pairs: NOUN/X 8, ADJ/X 8, ADV/X 5, DET/X 4, CCONJ/X 3, ADP/X 1, SYM/X 1, PROPN/X 1.
S pairs: NOUN/X 5, INTJ/X 1, ADJ/X 1, ADV/X 1.

P0228: `NB . This saying is a bit peremptory .`

**Rule:** Foreign spans and broken fusions are X; letters and isolated nominal mentions are NOUN, conventional NB INTJ and &c ADV. Preserve explicitly demonstrated citation categories.
**Test:** Foreign syntax, broken token, or English use/mention?
**Examples:** `les hommes`: X X; `month-to`: X; letter `D`: NOUN; `NB`: INTJ.

### 4.11 Infinitive marker versus preposition — ADP/PART, 28/0

P0023: `Follow the link to take a short quiz .`

**Rule:** Infinitival to is PART; relational to is ADP.
**Test:** Does to introduce an infinitive, allowing intervening modifiers?
**Examples:** `to take`: PART; `to Europeans`: ADP; `to be`: PART.

### 4.12 Derived description versus name — ADJ/PROPN, 27/6

P0224: `My dream is to study Austrian German in Sydney .`

**Rule:** Derived national/language descriptions are ADJ; actual names remain PROPN even attributively.
**Test:** Name itself, or adjective derived from one?
**Examples:** `Austrian German`: ADJ PROPN; `U.S. states`: PROPN NOUN; `Northern Arizona`: ADJ PROPN; `South American coast`: ADJ ADJ NOUN.

### 4.13 Named activity versus verbal construction — NOUN/VERB, 20/4

P0392: `Consider the word " need , " for example .`

**Rule:** Established activity/substance nouns are NOUN; verbal constructions with objects or action-modifying adverbs are VERB. Bare -ing alone is inconclusive.
**Test:** Named thing/activity or verbal predication?
**Examples:** `below freezing`: NOUN; `fishing nets`: NOUN; `Understanding the risks`: VERB; quoted `need` here: NOUN.

### 4.14 Quantity adjective versus determiner — ADJ/DET, 15/0

P0808: `Most ponds , emptied , would leave a meadow no more hollow than we frequently see .`

**Rule:** Retain the current closed DET inventory; other quantity/descriptive words before nouns are ADJ.
**Test:** Check inventory membership.
**Examples:** `Most ponds`: ADJ; `other years`: ADJ; `such illusions`: ADJ.

### 4.15 Referential that versus clause marker — PRON/SCONJ, 15/1

P0284: `However , word expert Martin says that does n't seem true .`

**Rule:** Subject/object or demonstrative that is PRON; clause-marker that, including manner clauses after ways, is SCONJ.
**Test:** A missing subject/object, or merely a manner adjunct?
**Examples:** `corpus that becomes`: PRON; `that does n't seem true`: PRON; `ways that pesticides are transported`: SCONJ.

### 4.16 Punctuation versus symbol — PUNCT/SYM, 15/0

P0285: `B : I was worried about her too .`

**Rule:** Structural punctuation is PUNCT; mathematical, currency, and comparable symbols are SYM.
**Test:** Delimits text or denotes quantity/operator?
**Examples:** `:`: PUNCT; `--`: PUNCT; `%`: SYM.

### 4.17 Counting versus substitute one — NUM/PRON, 14/3

P0805: `I have been inside one or two of them , and know what they are lined with .`

**Rule:** Counting one is NUM even without an immediately following noun; generic/substitute one is PRON.
**Test:** Does contrast with two preserve meaning?
**Examples:** `one or two`: NUM; `1 of which`: NUM; `one ought`: PRON; `one who gets quiet`: PRON.

### 4.18 Quantity description versus argument — ADJ/PRON, 14/1

P0095: `We do n't have enough for more .`

**Rule:** Quantity words are ADJ with nouns, PRON when supplying independent quantity arguments.
**Test:** Is a noun head expressed?
**Examples:** `enough for more`: PRON PRON; `90 percent or more`: PRON; `many variables`: ADJ.

### 4.19 Participants/information versus degree — ADV/PRON, 14/5

P0264: `It is a powerful tool that can help you learn more about English words and phrases .`

**Rule:** Quantified participants/information use PRON; intensity/extent uses ADV.
**Test:** “All members/more information” or “entirely/to a greater degree”?
**Examples:** `learn more`: PRON; `all comes` in P0899: PRON; `being all in the same boat`: PRON; `all around you`: ADV.

### 4.20 Speaker label versus determiner — DET/PROPN, 10/0

P0291: `A : Hey , did you hear about Sarah ?`

**Rule:** Speaker-identifying letters are PROPN; determiners remain DET, including inside titles.
**Test:** Identifies a speaker or determines a noun?
**Examples:** speaker `A`: PROPN; `a domino effect`: DET; title `Their`: DET.

### 4.21 Degree versus determination — ADV/DET, 8/1

P0765: `A fresh wrong did these words inflict : the worse , because they touched on the truth .`

**Rule:** Degree-comparative the and degree-modifying no are ADV; noun-determining uses are DET.
**Test:** Comparative extent or nominal introduction?
**Examples:** `the worse`: ADV; `no more proof`: ADV; `no slouch`: DET.

### 4.22 Components versus whole-phrase function — ADV/NOUN, 7/1

P0191: `They achieved peace at last .`

**Rule:** Retain nominal time expressions and idiom components; an adverbial phrase does not make every component ADV.
**Test:** Identify internal structure first.
**Examples:** `at last`: last NOUN; `of course`: course NOUN; `to-morrow morning`: to-morrow NOUN.

### 4.23 Numbers versus nominal magnitudes — NOUN/NUM, 5/1

P0622: `The chances of drowning at a beach with lifeguards are 1 in 18 million .`

**Rule:** Exact cardinal magnitudes/fractions are NUM; plural indefinite magnitudes are NOUN.
**Test:** Arithmetic value or indefinite multitude?
**Examples:** `18 million`: NUM NUM; `one-half`: NUM; `thousands of years`: NOUN.

### 4.24 Independent reference versus noun — NOUN/PRON, 5/2

P0196: `Mary signaled the others to stop .`

**Rule:** Referential others and independent quantifiers such as plenty are PRON, even with modifiers.
**Test:** Does the word substitute for the people/things/quantity?
**Examples:** `the others`: PRON; `plenty of ideas`: PRON.

### 4.25 Exception/comparison versus coordination — ADP/CCONJ, 3/1

P0034: `Everyone but Tom was invited .`

**Rule:** Exceptive but is ADP; additive as well as retains ADV ADV ADP internally.
**Test:** Exception/comparison complement or coordinated clauses?
**Examples:** `but Tom`: ADP; final `as` in P0573: ADP.

### 4.26 Name converted to action — PROPN/VERB, 3/1

P0207: `You can do it when you B&Q it .`

**Rule:** Names predicating actions are VERB; capitalized activities are not automatically names.
**Test:** Action with subject/object, or named activity being taught?
**Examples:** `you B&Q it`: VERB; `taught Laughing and Grief`: Laughing NOUN unless context establishes a course name.

### 4.27 Discourse response versus adverb — ADV/INTJ, 2/1

P0221, quoted utterance: `I know , right ?`

**Rule:** Reactions/discourse openers are INTJ; genuine question/manner words remain ADV.
**Test:** Requests information or manages interaction?
**Examples:** `right ?`: INTJ; `Why , Jim ,`: INTJ; `Well done`: ADV.

### 4.28 Explanatory coordination versus subordination — CCONJ/SCONJ, 1/1

P0910: `Had such a suggestion been adopted , the theory would have been triumphantly confirmed ; for as a matter of fact , typhus fever has disappeared .`

**Rule:** Explanatory for joining independent assertions is CCONJ; nominal-complement for is ADP.
**Test:** Connects assertions or introduces a nominal?
**Examples:** `; for as a matter of fact`: CCONJ; `for diplomatic reasons`: ADP.

### 4.29 Function words in titles — PRON/PROPN, 0/2

P0357: `You can read and listen to a previous Ask a Teacher program : This and That .`

**Rule:** Closed-class function words retain their tags inside names, including independent demonstratives.
**Test:** Apply function-word classification before naming conventions.
**Examples:** title `This`: PRON; title `That`: PRON; title `Their`: DET.

## A. Unclear or inconsistently applied current rules

Strong disagreements motivate these rewrites; references below distinguish current sections from proposed rules:

- **Current 4.7/4.10:** Replace decisive “very”/“has an adverb” tests with lexical-sense and event/state tests, proposed **4.1/4.7**.
- **Current 4.8:** Replace adjacency with stranded/shared-complement and particle tests, proposed **4.6**.
- **Current 4.3/4.11:** “Participant all is PRON despite following prepositional phrases; intensifying all is ADV.” Explicitly cover `his own` and `others`.
- **Current 4.4:** Add purpose so without that; exempt discourse why from “always ADV”; retain relative when's antecedent test.
- **Current 4.6/4.12/4.13:** Replace immediate-adjacency and bare-form shortcuts with construction tests, proposed **4.11/4.13/4.17**.
- **Current 4.9:** Distinguish manner `ways that` from subject/object relatives.
- **Current 4.16/4.17:** Add citation, letter, abbreviation, attributive-name, title-function-word, and poetic-capitalization decisions, proposed **4.5/4.10/4.12/4.29**.
- **Current 4.1/4.2/4.11:** Add own-complement scope, causative have, and degree the.
- **Missing decisions:** Adopt proposed **4.22/4.25/4.28**; qualify current 4.19 for exceptive but.

## B. Probable second-annotator errors: no substantive change needed

- **Gemma alignment:** P0337/P0426/P0929 suggest consecutive shifts, including punctuation as NOUN. Repair alignment.
- **Gemma rule violations:** Infinitival to as ADP; lexical have/do as AUX; be as VERB; punctuation as SYM; French P0864 words given English tags; steel/winter modifiers as ADJ.
- **Gemma rule violations:** Standalone demonstratives as DET; subject/object relative that as SCONJ; other/such as DET.
- **Strong second annotator:** Others as NOUN, letters as X, and month-to as ADJ contradict existing conventions; add examples, not reversals.
