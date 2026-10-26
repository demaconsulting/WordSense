# Sense group reviewer instructions (blind review of the proposed groups)

You judge proposed groups of senses. You see only the glosses and the proposed groups, nothing else.

## Input

A JSON file `{"batch": ..., "units": [...]}`. Each unit is one lemma and one part of speech with **all** its senses
(`id` and the full `gloss`) and the proposed `groups` of that unit: `group` (a number), `members` (sense ids of the unit)
and `kind`.

## What to do

For each proposed group decide whether its members belong together. Read the full gloss of every member and of the other
senses of the unit, and decide each group on its own. The proposals come from a generous proposer: expect that a large
part of them (a third or more) is wrong, and some are clearly wrong. A group that merely names a shared topic is wrong.

- `equivalent`: the members are effectively **one meaning**. You could swap one gloss for another without changing what
  a sentence says, and a careful reader given a sentence could not say which gloss it uses (near-identical glosses, or
  glosses that restate each other).
- `related`: the members are a genuine **family** of close meanings: the same core idea applied to close objects or
  situations, still distinguishable, but easily mixed up with one another.
- Reject the group when:
  - one member has a different kind of meaning from the others (another kind of subject or object, a literal against a
    figurative or technical use, a different result, a different register such as slang against plain use);
  - the glosses only share a word, a topic or a part of the wording, and a reader could tell the members apart with ease;
  - the members are all the senses of the unit, so that the group says nothing beyond the part of speech.

Answer `accept` only when you would put the members in one group yourself after reading the glosses, and every member
fits. When the proposed `kind` is too strong (the members are related but not equivalent), accept with the kind
`related`. When one member does not fit but the rest do, answer `reject` and name the member that does not fit in the
note. When in doubt, reject.

## Output

Write a UTF-8 text file with one line per proposed group, in the order of the input, tab-separated, no header, no blank
lines:

```text
<lemma> <pos> <group number> <accept|reject> <equivalent|related|none> <short note without tabs>
```

For a rejected group the kind is `none`. In the note, say what the members share (accept) or how they differ (reject).
