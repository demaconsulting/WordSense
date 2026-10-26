# WordSense

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

WordSense is a .NET library that identifies a word in context: its lemma, its part of speech and features and, where
the senses allow, its sense: an Open English WordNet (OEWN) fine sense with a readable id such as
`bank#financial-institution` and its WordNet sense key, or the group of closely related senses it belongs to. Every
answer states the level it was made at (sense, group or part of speech), carries a confidence for that level and lists
the candidate analyses with their probabilities, so you know whether "bank" is the money kind, at least a financial
word, or only a noun. Every word is looked up in an embedded dictionary of over 140,000 common words. Only where a word
has several analyses does a model choose from the sentence: the `Basic` model, which is part of the core package (a
small statistical tagger that needs nothing else, no download and no ONNX Runtime, and answers with the part of speech
and, for a verb whose past and non-past share a spelling, its tense),
or an ONNX encoder package (`MiniLM` or `BertBaseCased`) that runs on ONNX Runtime and scores the fine senses of the
words it was trained for. In this version the encoders know 856 lemmas (the senses appendix lists them); every other
ambiguous word is answered by the `Basic` model at the part-of-speech level, and more words follow in later versions.

## Features

- 📖 **Dictionary** - Parts of speech for over 140,000 common words, with the senses of each lemma
- 🧠 **Context Aware** - Picks the analysis (lemma, part of speech, features) of a word from its sentence
- 🏷️ **Fine Senses and Groups** - Open English WordNet senses with readable ids, WordNet sense keys and coarse groups
- 🎚️ **Answer Levels** - Each answer says whether it is a sense, a group or a part of speech
- 📊 **Confidence** - Every answer carries a confidence from 0 to 1 that applies to its level
- 🔑 **Stable Keys** - `ClassifyResult.Key` is one string per answer (`bass|bass/Noun/Number=Sing#sea-bass`), and
  `WordLexicon.Keys()` lists every key the library can return, so a downstream library can map keys to its own data
  (see "Keys" in the user guide)
- 📦 **Layered Packages** - Core with a dependency-free Basic model, optional ONNX encoder models
- 🔒 **Verified Models** - Downloads are checked against pinned SHA-256 digests
- 🌍 **Multi-Platform** - Windows, Linux and macOS; .NET 8, 9 and 10
- ✅ **Continuous Compliance** - Evidence on every CI run ([method][link-continuous-compliance])

## Installation

The core package already answers every word in context with its `Basic` model; the encoder packages add the fine senses
of the common words and bring the core package with them.

```bash
dotnet add package DemaConsulting.WordSense                # dictionary, senses and the Basic model; no download
dotnet add package DemaConsulting.WordSense.MiniLM         # small and fast encoder (22 MiB model files)
dotnet add package DemaConsulting.WordSense.BertBaseCased  # larger, case-sensitive encoder
```

| Package | Contents | Needs |
| :--- | :--- | :--- |
| `DemaConsulting.WordSense` | Dictionary (`WordLexicon`), model interface and the `Basic` statistical model | Nothing |
| `DemaConsulting.WordSense.MiniLM` | MiniLM model and its weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.BertBaseCased` | BERT base cased model and weights | Model files, downloaded on first use |
| `DemaConsulting.WordSense.Onnx` | Shared ONNX implementation (brought in by the encoder packages) | ONNX Runtime |

Requirements: .NET 8, 9 or 10 (Windows, Linux or macOS) and, for the ONNX models, internet access on first use, or a
mirror, to download the model files. Contributors who clone this repository also need [Git LFS](https://git-lfs.com).

## Quick Start

The sample below is checked by the test suite: its code is compiled, and its output is what it prints with each of the
three models. The [user guide][link-user-guide] covers the rest of the API, mirrors, limitations and troubleshooting.

<!-- sample:readme -->
```csharp
using DemaConsulting.WordSense;

// Basic is part of the core package and needs no download; MiniLM and BertBaseCased need their package and using,
// and download on first use
using var model = new BasicWordSenseModel();
// using var model = new MiniLMWordSenseModel();         // using DemaConsulting.WordSense.MiniLM;
// using var model = new BertBaseCasedWordSenseModel();  // using DemaConsulting.WordSense.BertBaseCased;

