# Shape-fit judge instructions (noun/verb lemmas)

You decide, for the noun and verb senses of a lemma, in which sentence shapes a sense can **naturally** be used. The
answer says where example sentences for the sense can be written; a shape is listed for a sense only when ordinary
writers use that sense in that shape. Do not force a shape onto a sense: leave it out when it would sound strained.

## Input

A JSON file `{"batch": ..., "shapes": {...}, "lemmas": [...]}`.

- `shapes` names every shape with its part of speech (`pos`), a `title` and a `rule` with an example for another word.
- Each lemma has `senses`: `id`, `pos` (Noun or Verb), `tags` (how often the sense is used; higher is more frequent)
  and a one-line `gloss`.

## What to do

For every sense, list the shapes **of its own part of speech** (the `n-` shapes for a Noun sense, the `v-` shapes for a
Verb sense) in which the sense can be used in a natural sentence:

- `n-obj-light`: the noun can be the object of do, make, run, perform, take, give or have (`make a repair`,
  `do a survey`, `take a break`, `run a test`). Typical for events, actions, results, acts. Not for a concrete object
  (`a table`).
- `n-compound-head`: the noun can follow a modifier noun (`service station`, `fire drill`, `river crossing`). Most nouns
  fit.
- `n-modifier`: the noun can modify another noun (`repair kit`, `survey results`, `parking lot`). Typical for
  activities, materials, places, measures. Not for an abstract noun that never modifies.
- `n-subject-initial`: the noun can begin a sentence with no article as its subject (`Parking is limited`,
  `Repair takes time`). Typical for mass nouns, abstract nouns and plurals. Not for a count noun that needs an article in
  the singular.
- `n-measure`: the noun can be measured or counted with a number or a unit (`Maximum load is 50 kg`,
  `a 20 minute break`). Typical for amounts, distances, durations, sizes, rates, counts. Not for an abstract noun that has
  no amount.
- `v-imperative-initial`: the verb can begin an instruction (`Close the valve.`, `Wash in cold water.`). Typical for
  actions that a person can do on purpose. Not for a verb that does not take an instruction (`seem`, `resemble`).
- `v-after-modal`: the verb can follow a modal or `to` (`will close`, `to wash`). Almost every verb sense does.

Judge from the gloss, the kind of thing it names and ordinary usage. If you are not sure that a shape is natural for the
sense, leave it out.

## Output

Write a UTF-8 text file with one line for **every sense** of the input, in the order of the input, tab-separated, no
header, no blank lines:

```text
<sense id> <shape ids separated by commas, or - when none>
```

Copy the sense ids exactly. Write the file with the file-creating tool (never with a shell redirection), then reply with
only: `done <number of lines>`.
