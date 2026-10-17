# API Documentation Quiz

This note is a repeatable check that the generated API documentation of the five packages is enough to use the library
without reading the source. An isolated reader gets only the generated API documents and answers eight questions; every
answer that is missing, vague or contradictory is a gap that is fixed in the XML documentation, the namespace
documentation, the README or the user guide. The check is repeated until every question is PASS.

## Procedure

1. Delete the generated API documents of all packages: `src/*/generated/api` (untracked build output).
2. Build the solution once in Release configuration, so that the documentation generator rewrites the API documents.
3. Copy the generated API documents of all five packages (`DemaConsulting.WordSense`, `.Basic`, `.Onnx`, `.MiniLM` and
   `.BertBaseCased`) to a fresh temporary folder, for example `%TEMP%\ws-docs-rN`. Copy nothing else: no source, no
   README, no user guide, no build notes.
4. Start an isolated agent that can read only that folder, give it the questions below and ask it to answer each one
   from the documents alone, quoting the document that supports the answer, and to mark the answer PASS (complete,
   specific and consistent) or GAP (missing, vague or contradictory, with what is missing).
5. Fix every GAP in the documentation (verify the behavior in the code first; never document a guess), and repeat from
   step 1 for the next round. Record the round, the date and the gaps closed in the log below.

## Questions

1. **Classify end to end.** With each of the three models (Basic, MiniLM, BertBaseCased), show how to classify a
   tokenized sentence: which packages to install, `EnsureModelAsync` (with the default download, with a mirror, and
   with an offline model directory, including the layout of that directory and whether `EnsureModelAsync` is still
   required), `Create`, `ClassifyAsync`, and how to read `Details` and `Confidence`.
2. **Look up candidates.** How do you list the possible senses of a word such as `read` with `WordLexicon.Words`? What
   are the keys (case, apostrophes), what happens for a missing word, and what do the entries contain?
3. **Choose a model.** Which model should be used when, with the measured accuracy, download size, latency and memory,
   a default recommendation, and when Basic is enough?
4. **Threshold the confidence.** What does `Confidence` mean for each model, which values are special (0 and 1), what
   threshold is sensible, and may a threshold be reused across models?
5. **Unknown words and unresolved ambiguity.** When is `Details` null and when is it a best sense with a low
   confidence? Which lemmas have no weights, if any? Give an example of each case.
6. **Thread safety, disposal and first use.** Which objects are thread-safe, what must be disposed and when, what
   happens after disposal (which exception), and what does the first use cost (lexicon, Basic table, encoder session)?
7. **Offline use and failures.** How is the library used offline (model directory, mirror), where are files stored and
   how is that location changed, which environment variables exist (and does one control the store), which files are
   verified against SHA-256, and which exceptions can `EnsureModelAsync` throw and when?
8. **Tokenization.** How must the text be tokenized (punctuation, contractions such as `don't`, hyphens, curly
   apostrophes), how many sentences per call, how does Basic split sentences, and what is the maximum input length of
   the encoders and what happens beyond it?

## Log

<!-- markdownlint-disable MD013 -->

| Round | Result | Notes |
| ----- | ------ | ----- |
| 1 | Gaps in all eight questions | Closed by the commit "Close API documentation gaps found by quiz round 1" |
| 2 | Q1-Q7 PASS with minor gaps, Q8 GAP | Minor gaps: no `EnsureModelAsync(mirror)` snippet in the encoder packages, no threshold-versus-coverage table for the encoders, no generic disposal guidance, no byte sizes and mixed MB/MiB, no concrete store paths, unclear whether memory includes the lexicon, repo-relative paths in XML docs, punctuation, cancelled-token behavior of Basic, unequal latency statements, no `ModelId` values. Q8: no reference tokenizer, no edge-case table, case handling not stated. Closed by the commit "Close API documentation gaps found by quiz round 2" |

<!-- markdownlint-enable MD013 -->