// Classify the words of a sentence whose "bow" and "record" are heteronyms and whose "go", "back" and "home"
// are among the trained words of the encoder models
var words = "The hunter fired an arrow from his bow at a deer so we will record a record then go back home".Split(' ');
var results = await model.ClassifyAsync(words);

// Print the part of speech (or unknown), the level, the confidence, the sense id (at the sense level) or the
// group id (at the group level) and the runner-up part of speech with its probability, when the word has one
// (the analyses that share a part of speech, such as the singular and the plural of "deer", share its probability)
foreach (var result in results)
{
    var pos = result.Details?.PartOfSpeech.ToString() ?? "unknown";
    var sense = result.SenseId ?? result.GroupId ?? "";
    var next = result.Analyses
        .Where(a => a.Details.PartOfSpeech != result.Details?.PartOfSpeech)
        .GroupBy(a => a.Details.PartOfSpeech)
        .Select(g => (Pos: g.Key, Probability: g.Sum(a => a.Probability)))
        .OrderByDescending(g => g.Probability)
        .Select(g => $"{g.Pos} {g.Probability:F2}")
        .FirstOrDefault("");
    Console.WriteLine($"{result.Word,-9} {pos,-12} {result.Level,-12} {result.Confidence,4:F2}  {sense,-14} {next}".TrimEnd());
}
```

Output with the `Basic` model:

<!-- output:basic -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51                 Conjunction 0.48
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 1.00                 Verb 0.00
then      Adverb       PartOfSpeech 1.00                 Noun 0.00
go        Verb         PartOfSpeech 1.00                 Noun 0.00
back      Adverb       PartOfSpeech 0.99                 Noun 0.01
home      Adverb       PartOfSpeech 0.51                 Noun 0.48
```

Output with the `MiniLM` model:

<!-- output:minilm -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51                 Conjunction 0.48
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 1.00                 Noun 0.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.99                 Verb 0.01
then      Adverb       PartOfSpeech 1.00                 Noun 0.00
go        Verb         PartOfSpeech 1.00                 Noun 0.00
back      Adverb       Sense        1.00  back#to-former-place Noun 0.00
home      Noun         PartOfSpeech 0.53                 Adverb 0.46
```

Output with the `BertBaseCased` model:

<!-- output:bertbasecased -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00                 Adjective 0.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00                 Verb 0.00
at        Preposition  Sense        1.00  at#preposition Noun 0.00
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51                 Conjunction 0.48
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary Noun 0.00
record    Verb         PartOfSpeech 0.98                 Noun 0.02
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.91                 Verb 0.09
then      Adverb       PartOfSpeech 1.00                 Noun 0.00
go        Verb         PartOfSpeech 1.00                 Noun 0.00
back      Adverb       Sense        1.00  back#to-former-place Noun 0.00
home      Adverb       PartOfSpeech 0.79                 Noun 0.13
```

The words `bow`, `record`, `go`, `back` and `home` are trained by the encoder models in this version (the senses
appendix lists them), while `Basic` answers every word at the part-of-speech level (and a word with one sense at the
sense level). The `Level` of an answer says how precisely the model answers. `Sense` is a fine sense
(`back#to-former-place`,
with its id and sense key in the catalog, and also for a word whose lemma and part of speech have only one sense, such
as `the`), `Group` is a group of closely related senses (`GroupId`, for example `back#group:return`; a model answers
`back` at that level on some CPUs),
and `PartOfSpeech` is the analysis only: the model could not tell the senses apart with the precision its thresholds ask
for (the thresholds are calibrated on real text, so the encoder models answer most ambiguous words at this level: MiniLM
and BertBaseCased answer `bow`, `record`, `go` and `home` at that level, and both answer `back` at the sense level,
`back#to-former-place`), and the
ranked
senses of each analysis in `Analyses` still show how the
evidence spreads. `Analyses` lists every possible analysis (lemma, part of speech and features) with its probability,
`Details` is the best analysis of the most probable lemma and part of speech, and a low confidence marks a guess (for
example `so`). The senses belong to a lemma and a part of speech, not to its feature variants: `deer` is a singular
and a plural of one noun, so its two analyses share the probability of the noun and the confidence is 1.00, not split
in two.
The probabilities are
comparable only within one result, never across models. The numbers are approximate across machines: the int8 encoder
arithmetic differs slightly between CPUs and operating systems, so the confidences move a little, and a word whose
margin lies close to its threshold can be answered one level more or less precisely (BertBaseCased answers `back` at
the sense level on one CPU and at its group `back#group:return` on another). A model never answers an untrained word at
the sense level
unless its lemma and part of speech have a single sense; it ranks the senses of the word by how often they are used
(the `Senses` of its analyses, and the label of its `Key`), which is never an answer. The modal and helping verbs
(`will`, `can`, `is`, `have`) have
an `Auxiliary` analysis. A word that is not in the dictionary has `IsKnown` false, `Details` of `null`, the level
`None` and a confidence of 0.
Tokenize the text yourself, and classify one sentence per call where you can: punctuation is a token of its own, and a
call may hold several sentences (Basic splits them at `.`, `!` and `?`; an encoder reads the whole list as one text).
See the
[user guide][link-user-guide] for tokenizing, confidence thresholds, mirrors, disposal and threading.

