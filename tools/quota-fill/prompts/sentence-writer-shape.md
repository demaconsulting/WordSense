# Sentence writer instructions, shape rows (noun/verb lemmas)

You write example sentences for a specific word sense **in a specific sentence shape**. The sentences train a word-sense
classifier that must tell a noun use from a verb use of the same spelling (`check`, `call`, `travel`), so the shape of every
sentence matters as much as its meaning.

## Input

A JSON file `{"batch": ..., "senses": [...]}`. Each entry has:

- `sense`: the sense id (copy it exactly into your output);
- `lemma`, `pos` (part of speech) and `gloss` (a one-line definition of the intended sense);
- `examples`: example uses of the sense from the dictionary, if any;
- `forms`: the allowed forms of the word. The braced target must be exactly one of these forms (lower case, or capitalized
  at the start of a sentence);
- `others`: the other senses of the same lemma (`id`, `pos`, `gloss`), so that you avoid sentences that could also fit them;
- `shape` and `shape_title`: the sentence shape to write;
- `shape_rule`: what the shape means, with an example for another word;
- `count`: how many sentences to write for this sense in this shape.

## The shapes

- `n-obj-light`: a noun that is the **object of a light verb** (do, make, run, perform, take, give, have) with a determiner
  and an optional modifier between the verb and the noun: `Do a quick {survey} before you leave.`
- `n-compound-head`: a noun that is the **head of a noun compound**, a noun that modifies it stands directly before it:
  `The traffic {survey} took a week.`
- `n-modifier`: a noun that **modifies the noun that follows it**: `The {survey} results were posted online.`
- `n-subject-initial`: a noun with no article that is the **first word of the sentence** and its subject, followed by
  its verb: `{Surveys} are mailed every spring.`
- `n-measure`: a noun in a **measurement or quantity frame**, the sentence has a number (and a unit where it fits):
  `Total {survey} cost is 1,200 dollars.`
- `v-imperative-initial`: a verb in the base form that is the **first word of an instruction**: `Survey the field
  before you plant.`
- `v-after-modal`: a verb in the base form that follows a **modal** (can, could, may, might, must, shall, should, will,
  would) or the infinitive marker **to**: `We will {survey} the land next week.`

## What to write

For each entry write exactly `count` sentences in which the word is used **in the intended sense, the intended part of
speech and the named shape**.

- Wrap the target word in one pair of braces, exactly once per sentence; only the target is braced. The target must be
  one of `forms`.
- The shape must be unmistakable: follow the rule of the shape literally. A sentence that does not have the shape is
  thrown away by a check.
- The context must make the intended sense clear to a reader who does not see the gloss, and must not fit another sense
  in `others`. Do not quote or paraphrase the gloss.
- Natural, complete sentences of 6 to 20 words, US English, ordinary punctuation, one sentence per line. The first word
  of a sentence is capitalized, so a braced first word is written `{Check}`.
- Be varied: different topics (everyday talk, news, science, business, home, technical text), different words around
  the target, different sentence lengths. Do not repeat a frame with only a few words changed. When two entries ask for
  the same shape of the same word, make their sentences different in topic as well.
- Real-world style, not textbook style. No lists, definitions, headings or quotations of the shape rule.

## Output

Write a UTF-8 text file with one line per sentence, three tab-separated columns, no header, no blank lines:

```text
<sense id> <shape> <sentence with the braced target>
```

Copy the sense id and the shape exactly from the entry. Write the entries in the order of the input, all lines of an entry
together. Write the file with the file-creating tool (never with a shell redirection), then reply with only:
`done <number of lines>`.
