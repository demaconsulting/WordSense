# Technical senses instructions (wave 3 of the quota filling)

You decide which senses of some common words are used in **technical instructions**: aircraft and machine maintenance
manuals, repair procedures, operating instructions and safety warnings written in plain, controlled English (for
example "Mount the bracket to the frame.", "Torque the bolts to 25 N m.", "Bleed the air from the brake line.", "Check
the oil level.", "Install the seal in the pump.").

## Input

A JSON file `{"batch": ..., "lemmas": [...]}`. Each lemma has `senses`: `id` (the public id `lemma#label`), `pos`,
`tags` (how often the sense is tagged in a sense-tagged corpus) and `gloss`.

## What to do

1. For each lemma pick the senses that a maintenance, engineering or operating text uses: the mechanical, procedural
   or measuring meaning (an arm of a robot or a lever, a seal that closes a joint, to mount an item on a frame, a bearing
   that supports a shaft, to bleed air from a system, to run a pump, to check or inspect an item, a lead that connects a
   circuit, to flush a system with fluid, to tap a thread or a housing, to lock a part, to clean a surface, to support a
   load, to position a part, to install, remove or attach an item, to inspect). Give each picked sense a number of
   **extra anchors** that a corpus should add to the usual quota: 14 for a sense that technical text uses very often (the
   core meaning of the instruction), 8 for a sense that it uses sometimes. Pick at most 4 senses per lemma and skip a
   sense that technical text does not use (a person's arm, a seal animal, a lead role in a play).
2. For each lemma also report a **gap** when a common meaning of the technical use has no sense in the list (for example
   a noun `mount` as a fixture, `bleed` as releasing air, a verb `torque`): write the lemma, the part of speech and a
   short description of the missing meaning with one example.

## Output

Write a UTF-8 text file, tab-separated, no header, no blank lines, one line per picked sense and one line per gap, in
this form:

```text
<sense id from the input> <14 or 8> <one short note on the technical use>
GAP <lemma> <part of speech> <missing meaning and one example>
```

Copy the sense id exactly from the input. A gap line starts with the word `GAP`. Write no other lines.