### Preparing a model up front (optional)

`Basic` needs no files, so it has no preparation step. The encoder models (`MiniLM` and `BertBaseCased`) fetch and
verify their model files the first time a word needs them. To control *when* that happens (at startup, before going
offline, through a mirror) and to handle the failures up front, call `EnsureModelAsync`; it is optional, because
`ClassifyAsync` ensures the files lazily. Where you hold the concrete model type, call it directly:

<!-- sample:download -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

// The concrete model type has the method: call it directly. It is optional, because ClassifyAsync ensures the
// files lazily; use it to control when the download happens (startup, before going offline) and to handle
// failures up front (ModelUnavailableException, ModelIntegrityException, DownloadConfigurationException)
using var model = new MiniLMWordSenseModel();
await model.EnsureModelAsync();

// Through a mirror, when the public download host is blocked: pass the options to the first ensure of the model,
// because a model that has its files does not fetch them again
using var mirrored = new MiniLMWordSenseModel();
await mirrored.EnsureModelAsync(new DownloadOptions(new Uri("https://models.example.com/wordsense")));
```

`EnsureModelAsync` is not a member of `IWordSenseModel`: that interface only classifies and disposes. It belongs to the
capability interface `IDownloadableWordSenseModel`, which the encoder models and `FallbackWordSenseModel` implement and
`Basic` does not. Code that holds only an `IWordSenseModel` (for example when the model is chosen at run time) asks for
the capability:

<!-- sample:capability -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

// Code that holds only an IWordSenseModel (Basic, MiniLM, BertBaseCased or your own) asks for the capability:
// Basic has no files and does not offer EnsureModelAsync
using IWordSenseModel model = new MiniLMWordSenseModel();
if (model is IDownloadableWordSenseModel downloadable)
{
    await downloadable.EnsureModelAsync();
}
```

The failures of the model files and of the download settings are typed: `ModelUnavailableException` (no source delivered
a file, or a file is missing from the directory you supplied), `ModelIntegrityException` (a file fails its SHA-256, or an
embedded resource is damaged) and `DownloadConfigurationException` (an invalid mirror, credential, timeout or
`WORDSENSE_MIRROR_*` setting). All derive from the abstract `WordSenseException` and keep the original exception as
`InnerException`.

## Choosing a model

<!-- markdownlint-disable MD013 -->

<!-- model-table:begin -->
| Model | Real-text sense (share @ precision) | Running-text part of speech (open corpus test) | UD-EWT test part of speech (convention-mapped) | Independent sense evaluation (SemCor) | STE challenge set (part of speech) | Download | Weights / table | RAM | Latency per sentence | First use (model + word table) | Download needed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Basic | 3.75% @ 78.89% | 94.53% (ambiguous 92.64%) | 92.28% (ambiguous 89.61%) | 3.00% @ 73.84% (top-1 56.37%) | 98.65% (ambiguous 97.83%) | none | 237 KiB | 4 MiB | 0.11 ms | 34 ms + 58 ms | no |
| MiniLM | 7.93% @ 96.32% | 94.62% (ambiguous 92.79%) | 92.66% (ambiguous 90.19%) | 8.31% @ 89.01% (top-1 58.67%) | 98.65% (ambiguous 97.83%) | 22 MiB | 5,225 KiB | 98 MiB | 1.52 ms | 443 ms + 60 ms | yes |
| BERT base cased | 27.12% @ 94.46% | 94.67% (ambiguous 92.86%) | 92.62% (ambiguous 90.13%) | 26.85% @ 86.70% (top-1 64.99%) | 98.20% (ambiguous 97.10%) | 104 MiB | 10,203 KiB | 221 MiB | 8.08 ms | 684 ms + 56 ms | yes |
<!-- model-table:end -->

