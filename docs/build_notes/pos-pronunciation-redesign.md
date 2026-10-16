# Part of Speech and Pronunciation Redesign

This note records the change from a tag-based answer to an answer of part of speech and pronunciation. The earlier
build notes describe the tag-based phases and are kept as the history of how the corpus was built.

## Before and After

Before, an answer was a sense name with tags and a group. The caller used a sense query, a detail query, a tag query
and `IsTag`, and mapped (lemma, group) to a pronunciation itself. The inventory had the columns `lemma`, `sense`,
`tags` and `group`, the builder ran the part-of-speech audit and `apply-relabel`, and the table was format version 3.

After, an answer is the lemma, a part of speech and, for a heteronym, a pronunciation. One `ClassifyAsync` always
returns the full analysis (margin, runner-up, abstained), and `GetCandidates` lists the possible answers. The
inventory has the columns `lemma`, `label`, `pos`, `meaning` and `respelling`; the label is an internal join key.
A pronunciation is a meaning and a plain respelling such as `REK-urd` and `rih-KORD`. The builder command
`validate-pos` checks the inventory rules, the commands `audit-*` and `apply-relabel` are retired, and the table is
format version 4.

A *lemma* is the dictionary base form of a word (`record` for `records`); a *heteronym* is a lemma whose meanings are
pronounced differently. A lemma that is not a heteronym has no pronunciation in its answers.

## Accuracy

The corpus, the encoder and the classifiers are unchanged, so the best-sense accuracy and the real-text accuracy do
not move; the figures below are the same holdout measured at the new levels.

- Best-sense accuracy (holdout): 96.91 %, unchanged.
- Meaning-level accuracy: 97.20 % (it was 97.08 % at the group level).
- Part of speech: 97.22 % for the best sense, and 98.91 % with abstention at 90.74 % coverage.
- Pronunciation meaning, over the rows of heteronym lemmas: 96.25 %.
- Real-text evaluation: 92.60 %, unchanged.

## Table Size

The embedded table grew from 1,299,772 bytes (format version 3) to 1,307,640 bytes (format version 4), which now
include the part of speech, meaning and respelling of every sense.
