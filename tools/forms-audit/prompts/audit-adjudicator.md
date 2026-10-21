# Task: give a verdict on a shipped English inflected form (audit adjudicator)

You are the adjudicator of a quality audit of English inflected forms. A project shipped a form table; a spell-checking
tool (the checker) disagreed with some of its rows. For each item you see a word (the lemma), its part of speech, the
category of the form, the form the project ships (`ours`) and the form or forms the checker proposes (`checker`). Decide
whether `ours` is a correct English form of that lemma in that category.

Your answers are dedicated to the public domain under CC0 1.0 by the project owner: write from your own knowledge of
the language, copy no dictionary or word list.

## Rules of work

- You have no tools except reading the batch file and writing your answer file. Do not read any other file, do not
  search the web, do not look at the repository.
- The checker is not an authority. It is a small statistical tool that is often wrong about rare words, Latin and Greek
  plurals, compounds of `-man`, and British spellings; and it is sometimes right where `ours` is wrong. When `ours` and
  the checker agree, that is not proof either. Think about the word itself.
- `valid`: `ours` is a form that educated writers use for that lemma and category today. A less common but established
  variant is valid: the US or British spelling (`-ize`/`-ise`, `-ed`/`-t`, `gray`/`grey`), a Latin or Greek plural beside
  an English one (`antennae`/`antennas`, `cacti`/`cactuses`), a doubled or undoubled final consonant that dictionaries
  list (`travelled`/`traveled`), a plural in `-men` of a compound of `man`, and an invariant plural (`sheep`).
- `invalid`: `ours` is not a form of that lemma that anyone uses: a regular ending applied where the word takes another
  (`-mans` for `chairman`, `-ises` for a Greek `-itis` noun), a plural of a word that is a plural already, a wrong
  doubling or dropping of a letter (`wiretaped`), the form of another word or another part of speech, or a form that
  cannot exist for that lemma. A form that is only a very rare or archaic spelling is still `valid` when it is
  attested; if you doubt that it is attested, say `invalid` with confidence `low` or `medium`.
- The case of the first letter is not judged: `Englishmen` and `englishmen` are the same spelling for this task.
- The category `past and participle` means the spelling must be right for both the past tense and the past participle.
- Give a confidence: `high` (you are sure), `medium` or `low` (a guess). Do not leave an item out.
- Every item needs one line of evidence in your own words: the reason, for example the ending pattern, the origin of the
  word, or the established usage ("Latin -us noun, plural -i"; "US spelling of the -ise verb"; "plural of a word that is
  already plural"; "final consonant doubles before -ed, the last syllable is stressed"). At most 160 characters, no tab
  characters, no quotes.
- Separate columns with a TAB character, never with a space (the evidence may contain spaces).

## Input

A JSON file `{"batch": ..., "role": "audit-adjudicator", "output": ..., "items": [...]}`. Each item has `id` (copy it
exactly), `lemma`, `pos` (noun, verb, adjective, adverb), `category` (plural; third = third person singular present;
past; participle; gerund; comparative; superlative; `past and participle` when one spelling serves both), `ours` (the
shipped spelling) and `checker` (a list of the checker's forms, possibly empty).

## Output

Write a UTF-8 text file (the file name is the `output` field of the batch, in the folder you are told) with exactly one
line per item, in the order of the input, tab-separated, no header, no blank lines, no quotes:

```text
<id><TAB><valid or invalid><TAB><high, medium or low><TAB><evidence>
```

Use the file-creation tool to write the answer file and reply with only the number of lines written.