<!-- markdownlint-enable MD013 -->

**Which model?** The part of speech is the dependable part of an answer, and it is the same quality in all three models
(94.5 to 94.7% of the tokens of running text, the open corpus test split), so choose by what else you need:

- ⚡ **Basic** (core package, no download, 0.11 ms a sentence): the part of speech of every word, the tense of a verb
  whose past and non-past share a spelling (`put`, `cut`, `read`: "I will read that tomorrow" against "I have read that
  already", for all three models), the sense of a
  word that has one, and for every other word with several senses a ranking of them by how often they are used (a base
  rate of the lemma, never an answer). Use it when the part of speech is all you need, and as the fallback of the
  other two.
- 🎯 **BertBaseCased** (104 MiB download, about 8 ms a sentence): the model for **fine senses**. For the 856 trained
  lemmas it commits to a fine sense on 27.1% of the real test sentences (26.9% of the SemCor words, the SemCor subset of
  825 of those lemmas) and is right in 94.5%
  (86.7% on SemCor) of those answers; its most probable sense is right for 65.0% of the SemCor words, a little more
  often than always choosing the first sense in Open English WordNet order (60.7%, an optimistic baseline: see the SemCor
  caveat in [Accuracy and Model Choice](docs/user_guide/accuracy.md)).
- ⚖️ **MiniLM** (22 MiB download, about 1.5 ms a sentence): mostly part of speech and groups. It commits to a fine
  sense on 7.9% of the real test sentences (96.3% right) and its most probable sense is right for 58.7% of the
  SemCor words, below the first-sense baseline; choose it for the small download and the speed, not for senses.

**What the columns measure.**

- *Real-text sense (share @ precision)*: of the 2,397 real held-out sentences of the *test documents* (Project
  Gutenberg, VOA, USGS, NPS and NWS text, and Tatoeba sentences under CC0; a half of the real held-out sentences that is
  split off by document and that no weight or threshold has seen), the share that the model answers at the sense level
  and the share of those answers that are right. The held-out sentences were found or written for specific senses, so
  this is a classifier measure on a balanced corpus; the written held-out sentences are easier and are not in the table.
- *Running-text part of speech (open corpus test)*: the part of speech of the selected analysis on the 1,501 sentences of
  the test split of the representative open corpus (random CC0 text, 21,482 scored tokens), on all tokens and, in
  brackets, on the 12,808 tokens whose word has two or more parts of speech in the dictionary.
- *UD-EWT test part of speech*: the same on the test file of the English EWT treebank (19,468 scored tokens), mapped to
  the conventions of the dictionary (possessive determiners, the copula *be*, phrasal-verb particles, prepositions
  before gerunds); the treebank is downloaded for evaluation only and is never committed. A possessive of a numeral
  such as `80's` is not answered, because numerals are unknown.
- *Independent sense evaluation (SemCor)*: the 31,556 single-word annotations of the 825 of the 856 trained lemmas that
  SemCor 3.0 can evaluate (the
  sense-annotated Brown Corpus, WordNet 3.0 keys mapped to the senses of the catalog; 97.1% of the keys map): the share
  answered at the sense level and the precision of those answers, and the *fine top-1*, the share of the words whose most
  probable sense is right. For comparison, always choosing the first (most frequent) sense of the right lemma and part of
  speech is right for 60.7% of these words; that baseline is optimistic because the order of the senses comes from
  WordNet counts that include SemCor itself. On all 184,323 mapped SemCor words of a trained lemma or of an untrained
  lemma with two or more senses (17.1% of them are of a trained lemma), the fine top-1 is 60.4% for BertBaseCased,
  59.3% for MiniLM and 59.0% for
  Basic, against 69.7% for the tag-count first sense (an optimistic baseline for the same reason): the models rank the
  senses of the lemmas they were not trained for by the usage prior (the WordNet 3.0 tag counts, which include SemCor, so
  that figure, and every SemCor figure for the word prior of the tagger, which reads the same counts, is circular on
  SemCor; see
  [Accuracy and Model Choice][link-accuracy]). The independent measurement of the usage prior is the *all-words gold
  set*: 397 tokens of untrained polysemous lemmas from the test split of the open corpus, labeled blind by two language
  models (91.7% agreement), where the fine top-1 of the usage ranking is 65.5% for all three models, against 68.5% for
  the first sense told the right lemma and part of speech.
- *STE challenge set*: the 32 hard sentences of simplified technical English of the open corpus, scored like the running
  text; it is small and chosen for its hard cases, so read it as a regression check and not as accuracy.

The sense-level answers are calibrated on real text towards a precision of 95%: BertBaseCased reaches 94.5% on the real
test sentences and MiniLM 96.3% (the lemma-level target 0.97 is reached for BertBaseCased but not for MiniLM, whose
thresholds then keep a share of the calibration
answers and the builder reports the target as not reached; see
[Accuracy and Model Choice][link-accuracy]), and on
SemCor, which is very unlike the training sentences, the answers are right in 86.7% (BERT) and 89.0% (MiniLM) of the
cases, and the encoder models commit to a fine sense for a minority of the words (26.9% and 8.3% of the SemCor
words). The probabilities of the fine senses
are corrected to the usage of the senses (WordNet 3.0 counts);
when this correction was introduced it raised the fine top-1 on SemCor from 56.9% to 60.7% (BERT) and from 47.5% to
53.6% (MiniLM) and lowered it slightly
on the real test sentences, which are balanced by sense (see [Accuracy and Model Choice][link-accuracy]). The sense
choice of the encoders is not yet clearly better than the first-sense baseline for the words they cover (BERT 65.0%,
MiniLM 58.7%); the part of speech is the
dependable part of an answer today. The RAM, latency and first-use columns were measured on one idle development
machine (an Intel Core i7-13850HX laptop, Windows 11, .NET 10; the conditions and the spread of repeated runs are in
[Accuracy and Model Choice][link-accuracy]), so they are machine dependent: read them as the order of magnitude and the
ratio between the models. The levels of the
encoder answers on the real test sentences (the sets that `evaluate` reports are listed in the same chapter):

| Model | Sense (share, precision) | Group (share, precision) | Part of speech (share, precision) | Fine top-1 |
| --- | ---: | ---: | ---: | ---: |
| MiniLM | 7.9%, 96.3% | 1.2%, 96.4% | 90.9%, 96.6% | 62.5% |
| BERT base cased | 27.1%, 94.5% | 0.8%, 88.9% | 72.1%, 97.3% | 72.2% |

The table is generated into [docs/user_guide/model-table.md][link-model-table]; the user guide chapter
[Accuracy and Model Choice][link-accuracy] explains how the numbers were measured and how to read them. A reviewed suite
of 364 sentences about common ambiguous words and same-spelling verbs (`corpus/suite`) guards each model against regressions.

## Planned Improvements

This version is a foundation, and these follow-ups are planned (the figures are from the corpus reports):

- **More fine-sense training data.** Fill the per-sense quotas beyond the top 200 lemmas (waves 1 to 3 covered lemmas
  1 to 200, and waves 4c, 4u and 4r the short senses of the common, uncommon and rare tiers), and refill the senses
  that lost sentences when the
  Tatoeba CC BY rows were removed from the corpus: at
  present 6,198 of the 7,035 senses of the trained lemmas meet their quota, 748 are short and 89 have no sentence at
  all (3,174 anchors and 791 held-out sentences are missing, see `WordSense.Builder tiers`).
- **More fine senses on real text at the 0.97 target for MiniLM.** MiniLM answers 7.9% of the real
  test sentences at the sense level (BertBaseCased 27.1%); more and better-balanced data, and tuning the
  calibration (for example per domain), should raise both.
- **A second check of the annotations.** Cross-check the open corpus with the second annotator `gemma4` (36% of its tokens
  are still `unchecked`, about 4,500 sentences) and take a human-reviewed gold sample of the open corpus. (The real-text
  rows of the fine-sense corpus have been checked blind; see `corpus/README.md`.)
- **Multiword and technical-name spans.** An API for spans such as "Theta Arm" or "bleed air" instead of one-token
  words.
- **More genres for the tagger.** Add informal web-genre data (with a license that allows it) to the open part-of-speech
  corpus.
- **A broader technical (STE) domain.** The senses still missing from the catalog or the corpus are listed in the
  method of the quota filling in `corpus/README.md`: bleed air, `torque` as a sense, a lock as a mechanism, a moment arm,
  a ground roll and aerodynamically clean.
- **Participial adjectives.** A policy for the 105 participial adjective entries that are left without an adjective
  analysis (only 9 have it), with a relabeling of the corpus rows that name them.
- **A stricter suite.** Of the 216 suite rows that name fine senses, 176 have a one-sense label and 40 accept a set of
  senses: 13 accept the two senses that a third reviewer judged equally right, and 27 still accept the full set because
  two blind labelers disagreed, were unsure or chose a sense outside the set. A human adjudication of those 27 rows
  would complete it (see `corpus/suite/README.md`).
- **Formal review follow-ups.** The first formal review of all 33 review-sets left compliance items open: split the
  compound requirements into atomic ones and move requirements that prescribe the implementation into the design
  documents; add typed method contracts to the unit designs; label the Arrange, Act and Assert steps in the tests and
  link system and subsystem requirements to tests of the same level; add pack-and-inspect tests for the package metadata
  of all four packages; add direct tests for the catalog-wide invariants; model the
  builder as a subsystem with its own units and the OTS items in SysML2; automate the reproducibility check of the real
  weights and use the full digest as the inventory version.

## Documentation

- **User Guide**: usage, configuration, limitations, licensing and troubleshooting ([user guide][link-user-guide])
- **Senses appendix**: every supported lemma with its senses, groups and OEWN keys, the keys, and the heteronyms by
  their keys ([appendix][link-appendix])
- **Design, Verification and Requirements**: generated from `docs/`

## Credits and licenses

The packages that embed derived data (`DemaConsulting.WordSense`, `DemaConsulting.WordSense.MiniLM` and
`DemaConsulting.WordSense.BertBaseCased`) carry this section in their package
readme. The corpus itself is not shipped in the packages, only the derived tables and weights; every sentence has a
row in `corpus/provenance/` (its id, split, source and source document), and the license evidence and the URLs of each
source are in `corpus/README.md` ("Sources").

Package licenses:

- **`DemaConsulting.WordSense`** (core): `MIT AND CC-BY-4.0`. The code and the project-authored word data are MIT; the
  embedded word image is derived from Open English WordNet 2025 (CC BY 4.0), the Moby Part-of-Speech List (public
  domain), the inflected forms that the project authored (CC0 1.0) and the WordNet 3.0 sense frequency counts (the
  WordNet license, a permissive license that requires its notice on every copy); the embedded statistical table of the `Basic`
  model is derived from our own CC0 1.0 corpora (the open part-of-speech corpus and the sentence corpus below). The
  package contains a `NOTICE.md` with the attribution of Open English WordNet, the Princeton WordNet notice that Open
  English WordNet requires, the WordNet 3.0 notice of the sense frequency counts, and the Moby notice. There is no
  CC BY-SA material in any package. The former
  `DemaConsulting.WordSense.Basic` package is retired (before version 1.0): its model, `Basic`, moved into the core
  package.
- **`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`**: `MIT AND CC-BY-4.0`. The weights
  are statistical summaries of the corpus and do not contain its sentences, but they hold the sense ids of Open English
  WordNet (CC BY 4.0), and they carry a sense usage prior derived from the WordNet 3.0 sense counts (added to the stored
  biases). Each package contains a `NOTICE.md` with the Open English WordNet attribution and the WordNet 3.0 notice. The
  encoder model files are not in the packages; they are downloaded from this repository under the Apache License 2.0
  (`model/<model-id>/LICENSE`).
- **`DemaConsulting.WordSense.Onnx`**: `MIT`.

- **Moby Part-of-Speech List** (Grady Ward): public domain; the embedded dictionary is derived from it.
- **Project-authored inflected forms** (`corpus/lexicon/forms-authored.tsv`): the irregular and the regular forms of
  the lexicon were authored by the project with language models, recorded with their provenance and checked by a
  sampled audit; they are dedicated to the public domain under CC0 1.0 by the project owner. No third-party inflection
  database is used.
- **Project-authored words, senses and meanings**: MIT License.
- **Project-authored sentences and annotations** (the sentences written by language models for this project, our
  labels, the selection of the real sentences and the open part-of-speech corpus `corpus/open`): dedicated to the public
  domain under CC0 1.0 by the project owner. The underlying real sentences are not ours; their sources and licenses
  are below.
- **Tatoeba sentences** (<https://tatoeba.org>): only the sentences of the official CC0 1.0 export are used. Tatoeba
  sentences that are licensed CC BY are not used anywhere.
- **Project Gutenberg works** (<https://www.gutenberg.org>): public domain in the United States (published 1930 or
  earlier; the Project Gutenberg boilerplate and trademark are removed).
- **Works of US federal agencies** (Voice of America Learning English staff-written articles, USGS, National Park
  Service, NOAA National Weather Service and FAA publications): public domain in the United States (17 U.S.C. §105)
  and by the policy statement of each agency; third-party passages are excluded.
- **Universal Dependencies English-EWT treebank** (<https://github.com/UniversalDependencies/UD_English-EWT>, CC BY-SA
  4.0): used to evaluate the part-of-speech tagger only. It is downloaded to a git-ignored cache, never committed or
  redistributed, and no model, table or corpus here is trained from it.
- **Wikipedia and Wiktionary text**: not used.
- **sentence-transformers/all-MiniLM-L6-v2** (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>): Apache
  License 2.0; the int8 ONNX export is redistributed under the same license.
- **bert-base-cased** (Google, <https://huggingface.co/google-bert/bert-base-cased>): Apache License 2.0; the int8 ONNX
  export is redistributed under the same license.
- **ONNX Runtime** (Microsoft Corporation): MIT License; installed as a separate NuGet dependency.
- **Open English WordNet 2025** (<https://en-word.net>, <https://github.com/globalwordnet/english-wordnet>): CC BY 4.0.
  The word data under `data/` (entries, senses, synsets, glosses, examples, relations and pronunciations) is imported
  from it and keeps that license; see `data/README.md` and `data/PROVENANCE.md`. The sense ids, sense keys, lemmas and
  forms of the word image that the core package embeds are derived from it (changed: imported, extended and compiled);
  the entries, glosses and examples are not in the image. Open English WordNet is itself derived from **Princeton
  WordNet** and carries its notice (WordNet 3.1 Copyright 2011 by Princeton University, all rights reserved), which is
  reproduced in the `NOTICE.md` of the core package.
- **WordNet 3.0** (Princeton University, <https://wordnet.princeton.edu>): the sense frequency counts of the word data
  (`data/sense_frequency`, from the WordNet 3.0 sense index `index.sense`) are used under the WordNet license: WordNet 3.0
  Copyright 2006 by Princeton University, all rights reserved; the full notice is in `data/PROVENANCE.md`. They are
  used for the usage prior that corrects the scores of the encoder weights, for the usage prior that the Basic model
  ranks the senses of the lemmas no model scores by and, summed over the lemmas of a spelling, for the word prior of the
  Basic part-of-speech tagger (the counts are embedded in the word image of the core package; the
  order of the senses in the word image is the order of Open English WordNet), so the `NOTICE.md` of the core package and
  of the MiniLM and BertBaseCased packages reproduces the notice.
- **SemCor 3.0** (the sense-annotated Brown Corpus text of the WordNet project): an evaluation source only. It is
  downloaded to a cache by the builder to measure the models, never committed, redistributed or used for training.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process. The maintainer tools in `tools/` (the builder, the Python scripts for the
corpus, the forms and the model preparation) are described in [tools/README.md][link-tools].

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/WordSense?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/WordSense?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/WordSense?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/WordSense?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/WordSense/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_WordSense&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_WordSense&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.WordSense?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/WordSense/network/members
[link-stars]: https://github.com/demaconsulting/WordSense/stargazers
[link-contributors]: https://github.com/demaconsulting/WordSense/graphs/contributors
[link-license]: https://github.com/demaconsulting/WordSense/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/WordSense/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_WordSense
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_WordSense
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.WordSense
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/WordSense/blob/main/CONTRIBUTING.md
[link-user-guide]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/introduction.md
[link-appendix]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/appendix-senses.md
[link-accuracy]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/accuracy.md
[link-model-table]: https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/model-table.md
[link-tools]: https://github.com/demaconsulting/WordSense/blob/main/tools/README.md
