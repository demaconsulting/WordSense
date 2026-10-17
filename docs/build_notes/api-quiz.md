# API Documentation Quiz

This note is a repeatable check that the generated API documentation of the five packages is enough to use the library
without reading the source. An isolated reader gets only the generated API documents and answers eleven questions; every
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
   required, or whether `ClassifyAsync` ensures lazily), `ClassifyAsync`, and how to read `Details` and `Confidence`.
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
9. **Disposal and ownership.** Who disposes a model, and is it the same for every model (Basic included) without
   casting? What happens when the model is disposed while
   `EnsureModelAsync` is downloading (which exception, any leftover files)? What happens after disposal to
   `EnsureModelAsync` and `ClassifyAsync` (which exception, for which words), and is
   disposing twice or from several threads safe?
10. **Laziness and lazy ensure.** Must `EnsureModelAsync` be called before `ClassifyAsync`? When does `ClassifyAsync`
    ensure the model by itself, which mirror settings does it use, is a model that is never needed ever downloaded,
    do lexicon-only words touch the model files or ONNX, do concurrent callers share one ensure, and can a failed
    ensure be retried?
11. **Public contract and third-party implementers.** What exactly does `IWordSenseModel` promise (argument validation,
    result shape, `Confidence` values, exceptions, thread safety, disposal while a call is running, idempotent
    disposal)? May someone else implement the interface, and what must such an implementation honor so that callers
    cannot tell it from the packaged models?

## Log

<!-- markdownlint-disable MD013 -->

| Round | Result | Notes |
| ----- | ------ | ----- |
| 1 | Gaps in all eight questions | Closed by the commit "Close API documentation gaps found by quiz round 1" |
| 2 | Q1-Q7 PASS with minor gaps, Q8 GAP | Minor gaps: no `EnsureModelAsync(mirror)` snippet in the encoder packages, no threshold-versus-coverage table for the encoders, no generic disposal guidance, no byte sizes and mixed MB/MiB, no concrete store paths, unclear whether memory includes the lexicon, repo-relative paths in XML docs, punctuation, cancelled-token behavior of Basic, unequal latency statements, no `ModelId` values. Q8: no reference tokenizer, no edge-case table, case handling not stated. Closed by the commit "Close API documentation gaps found by quiz round 2" |
| 3 | Q9 and Q10 added by later changes; gaps in the new disposal, ownership and laziness questions, and a set of consistency gaps | Real gaps verified against the code and fixed: unified the offline-directory wording (`EnsureModelAsync` is optional, `ClassifyAsync` verifies lazily and may throw `FileNotFoundException` or `InvalidDataException`); documented that a `MirrorOptions` passed during an in-flight ensure is ignored, that the ensured state (including the `StoreRoot` of a successful call) is per instance, the exceptions of `ClassifyAsync` (`ArgumentException` for invalid environment mirror settings, `InvalidDataException` for a SHA mismatch in a supplied directory), the reference tokenizer limitation for abbreviations (`Mr.` is split; merge it yourself), disposal (race with a running classification, synchronous versus faulted-task exceptions, files kept, precedence over a cancelled token), hashing times (about 30 ms and 110 ms), parallel inference on the shared session and CPU-only execution, model-specific thresholds (Basic never below 1/n), network notes (proxy, hosts, shared store, replacement of bad files), and wording (model contract, example through `IWordSenseModel`, absolute link, per-lemma classifier weights versus encoder files, numbers). Closed by the commit "Close API documentation gaps from quiz round 3; final verification". The planned rounds are exhausted and the quiz was not run again; repeat it in a future change |
| 4 (verification pass, round 1) | GAP in Q4, Q6, Q7, Q9 and Q11 (Q11 newly added); the other questions PASS | Verified against the code and fixed: one disposal contract for every model (marked disposed at once, running calls complete, resources released after the last one); one recommended starting threshold per model (0.8) with the meaning of 0.5 for the encoders and corrected error rates (about 1 in 14, 50 and 100); the core namespace no longer calls the options and result types contracts; an "Implementing the interface" section; environment variable rules (user without password, credentials or token without URL, ignored when a model directory is supplied, invalid `Timeout`); synchronous versus offloaded work and what cancellation interrupts; cosmetic wording (punctuation row, hashing once per instance, wait cancellation, the import list of the tokenizer snippet, empty and spaced elements, the meanings of `read`). Closed by the commit "Address final verification quiz round 1 and remaining review findings"; the quiz has not been repeated since |

<!-- markdownlint-enable MD013 -->
