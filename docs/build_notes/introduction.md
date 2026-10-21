# Introduction

This document contains the build notes for the WordSense project.

## Purpose

This report serves as a comprehensive record of changes and bug fixes for this
release of WordSense. It provides transparency about what has changed since the
previous version and helps users understand the improvements and fixes included
in this build.

## Scope

This build notes report covers:

- Version information and commit details
- Changes and new features implemented
- Bugs fixed in this release

## Changes Before Version 1.0

This section is the user-facing summary of what changed since the last published release. The package layout change is
described first, then the new meaning-based behavior, the models, the data and licensing, and finally what breaks and
how to migrate. The measured figures are in the README model table and in the build notes that follow.

### What WordSense Does Now

The library no longer assigns a pronunciation or part-of-speech class to a word; it identifies the **meaning** of a word
in context. An answer carries the lemma, the part of speech and features and, where the model can tell, an Open English
WordNet (OEWN) fine sense with a readable id such as `bank#financial-institution`, linked to its WordNet sense key, or
the group of closely related senses it belongs to (`help#group:assistance`). Every answer states its level (sense,
group or part of speech) and carries a confidence for that level.

- **Fine senses and groups.** About 129,000 senses (the single-word OEWN senses and the project senses, the closed-class
  uses and the discourse senses that WordNet lacks) form the sense catalog; 140 curated groups collect the senses that
  are easy to confuse.
  The sense ids are readable (`lemma#label`), stable and never reused.
- **A new result shape.** The result type `WordSense` is now `ClassifyResult`. It lists every analysis of the word with
  its probability and, inside each analysis, the ranked fine senses with their probability (`Analyses`, with
  `AnalysisCandidate.Senses`); `Level` (`SenseLevel`: sense, group, part of speech or none), `Confidence` (the
  probability of the answer at its level), `SenseId`, `GroupId`, `Details` and `IsKnown` summarize it.
- **Catalog lookups.** `WordLexicon.SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup` and `Version` look up
  the senses of a lemma, a sense by its id or its OEWN sense key, a group and the version of the inventory.
- **Calibrated levels.** Each trained lemma has its own thresholds for the sense level and the group level, chosen by
  cross-validation and then calibrated on real held-out text of other documents so that the answers at a level are
  right about as often as promised (the target is 97% for the lemma-level classifier, about 95% for the whole pipeline
  on real test text). A word that the model cannot settle is answered at the next level that exists above it.
- **Usage-corrected probabilities.** The per-sense probabilities of the encoder models are corrected from the balanced
  training sentences to the usage of the senses with a tempered prior built from the WordNet 3.0 counts (see
  `frequency-prior.md`). On SemCor, human-annotated running text, the share of words whose most probable sense is right
  rose from 47.5% to 53.8% (MiniLM) and from 56.9% to 60.9% (BertBaseCased).

### Models and Packages

- The statistical `DemaConsulting.WordSense.Basic` package is retired; its model, `BasicWordSenseModel`, is part of the
  core package `DemaConsulting.WordSense` and needs no download and no ONNX Runtime (see "Package Changes Before
  Version 1.0" below).
- `DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased` score the fine senses of 775 trained
  lemmas with weights embedded in the package and answer every other word through the Basic model
  (`FallbackWordSenseModel`).
- The part of speech is the dependable part of an answer: about 94% on running text (the open corpus test split) for all
  three models. BertBaseCased commits to fine senses where it is sure (15.7% of the real test sentences at 94.2%
  precision); MiniLM mostly answers part of speech or group (3.1% at 87.7%).

### Data and Licensing

- The Basic tagger is trained from our own CC0 open part-of-speech corpus (`corpus/open`), and the encoder weights from
  the project-authored CC0 sentence corpus and from real sentences of US public domain sources (Project Gutenberg books
  published 1930 or earlier, works of US federal agencies) and the CC0 Tatoeba export. The Universal Dependencies
  treebank, SemCor and the Wikipedia and Wiktionary text are not training inputs; the treebank and SemCor are used for
  evaluation only and are never committed.
- The core package is `MIT AND CC-BY-4.0` (the embedded word image derives from Open English WordNet 2025, CC BY 4.0,
  with the WordNet 3.0 sense counts under the Princeton license); `Onnx`, `MiniLM` and `BertBaseCased` are `MIT`. **No
  CC BY-SA material is shipped in any package.**

### Breaking Changes and Migration

- **`WordSense` is now `ClassifyResult`**, and `IWordSenseModel.ClassifyAsync` returns `Task<ClassifyResult[]>`. Rename
  the type; the name `WordSense` stays the name of the project, the namespace and the packages.
- **`WordDetails.SenseId` is gone.** An analysis is a lemma, a part of speech and features; the sense is in
  `ClassifyResult.SenseId` next to `GroupId` and `Level`. Replace a test for a non-null sense id by a test of the level.
- **The sense ids changed, and no mapping from the old ids is provided.** The previous release used pronunciation or
  part-of-speech class ids (`lead#metal`, `read#past`); the new ids are fine senses (`lemma#label`). Group ids are
  versioned per data release: save `WordLexicon.Version` with a stored group id.
- **Confidence refers to the answered level.** Re-tune any threshold you apply, and test `Level` first.
- **`WordLexicon.Lookup` returns each analysis once**; use `WordLexicon.SensesOf(lemma, partOfSpeech)` to list the
  senses of an analysis.
- **Words that an encoder model was not trained for** are answered at the part of speech level by the Basic model, where
  the previous release answered their first dictionary sense.
- **Reference `DemaConsulting.WordSense` instead of `DemaConsulting.WordSense.Basic`**, and drop
  `using DemaConsulting.WordSense.Basic;`.

## Package Changes Before Version 1.0

The statistical `DemaConsulting.WordSense.Basic` package is retired. Its model, `BasicWordSenseModel`, is now part of
the core package `DemaConsulting.WordSense` (namespace `DemaConsulting.WordSense`; `using DemaConsulting.WordSense.Basic;`
is no longer needed), so the core package answers every word in context at the part of speech level with no download
and no ONNX Runtime. The `DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased` packages
depend on
the core and Onnx packages only. The statistics of the model are trained from the CC0 open part-of-speech corpus of this
repository (no Universal Dependencies treebank), and the core package license expression remains `MIT AND CC-BY-4.0`
(Open English WordNet); no package contains CC BY-SA material. A project that referenced
`DemaConsulting.WordSense.Basic` references `DemaConsulting.WordSense` instead. No version of the Basic package was ever
released as 1.0 or later.

## Generation Source

This report is automatically generated by the BuildMark tool, analyzing the
Git repository history and issue tracking information. It serves as evidence of
changes and improvements included in this release.

## Audience

This document is intended for:

- Software developers working on WordSense
- Users evaluating what has changed in this release
- Project stakeholders tracking progress
- Contributors understanding recent changes
