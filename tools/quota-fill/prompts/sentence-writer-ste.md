# Technical sentence writer instructions (STE style, wave 3 of the quota filling)

You write example sentences for specific word senses in the style of **technical instructions**: aircraft and machine
maintenance manuals, repair procedures, operating instructions, safety warnings and short technical descriptions. The
sentences train a word-sense classifier, so each one must show the intended sense clearly, and the set of sentences of
a sense must be varied.

## Input

A JSON file `{"batch": ..., "senses": [...]}`. Each entry has:

- `sense`: the sense id (copy it exactly into your output);
- `lemma`, `pos` (part of speech) and `gloss` (a one-line definition of the intended sense);
- `examples`: example uses of the sense from the dictionary, if any;
- `forms`: the allowed forms of the word. The braced target must be exactly one of these forms (lower case, or
  capitalized at the start of a sentence);
- `others`: the other senses of the same lemma (`id`, `pos`, `gloss`), so that you avoid sentences that could also fit
  them;
- `count`: how many sentences to write for this sense;
- `domain` (some entries): the technical domain to write in.

## What to write

For each entry write exactly `count` sentences in which the word is used **in the intended sense and part of speech**.

- Wrap the target word in one pair of braces, for example `{Mount} the bracket to the frame.`, exactly once per
  sentence. Only the target form is braced.
- Write in the style of simplified technical English: short, direct sentences; the imperative for a step ("Remove the
  cover.", "Tighten the nut by hand, then torque it to 12 N m."); the present tense for a description ("The seal prevents
  leakage at the joint."); `must` and `do not` for a warning or caution ("Do not touch the hot surface."); a condition
  or a check ("Make sure that the valve is closed."). Use concrete parts and quantities: pumps, valves, brackets, bolts,
  hoses, bearings, cables, filters, fluids, aircraft and engine parts, units (psi, N m, mm), part numbers, procedure
  steps.
- Mix the styles: about half imperative steps, the rest descriptions, warnings, conditions and results (for a noun:
  descriptions, lists of parts, inspection findings, maintenance notes).
- The context must make the intended sense clear to a reader who does not see the gloss, and must not fit a different
  sense in `others` as well. Do not quote or paraphrase the gloss.
- Natural, complete sentences of 6 to 25 words, US English, ordinary punctuation, one sentence per line, no markdown.
- Be varied: different systems (hydraulic, electrical, fuel, landing gear, engine, airframe, robotics, plumbing,
  automotive), different sentence structures, different positions of the target and several allowed forms when they
  exist. Do not repeat a frame with only a few words changed; do not start many sentences with the same word.
- Use no proper names as the target and no lists, definitions or headings.

## Output

Write a UTF-8 text file with one line per sentence, tab-separated, no header, no blank lines:

```text
<sense id> <sentence with the braced target>
```
