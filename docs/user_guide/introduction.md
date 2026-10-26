# Overview

WordSense is a .NET library that identifies an English word in context, offline. For each word it returns the lemma,
the part of speech with its features and, where it can tell, the sense: an Open English WordNet (OEWN) fine sense, or
the group of closely related senses it belongs to.

Every answer states its **level** (sense, group or part of speech), carries a **confidence** for that level and lists
the probability of every candidate analysis. So you know whether "bank" is the money kind, at least a financial word, or
only a noun.

Every word is looked up in an embedded word table. A word with one analysis and one sense is answered at once. A word
with several analyses is decided from its sentence by the model you choose (see "Choosing a Model").

<!-- markdownlint-disable MD051 -->
<!-- The appendix is a separate file that pandoc concatenates, so its heading anchor only exists in the PDF. -->
The [appendix](#appendix-supported-senses) lists every trained lemma with its senses, groups and OEWN keys, and the
heteronyms by their keys.
<!-- markdownlint-enable MD051 -->

# Installation

```bash
dotnet add package DemaConsulting.WordSense.MiniLM
```

| Package | Contents | Needs |
| ------- | -------- | ----- |
| `DemaConsulting.WordSense` | API, word table, sense catalog and the `Basic` model | Nothing |
| `DemaConsulting.WordSense.MiniLM` | MiniLM encoder model and weights | Model files, ONNX Runtime |
| `DemaConsulting.WordSense.BertBaseCased` | BERT base cased encoder model and weights | Model files, ONNX Runtime |
| `DemaConsulting.WordSense.Onnx` | Shared ONNX code, brought in by the encoder packages | ONNX Runtime |

A model package brings the core package. Requirements: .NET 8, 9 or 10 on Windows, Linux or macOS. An encoder model
downloads its model files on first use (see "Configuration and Model Download"). Contributors who clone the repository
also need [Git LFS](https://git-lfs.com) (`git lfs install`, then `git lfs pull`), because `model/` is stored in it.

# Quick Start

The samples in this guide assume `ImplicitUsings`, `using DemaConsulting.WordSense;` and
`using DemaConsulting.WordSense.MiniLM;`. The README has a complete sample with its output.

```csharp
// MiniLM downloads its model files on first use. For a model without a download: new BasicWordSenseModel()
using var model = new MiniLMWordSenseModel();

string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "the", "new", "album", "."];
ClassifyResult[] results = await model.ClassifyAsync(words);

foreach (var result in results.Where(r => r.IsKnown))
{
    Console.WriteLine($"{result.Word}: {result.Details!.PartOfSpeech}, {result.Level}, " +
                      $"{result.SenseId ?? result.GroupId ?? "-"} ({result.Confidence:F2})");
}
```

`ClassifyAsync` takes the tokens of one text and returns an array parallel to the input: element `i` is the result of
`words[i]`.

## Input and Tokenization

You tokenize the text. The models expect what the evaluation used:

- Punctuation is a token of its own (`"race"`, `"."`, never `"race."`). Classify one sentence per call: the list is
  read as one running text.
- Keep a contraction (`don't`), a possessive (`dog's`) and a hyphenated word (`re-read`) as one token. A contraction is
  a stored word, and a possessive is found as its stem with `Poss=Yes`. A hyphenated word is unknown, but the words
  around it are still classified. A curly apostrophe (U+2019) is read as `'`.
- A number, punctuation mark, misspelling or name that is not in the table is unknown. Merge abbreviations such as
  `Mr.` into one token: a split period ends a sentence for `Basic`.
- Case is ignored by the lookup, but `BertBaseCased` sees it. `Basic` prefers the lemma of the written case where a
  spelling has both (`Polish` and `polish`).
- `Basic` splits the list into sentences at `"."`, `"!"` and `"?"` and reads five words on each side of the target. An
  encoder encodes at most `maxSequenceLength` word pieces (128 by default, 8 to 512 allowed) from a window around the
  target.
- A null element throws `ArgumentException`. An empty string, or one that contains a space, is unknown.

A reference tokenizer (the repository has a unit test for this exact pattern):

```csharp
using System.Text.RegularExpressions;

var tokenPattern = new Regex(
    @"\d+(?:[.,]\d+)+|[\p{L}\p{Nd}\p{Mn}\p{Mc}]+(?:['\u2019-][\p{L}\p{Nd}\p{Mn}\p{Mc}]+)*|[\uD800-\uDBFF][\uDC00-\uDFFF]|\S",
    RegexOptions.CultureInvariant);

string[] Tokenize(string sentence) => tokenPattern.Matches(sentence).Select(match => match.Value).ToArray();

// Smith's | well-known | dog | didn't | " | bark | " | at | 3.5 | cats | .
string[] tokens = Tokenize("Smith's well-known dog didn't \"bark\" at 3.5 cats.");
```

# Results

## The Result

An **analysis** is one reading of a word: a `WordDetails` of lemma, part of speech and features (`Number=Plur`). A form
that belongs to several lemmas or tenses (`left`, `read`) has one analysis for each. A **sense** is one meaning of a
lemma and part of speech, with a stable id `lemma#label` such as `bank#financial-institution`. It is an OEWN fine sense
with an OEWN sense key (`bow%1:06:02::`), or a project sense without a key (`the#determiner`, `well#discourse-marker`).
A **group** is a curated set of at least two, but not all, closely related senses of one lemma and part of speech, with
an id such as `help#group:assistance`. A sense is in at most one group, and most lemmas have none.

`ClassifyResult` is a `readonly record struct` with plain properties, so it serializes without custom code:

- `Word` is the word as you gave it. `IsKnown` says whether it is in the word table.
- `Analyses` lists every analysis as an `AnalysisCandidate`, largest probability first. The probabilities sum to one and
  nothing is truncated. Analyses that differ only in features (`cut` as an infinitive and as a past tense) share the
  probability of their lemma and part of speech.
- `AnalysisCandidate` has `Details` (`Lemma`, `PartOfSpeech`, `Features`), `Probability` and `Senses`: its ranked fine
  senses, each a `SenseId` and a `Probability`, summing to one. `Senses` is empty only when the lemma and part of speech
  have no sense. `SensesRankedByUsage` is true when the senses are the usage-prior ranking of an untrained lemma rather
  than scores from the context of the word.
- `Details` is the selected analysis, the first of `Analyses`, or `null` for an unknown word.
- `Level` is `None`, `PartOfSpeech`, `Group` or `Sense`. `Confidence` is the probability of the answer at that level.
- `GroupId` is set at the `Group` level, and at the `Sense` level when the sense is in a group. `SenseId` is set at the
  `Sense` level only.
- `Key` is one stable string for the answer (see "Keys").

`Analyses` is the source of truth and the rest summarizes it. Read `Level`, `Confidence` and the ids for the model's
decision, or `Analyses` for your own. An unknown word has no entry in `WordLexicon`: `IsKnown` is false, `Analyses` is
empty, `Details` is null, `Level` is `None` and `Confidence` is 0. Every other word gets an answer, so `Level` `None`
never means low confidence.

## Levels and Confidence

The levels nest: part of speech, then the optional group, then the sense. A model that is unsure between senses steps
up to the next level that exists: their shared group, or else the part of speech.

| Level | Meaning | Ids set |
| ----- | ------- | ------- |
| `Sense` | The best fine sense is clear enough | `SenseId`, and `GroupId` when it has a group |
| `Group` | The senses are too close to tell apart, but their group is clear | `GroupId` |
| `PartOfSpeech` | Neither is clear, the lemma has no group, or no senses were scored | none |

An encoder answers a trained lemma at the finest level that passes the lemma's own threshold. The thresholds are
measured when the weights are trained (see "Accuracy and Model Choice"). Test the `Level`, not whether an id is `null`.
A `PartOfSpeech` result is a valid answer, and its `Senses` still rank the senses of the analysis.

The **confidence** is the probability of the answer at its level. The *slot* of the selected analysis is its lemma and
part of speech: the sum of the probabilities of the analyses that share them.

- `PartOfSpeech`: the probability of the slot.
- `Group`: the probability of the slot times the probability of the group's senses within the slot.
- `Sense`: the probability of the slot times the probability of the answered sense within the slot.

It is 0 for an unknown word and 1 for a word with one lemma and part of speech and one sense. It is also 1 at the
`PartOfSpeech` level for a word whose one lemma and part of speech have several senses that the model does not decide,
so test the `Level`, not whether the confidence is 1.

The confidence is a score, not a calibrated probability. Compare it only within one result, never across models, words
or levels: 0.9 at the `Group` level means something other than 0.9 at the `Sense` level. Choose a threshold per model
and level on your own text.

## Analyses and Senses

Where a model scored the senses (an encoder, for a trained lemma), each analysis lists its senses with their
probabilities at every level of answer. To map senses to your own categories, sum the product of the analysis
probability and the sense probability per category. For example `bow#archery` at 0.48 and `bow#violin` at 0.41 give 0.89
for a "things you hold and play or shoot with" category, though neither sense alone is clear enough for a `Sense`
answer. The probability that a word is a verb is the sum of `Probability` over the analyses whose
`Details.PartOfSpeech` is `Verb`.

For a lemma without trained weights, every model ranks the senses of each analysis that has two or more by how often
they are used: the WordNet 3.0 tag counts, with smoothing. The ranking is the same in every sentence. It describes the
lemma and is never an answer: the level stays `PartOfSpeech` and the ids stay empty. The probabilities are a ranking
score of one model, so do not compare them across models.

## Senses, Groups and OEWN Keys

The core package resolves the ids of an answer without a model, and finds a sense by its OEWN key:

```csharp
// The senses of "bow" as a noun, in OEWN order (common senses first), with the group of each
foreach (SenseInfo info in WordLexicon.SensesOf("bow", PartOfSpeech.Noun))
{
    Console.WriteLine($"{info.SenseId} in {info.GroupId ?? "(no group)"}, key {info.SenseKey ?? "(project sense)"}");
}

// From an answer to its sense, from a WordNet key to a sense, and from a group answer to its members
ClassifyResult answer = (await model.ClassifyAsync(["She", "drew", "the", "bow", "."]))[3];
if (answer.SenseId is { } id && WordLexicon.TryGetSense(id, out SenseInfo found))
{
    Console.WriteLine($"{found.SenseId}: group {found.GroupId ?? "(none)"}, key {found.SenseKey}");
}

WordLexicon.TryGetSenseByKey("bow%1:06:02::", out SenseInfo byKey);

if (answer.GroupId is { } groupId && WordLexicon.TryGetGroup(groupId, out SenseGroup group))
{
    Console.WriteLine(string.Join(", ", group.SenseIds));
}
```

`WordLexicon.Lookup(word)` returns the analyses of a spelling, and `TryLookup`, `Contains` and `FormsOf(lemma)` answer
related questions. The inflected forms of every dictionary word are in the table, so no morphology runs at use time.

**Stored ids.** Sense ids are frozen, so you can store them. Group ids are not: a data release can add, rename, merge or
drop a group. Treat a group id as an opaque value of one release. Store `WordLexicon.Version` with it and resolve it
again with `TryGetGroup` after the version changes. Compare `Version` for equality only.

## Keys

A key is one stable string for an answer. It names the meaning and never the sound, so a consumer that attaches its own
data to a meaning (a speech front end mapping words to phonemes, for example) can key that data on it. The format is
`{spelling}|{lemma}/{Part}[/{Features}][#{label}]`:

- `spelling` is the lower-case written form with a straight apostrophe (`polish` for `Polish`). `lemma` is the lemma of
  the selected analysis, with its case. `Part` is the `PartOfSpeech` name.
- `Features` are the features of the analysis. The `/` and the features are left out when empty (`the|the/Determiner`).
- `label` is the part after `#` of a sense id (`sea-bass` for `bass#sea-bass`), left out when the key names no sense.

At the `Sense` level the label is the answered sense. At the `Group` level, or at the `PartOfSpeech` level where the
senses are ranked, it is the most probable ranked sense. A group id is never used. For a word that no model scored, that
label is the most frequent sense of the lemma, not a decision of the context: test `Level` and `Confidence` before you
read it as a meaning. An unknown word has a `null` key. The key depends only on the fields of the result, so it is the
same for every model, your own included. A `\` escapes a `\`, `|`, `/` or `#` inside a part (`a\/c|A\/C/Noun/Number=Sing`).

```csharp
ClassifyResult answer = (await model.ClassifyAsync(["She", "caught", "a", "bass", "."]))[3];
Console.WriteLine(answer.Key);      // for example: bass|bass/Noun/Number=Sing#sea-bass

if (WordKey.TryParse(answer.Key, out WordKey? parts))
{
    Console.WriteLine($"{parts.Spelling} {parts.Lemma} {parts.PartOfSpeech} {parts.Features} {parts.Label ?? "(no sense)"}");
}
```

`WordLexicon.Keys()` enumerates every key any model can return: for every spelling and analysis the key without a label,
and one key with a label for each catalog sense of the analysis. Build a map from these keys to your own data once, then
look up `result.Key` for each answer. The committed image has 1,285,019 keys, about 63 MB of text, which stream in about
three seconds. The builder writes them as a file: `dotnet run --project tools/WordSense.Builder -c Release -- keys
--output keys.txt`. The build publishes it, with the heteronym table, as the `word-keys` artifact.

**Heteronyms** are spellings that stand for different words, so the key, not the spelling, tells them apart. The
appendix lists them at three levels. At the *entry* level the analyses belong to entries that sound different (`record`,
`August` and `august`). At the *tense* level the past and the present are spelled alike (`read`); the key tells them
apart through the features, but the encoder models do not score features, so decide the tense yourself from the
analyses when it matters. At the *sense* level the senses of one analysis sound different (`bass`, `bow`, `lead`); only
the label decides. `dotnet run --project tools/WordSense.Builder -c Release -- heteronyms --output heteronyms.tsv`
writes the table as a TSV.

# Choosing a Model

The chapter "Model Comparison Table" lists accuracy, size and speed, and "Accuracy and Model Choice" explains the
columns. The part of speech is the dependable part of an answer in all three models. Treat a sense as a candidate to
check.

- Use the **core package alone** to look up analyses and senses, resolve a sense id or key, and answer words that have
  one sense.
- Use **`Basic`** when the part of speech is enough, nothing may be downloaded, or ONNX Runtime cannot be shipped. It is
  also the fallback of the encoder models.
- Use **`MiniLM`** (uncased, 22 MiB) for a small download and fast answers. It answers mostly at the part-of-speech and
  group levels.
- Use **`BertBaseCased`** (cased, 104 MiB) for fine senses. It commits to a sense most often and ranks senses best.

The sentence of the README, answered by the encoder models. The confidences differ slightly between CPUs, and a word
near its threshold can be answered one level finer or coarser (`BertBaseCased` answers `back` at the group
`back#group:return` on some CPUs).

<!-- output:minilm -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00
at        Preposition  Sense        1.00  at#preposition
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary
record    Verb         PartOfSpeech 1.00
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.99
then      Adverb       PartOfSpeech 1.00
go        Verb         PartOfSpeech 1.00
back      Adverb       Sense        1.00  back#to-former-place
home      Noun         PartOfSpeech 0.53
```

<!-- output:bertbasecased -->
```text
The       Determiner   Sense        1.00  the#determiner
hunter    Noun         PartOfSpeech 1.00
fired     Verb         PartOfSpeech 1.00
an        Determiner   Sense        1.00  an#determiner
arrow     Noun         PartOfSpeech 1.00
from      Preposition  Sense        1.00  from#preposition
his       Determiner   Sense        1.00  his#determiner
bow       Noun         PartOfSpeech 1.00
at        Preposition  Sense        1.00  at#preposition
a         Determiner   Sense        1.00  a#determiner
deer      Noun         Sense        1.00  deer#noun
so        Adverb       PartOfSpeech 0.51
we        Pronoun      Sense        1.00  we#pronoun
will      Auxiliary    Sense        1.00  will#auxiliary
record    Verb         PartOfSpeech 0.98
a         Determiner   Sense        1.00  a#determiner
record    Noun         PartOfSpeech 0.91
then      Adverb       PartOfSpeech 1.00
go        Verb         PartOfSpeech 1.00
back      Adverb       Sense        1.00  back#to-former-place
home      Adverb       PartOfSpeech 0.79
```

## How the Models Decide

**`Basic`** needs no files, no network and no ONNX Runtime. A hidden Markov tagger over the part-of-speech statistics of
the project's open corpus chooses the analysis. Context cues separate analyses of one part of speech for the lemmas that
have enough training sentences, and a tense model reads the tense of verbs whose past and non-past share a spelling
(`put`, `read`). `Basic` names a sense only for an analysis whose lemma and part of speech have one sense. It never
answers at the `Group` level.

**The encoder models** compare the word in your sentence with example sentences of each sense, learned at build time.
Each trained lemma ships as a small classifier with confidence thresholds measured for that lemma. The lemmas are those
of the appendix, the same for both models. An analysis that belongs to several lemmas or tenses is first chosen by a
per-form classifier. The scores are corrected to the usage of the senses (the WordNet 3.0 counts), so the most probable
sense is the usual one unless the context says otherwise.

**Fallback.** `FallbackWordSenseModel(primary, fallback)` combines two models. A word gets the complete result of the
primary model when the primary scored senses for it, and otherwise the complete result of the fallback. A result is
never a mixture. MiniLM and BertBaseCased are an encoder with `Basic` as the fallback, so an ambiguous word that the
encoder was not trained for gets the `Basic` analysis at the `PartOfSpeech` level, with its senses ranked by usage. When
the primary gives every feature variant of a slot the same probability, as an encoder does, the variants take the
proportions of the fallback, which reads the tense. The constructor `FallbackWordSenseModel(primary, fallback,
primaryWeight)` takes a weight: below 1, an answer at the part-of-speech level combines the slot probabilities of both
models as `p_primary^w × p_fallback^(1−w)`. `MiniLM` uses 0.6 and `BertBaseCased` uses 1 (off).

**Your own model.** Implement `IWordSenseModel` (`ClassifyAsync` and `Dispose`) to wrap another engine. Return a non-null
array parallel to the input, `ClassifyResult.Unknown(word)` for an unknown word, and for a known word all analyses
(probabilities summing to one, largest first), the selected one in `Details` and a level that carries only its own ids.
Throw `ObjectDisposedException` after `Dispose`, and allow concurrent calls. Take analyses from `WordLexicon.Lookup` and
senses from `WordLexicon.SensesOf`, with the word lower-cased and U+2019 replaced by `'`.

# Configuration and Model Download

`Basic` and the core package have no model files. The encoder files are downloaded once to `<store>/<model-id>/`. The
default store is `%LOCALAPPDATA%\DemaConsulting\WordSense\models` on Windows and
`~/.local/share/DemaConsulting/WordSense/models` on Linux and macOS (`$XDG_DATA_HOME` is honored). Each file is
verified against its pinned SHA-256 and moved into place atomically, so a partial download is never used, and valid files
are reused without network access. Each file is requested from a mirror first, when one is configured, and then from the
GitHub LFS copy of this repository (`media.githubusercontent.com`). There is no other source.

- **MiniLM** (`all-MiniLM-L6-v2-int8`): `model_int8.onnx` (22,972,370 bytes) and `vocab.txt` (231,508).
- **BertBaseCased** (`bert-base-cased-int8`): `model_int8.onnx` (108,477,896 bytes), `vocab.txt` (213,450),
  `config.json` (639) and `tokenizer.json` (668,923).

The SHA-256 values are in `SHA256SUMS` of each model folder (`model/<model-id>/`).

## Preparing a Model

`EnsureModelAsync` is optional: `ClassifyAsync` ensures the files lazily, on the first word that needs the encoder. Call
it to control when the download happens and to handle failures up front. Concurrent callers share one ensure, and a
failed ensure can be retried.

<!-- sample:download -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

// Optional: ClassifyAsync ensures the files lazily. Call EnsureModelAsync to control when the download
// happens and to handle its failures up front.
using var model = new MiniLMWordSenseModel();
await model.EnsureModelAsync();

// Through a mirror: pass the options to the first ensure of the model
using var mirrored = new MiniLMWordSenseModel();
await mirrored.EnsureModelAsync(new DownloadOptions(new Uri("https://models.example.com/wordsense")));
```

`EnsureModelAsync` belongs to the capability interface `IDownloadableWordSenseModel`, which the encoder models and
`FallbackWordSenseModel` implement and `Basic` does not. Code that holds only an `IWordSenseModel` asks for it:

<!-- sample:capability -->
```csharp
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

// Code that holds only an IWordSenseModel asks for the capability. Basic has no files and does not offer it.
using IWordSenseModel model = new MiniLMWordSenseModel();
if (model is IDownloadableWordSenseModel downloadable)
{
    await downloadable.EnsureModelAsync();
}
```

## Offline Use

Copy the model files into one flat folder (from `model/<model-id>/`, or from a machine that has run `EnsureModelAsync`)
and pass it to the constructor: `new MiniLMWordSenseModel(modelDirectory: @"C:\models\all-MiniLM-L6-v2-int8")`. The
files are verified against their SHA-256 without the network, and options and environment variables are ignored. You can
also pre-populate the store: files copied to `<StoreRoot>/<model-id>/` that match their SHA-256 are kept and never
requested.

## Mirrors and Options

A mirror serves `{BaseUri}/{model-id}/{file-name}` for every file of the model. It is tried before GitHub. A file that
fails its SHA-256 is discarded and the next source is tried. Pass a `DownloadOptions` to `EnsureModelAsync`:

```csharp
var mirror = new DownloadOptions(
    BaseUri: new Uri("https://models.example.com/wordsense"),
    BearerToken: "token",
    Credentials: null,        // an ICredentials for HTTP Basic
    StoreRoot: null,          // where the model folders are stored; null selects the default store
    Timeout: TimeSpan.FromMinutes(2));   // per file; the default is 5 minutes
await model.EnsureModelAsync(mirror);
```

Without an argument, the mirror comes from environment variables: `WORDSENSE_MIRROR_URL` (the base URI; the others are
used only when it is set), `WORDSENSE_MIRROR_USER` and `WORDSENSE_MIRROR_PASSWORD` (HTTP Basic) and
`WORDSENSE_MIRROR_BEARER_TOKEN` (exclusive with user and password).

- The base URI must be absolute `http` or `https`, without query, fragment or user-info.
- A bearer token or credential is sent only to the mirror host, never to a redirect target on another host, and is
  rejected for plain `http` unless the host is a loopback address.
- A timeout of zero, negative or longer than about 49 days is rejected. `Timeout.InfiniteTimeSpan` means no limit.
- Only `DownloadOptions.StoreRoot` sets the store; prefer an absolute path. A `DownloadOptions` always needs a `BaseUri`.
- Downloads use `HttpClient` with the default .NET proxy behavior. Allow-list your mirror host and
  `media.githubusercontent.com`. Redirects are followed (at most 10 hops), so their targets must be reachable too.

## Lifetime, Threads and Cancellation

- Models are `IDisposable`: dispose them with `using`. Disposal never deletes the downloaded files and cancels a running
  download. After `Dispose`, `ClassifyAsync` and `EnsureModelAsync` throw `ObjectDisposedException`: `Basic` from the
  call, an encoder through the returned task, so put the `await` inside the `try`.
- `ClassifyAsync` is safe for concurrent use. Calls share one ONNX session. The encoder models run on the CPU only.
- `Basic` runs synchronously. An encoder runs inference on the thread that continues the call: offload it yourself if
  the caller must not block.
- A cancellation token cancels the wait of that call only. The shared ensure keeps running until it finishes or the model
  is disposed. A single ONNX run is never interrupted.

## Handling Failures

The library reports three typed exceptions, all derived from the abstract `WordSenseException`. The original exception
is the `InnerException`. They come from `EnsureModelAsync`, from the lazy ensure of `ClassifyAsync` and, for the embedded
data, from `WordLexicon` and `Basic`.

<!-- markdownlint-disable MD013 -->

| Exception | When | What to do |
| --------- | ---- | ---------- |
| `ModelUnavailableException` | The supplied model directory is missing or lacks a file, or every download source failed (the inner `HttpRequestException` lists them) | Check the network, proxy and mirror, or supply a model directory. A retry is possible |
| `ModelIntegrityException` | A file fails its SHA-256, an embedded resource is missing, corrupt or mismatched, or a model file cannot be used | Replace the files, or reinstall and keep all package versions equal. Retrying does not help |
| `DownloadConfigurationException` | The `DownloadOptions` or the `WORDSENSE_MIRROR_*` settings are invalid. The inner exception names the setting | Fix the setting. It is not thrown when a model directory is supplied |

<!-- markdownlint-enable MD013 -->

`DownloadConfigurationException` is not an `ArgumentException`. Other exceptions are not wrapped: `IOException` or
`UnauthorizedAccessException` when the store cannot be written, `ObjectDisposedException`, `OperationCanceledException`,
`ArgumentNullException`, `ArgumentException` and `ArgumentOutOfRangeException` (for example `maxSequenceLength`).

# Limitations

- **The word table decides what is known.** A word that `WordLexicon.Lookup` does not find is unknown: numbers written
  with digits, most names, dialect spellings and archaic verb endings.
- **Only some ambiguous words are decided by an encoder.** The encoders choose from context for the lemmas with trained
  weights (see the appendix). Any other ambiguous word, such as `need`, `look` or `help`, is answered at the
  part-of-speech level by `Basic`, with its senses ranked by usage.
- **`Basic` decides the part of speech from the words next to a word and from word statistics, not from the whole
  sentence** (a hidden Markov model with context cues). A clue further away is out of its reach.
- **Its known weak spots** are a noun use of a word that is mostly a verb after a modifier noun (`security check`,
  `monitor stand`, `dial travel`: on the development splits 9 of 23 such nouns were right, against 303 of 324 verb uses
  of the same words), short fragments without a determiner (`Wash pump`) and a gerund that opens a sentence.
- **When these matter, use `MiniLM` or `BertBaseCased`.** For the lemmas they are trained for (see the appendix) they
  read the whole sentence and decide the part of speech better; every other word keeps the part of speech of `Basic`.
- **Treat the part of speech of `Basic` as reliable at a confidence of 0.8 or more, or when an encoder model agrees.**
  Below that, check the alternatives in `Analyses`. The weak spots above can be wrong with high confidence, so check
  them whatever the confidence.
- **Fine senses are WordNet's.** Even a trained model can often tell only the group.
- **Group ids change between releases.** Sense ids are frozen.
- **There is no pronunciation data.** Map the sense id, group id or key to your own pronunciations.
- **English only.** The encoder models need files and the ONNX Runtime native libraries, and run on the CPU only.

# Licensing and Attribution

WordSense is licensed under the MIT License. The core, MiniLM and BertBaseCased packages embed derived data and are
`MIT AND CC-BY-4.0`. The Onnx package is `MIT`. Each package that embeds derived data carries a `NOTICE.md` with these
credits. No package contains CC BY-SA material. The corpus is not shipped, and the weights are statistical summaries of
it that do not contain its sentences. This is the project's reading and not legal advice.

- **Open English WordNet 2025** (<https://en-word.net>, CC BY 4.0): the word data under `data/` is imported from it and
  keeps that license, and the sense ids, sense keys, lemmas and forms of the word image are derived from it. It carries
  the notice of Princeton WordNet (WordNet 3.1 Copyright 2011 by Princeton University, all rights reserved), reproduced
  in the `NOTICE.md` of the core package. See `data/PROVENANCE.md`.
- **WordNet 3.0** (Princeton University): the sense frequency counts are used under the WordNet license, for the
  training quotas, the usage correction of the encoder weights, the usage ranking and the word prior of `Basic`. The
  notice is in `data/PROVENANCE.md` and in the `NOTICE.md` of the three data packages.
- **Moby Part-of-Speech II** (Grady Ward, <https://www.gutenberg.org/ebooks/3203>): public domain. See
  `corpus/lexicon/PROVENANCE.md`.
- **Project-authored data**: the inflected forms, written sentences, annotations and the open part-of-speech corpus
  `corpus/open` are CC0 1.0. The sense labels, groups and project senses are MIT.
- **Real sentences**: only Tatoeba sentences of the official CC0 export, Project Gutenberg books published 1930 or
  earlier, and works of US federal agencies (VOA Learning English, USGS, NPS, NOAA National Weather Service and FAA;
  public domain under 17 U.S.C. §105). Every sentence has a row in `corpus/provenance/` that names its source. The
  evidence is in `corpus/README.md` ("Sources"). No CC BY, CC BY-SA, Wikipedia or Wiktionary text is used.
- **Evaluation only**: SemCor 3.0 (the WordNet license applies) and the Universal Dependencies English-EWT treebank (CC
  BY-SA 4.0) are downloaded to a cache to measure the models, and never committed, redistributed or trained on.
- **Models**: `all-MiniLM-L6-v2` and `bert-base-cased` are under the Apache License 2.0, and the int8 ONNX exports are
  redistributed under it (`model/<model-id>/LICENSE`). ONNX Runtime (MIT) is a separate NuGet dependency.

# Troubleshooting

**The model cannot be downloaded, or is slow.** Offline or behind a firewall, configure a mirror or pass a model
directory. For timeouts, raise `DownloadOptions.Timeout` or use a mirror close to the host.

**A model file does not match its SHA-256.** The file is stale, truncated, modified or a Git LFS pointer: replace it. A
mirror must serve the exact pinned files; the upstream `onnx/model_int8.onnx` of bert-base-cased is a different file.

**The model files are Git LFS pointers.** In a clone made without Git LFS, the files in `model/` are short text files
that start with `version https://git-lfs`, and the tests that need the models skip (they fail when
`WORDSENSE_REQUIRE_MODELS` is `true`, as in CI). Run `git lfs install` and `git lfs pull`.

**A word returns nothing.** `IsKnown` is false for a word that is not in the table. Check
`WordLexicon.Contains(word.ToLowerInvariant())`.

**A word has no sense id.** That is not an error: the level is `PartOfSpeech`. The lemma has no trained weights for that
model, or the model does not decide senses (`Basic`). A `Group` result has a group id and no sense id: the member senses
are in `WordLexicon.TryGetGroup`.

**A stored group id is not found.** Group ids are versioned per data release. Compare the saved `WordLexicon.Version` with
the current one and map the stored ids again. Sense ids keep resolving.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] sentence-transformers/all-MiniLM-L6-v2 (<https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>)
- [REF-3] bert-base-cased (<https://huggingface.co/bert-base-cased>)
