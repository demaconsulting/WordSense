using System.Globalization;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The evaluation commands of the builder that measure every model on independent text: <c>evaluate-senses</c>,
///     <c>evaluate-running</c>, <c>evaluate-suite</c>, <c>check-leakage</c> and <c>tiers</c>.
/// </summary>
internal static partial class Program
{
    private static async Task<int> RunEvaluateSensesAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var cache = options.TryGetValue("cache", out var c) ? Path.GetFullPath(c) : SemCor.CacheDirectory(repository);
        var archive = Path.Join(cache, SemCor.ArchiveName);
        if (!File.Exists(archive))
        {
            if (!IsOn(options, "download"))
            {
                throw new InvalidDataException($"SemCor 3.0 is not in the cache '{cache}'. Give --download to fetch {SemCor.ArchiveUrl} (the pinned SHA-256 is verified; the archive is used for evaluation only and is never committed), or place {SemCor.ArchiveName} in the cache.");
            }

            using var client = new HttpClient();
            await SemCor.DownloadAsync(client, new Uri(SemCor.ArchiveUrl), archive, SemCor.ArchiveSha256, cancellationToken).ConfigureAwait(false);
        }

        var (sentences, counts) = SemCor.ReadArchive(archive, SemCor.ArchiveSha256);
        var trained = Corpus.ReadLemmas(Path.Join(corpus, "anchors"));
        var sample = IntOption(options, "sample", 1, 1);
        var data = options.TryGetValue("data", out var dataFolder) ? Path.GetFullPath(dataFolder) : Path.Join(repository, "data");
        var known = new HashSet<string>(LeakageCheck.Load(corpus, data).SelectMany(s => s.Sentences.Select(x => Corpus.Normalize(x.Sentence))), StringComparer.Ordinal);
        var (selected, funnel) = SenseEvaluator.Select(sentences, counts, trained, sample, known);
        var tagCounts = Tiers.ReadTagCounts(data);
        var measured = new List<SenseEvalModelResult>();
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
            measured.Add(await SenseEvaluator.MeasureAsync(ModelTable.DisplayName(info.Name), model, selected, output, cancellationToken, tagCounts).ConfigureAwait(false));
        }

        var result = new SenseEvalResult(funnel, measured);
        var text = result.Render();
        output.Write(text);
        WriteReport(options, text);
        if (options.TryGetValue("table", out var table))
        {
            foreach (var (info, m) in ModelCatalog.Select(options.GetValueOrDefault("model")).Zip(measured))
            {
                ModelTable.Update(table, info.Name, new Dictionary<string, string> { [ModelTable.IndependentSenseColumn] = SenseEvalResult.TableCell(m) });
            }
        }

        return 0;
    }

    private static async Task<int> RunEvaluateRunningAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var gold = options.TryGetValue("gold", out var g) ? Path.GetFullPath(g) : RunningTextEvaluator.DefaultGold(repository);
        var report = options.GetValueOrDefault("report", "all");
        if (report is not ("all" or "running" or "tense"))
        {
            throw new InvalidDataException($"--report must be all, running or tense, not '{report}'.");
        }

        // Each report loads only its own inputs, so a missing input of the other report does not stop it
        var (sets, notes) = report is "all" or "running"
            ? RunningTextEvaluator.Load(corpus, options.GetValueOrDefault("split", "test"), gold)
            : ([], []);
        var (tenseSets, tenseNotes) = report is "all" or "tense"
            ? TenseEvaluator.Load(corpus, gold)
            : ([], []);
        var scoresByModel = new List<(string Model, List<TaggerScore> Scores)>();
        var tenseByModel = new List<(string Model, List<TenseScore> Scores)>();
        var outcomes = new List<(string Model, TokenOutcome Outcome)>();
        var latencies = new List<(string Model, double Median, double Mean, double P95, int Sentences)>();
        var wantOutcomes = options.ContainsKey("outcomes") && (report is "all" or "running");
        var documents = wantOutcomes ? RunningTextEvaluator.Documents(corpus, options.GetValueOrDefault("split", "test")) : null;
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
            var display = ModelTable.DisplayName(info.Name);
            if (report is "all" or "running")
            {
                var modelOutcomes = new List<TokenOutcome>();
                var scores = await RunningTextEvaluator.RunAsync(model, sets, cancellationToken, wantOutcomes ? modelOutcomes : null, documents).ConfigureAwait(false);
                outcomes.AddRange(modelOutcomes.Select(o => (info.Name, o)));
                scoresByModel.Add((display, scores));
                output.WriteLine($"{info.Name}: running text done.");
                if (options.TryGetValue("table", out var table))
                {
                    ModelTable.Update(table, info.Name, RunningTextEvaluator.TableCells(scores));
                }

                if (IsOn(options, "latency"))
                {
                    var open = sets.First(s => s.Name == RunningTextEvaluator.OpenSetName).Sentences;
                    var (median, mean, p95, timed) = await RunningTextEvaluator.MeasureLatencyAsync(model, open, IntOption(options, "latency-sentences", 1000, 1), IntOption(options, "seed", OutcomeComparer.DefaultSeed), cancellationToken).ConfigureAwait(false);
                    latencies.Add((display, median, mean, p95, timed));
                    output.WriteLine($"{info.Name}: latency done.");
                }
            }

            if (report is "all" or "tense")
            {
                tenseByModel.Add((display, await TenseEvaluator.RunAsync(model, tenseSets, cancellationToken).ConfigureAwait(false)));
                output.WriteLine($"{info.Name}: tense done.");
            }
        }

        var text = report is "tense" ? string.Empty : RunningTextEvaluator.Render(scoresByModel, notes, options.GetValueOrDefault("split", "test"));
        if (report is not "running")
        {
            text += (text.Length == 0 ? string.Empty : "\n") + TenseEvaluator.Render(tenseByModel, tenseNotes);
        }

        if (latencies.Count > 0)
        {
            text += "\n" + RunningTextEvaluator.RenderLatency(latencies, options.GetValueOrDefault("split", "test"));
        }

        output.Write(text);
        WriteReport(options, text);
        if (wantOutcomes)
        {
            File.WriteAllText(options["outcomes"], OutcomeFile.Render(outcomes), new System.Text.UTF8Encoding(false));
            output.WriteLine($"Wrote {outcomes.Count.ToString("N0", CultureInfo.InvariantCulture)} token outcomes to {options["outcomes"]}.");
        }

        return 0;
    }

    private static int RunCompareOutcomes(Dictionary<string, string> options, TextWriter output)
    {
        var baseFile = Path.GetFullPath(Require(options, "base"));
        var newFile = Path.GetFullPath(Require(options, "new"));
        var cluster = options.GetValueOrDefault("cluster", "sentence");
        var replicates = IntOption(options, "reps", OutcomeComparer.DefaultReplicates, 1);
        var seed = IntOption(options, "seed", OutcomeComparer.DefaultSeed);
        var baseOutcomes = SelectModel(OutcomeFile.Read(baseFile), options.GetValueOrDefault("base-model"), baseFile);
        var newOutcomes = SelectModel(OutcomeFile.Read(newFile), options.GetValueOrDefault("new-model"), newFile);
        var compared = OutcomeComparer.Compare(baseOutcomes, newOutcomes, cluster, replicates, seed);
        var text = OutcomeComparer.Render(compared, Path.GetFileName(baseFile) + SelectedName(options, "base-model"), Path.GetFileName(newFile) + SelectedName(options, "new-model"));
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }

    private static string SelectedName(Dictionary<string, string> options, string name) => options.TryGetValue(name, out var model) ? " (" + model + ")" : string.Empty;

    /// <summary>
    ///     Picks the outcomes of one model from a file: the named model, or the only one the file has.
    /// </summary>
    private static List<TokenOutcome> SelectModel(List<(string Model, TokenOutcome Outcome)> outcomes, string? model, string file)
    {
        var models = outcomes.Select(o => o.Model).Distinct(StringComparer.Ordinal).ToList();
        var name = model ?? (models.Count == 1 ? models[0] : throw new ArgumentException($"The file '{Path.GetFileName(file)}' has the models {string.Join(", ", models)}; name one with --base-model or --new-model."));
        return models.Contains(name) ? [.. outcomes.Where(o => o.Model == name).Select(o => o.Outcome)] : throw new ArgumentException($"The file '{Path.GetFileName(file)}' has no outcomes of the model '{name}'.");
    }

    private static async Task<int> RunEvaluateSuiteAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var directory = Path.Join(corpus, "suite");
        var rows = SuiteFile.ReadRows(Path.Join(directory, SuiteFile.SuiteName));
        var expectationsFile = Path.Join(directory, SuiteFile.ExpectationsName);
        var expectations = File.Exists(expectationsFile) ? SuiteFile.ReadExpectations(expectationsFile, rows) : null;
        var answers = await SuiteEvaluator.RunDetailedAsync(repository, rows, output, cancellationToken).ConfigureAwait(false);
        var grades = (IReadOnlyDictionary<string, IReadOnlyList<SuiteGrade>>)answers.ToDictionary(p => p.Key, p => (IReadOnlyList<SuiteGrade>)[.. p.Value.Select(a => a.Grade)], StringComparer.Ordinal);
        var text = SuiteEvaluator.Render(rows, grades, expectations);
        if (options.TryGetValue("diff", out var baselineFile))
        {
            // Every change against the baseline, in either direction, row by row
            var baseline = SuiteDiff.ReadOutcomes(Path.GetFullPath(baselineFile));
            var changes = SuiteDiff.Diff(rows, expectations ?? throw new InvalidDataException($"The expectation file '{expectationsFile}' is needed for --diff."), baseline, answers);
            text += "\n" + SuiteDiff.RenderDiff(changes, rows.Count);
        }

        output.Write(text);
        WriteReport(options, text);
        if (options.TryGetValue("outcomes", out var suiteOutcomes))
        {
            File.WriteAllText(suiteOutcomes, SuiteDiff.RenderOutcomes(rows, answers), new System.Text.UTF8Encoding(false));
            output.WriteLine($"Wrote the answers of the suite to {suiteOutcomes}.");
        }
        if (options.TryGetValue("write-expectations", out var target))
        {
            if (File.Exists(target) && !IsOn(options, "overwrite"))
            {
                throw new InvalidDataException($"The expectation file '{target}' exists; give --overwrite true to replace it. An expectation is raised for an improvement in a reviewed commit and is never lowered to hide a regression.");
            }

            File.WriteAllText(target, SuiteFile.RenderExpectations(rows, grades), new System.Text.UTF8Encoding(false));
            output.WriteLine($"Wrote the expectations to {target}.");
        }

        return 0;
    }

    private static async Task<int> RunEvaluateShapesAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var rows = ShapeProbeFile.Read(corpus);
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"The shape probe set is not in '{ShapeProbeFile.PathOf(corpus)}'.");
        }

        var models = new List<(string Model, IReadOnlyList<ShapeAnswer> Answers)>();
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
            models.Add((ModelTable.DisplayName(info.Name), await ShapeProbeEvaluator.AnswerAsync(model, rows, cancellationToken).ConfigureAwait(false)));
            output.WriteLine($"{info.Name}: shape probe done.");
        }

        var text = ShapeProbeEvaluator.Render(rows, models);
        output.Write(text);
        WriteReport(options, text);
        if (options.TryGetValue("outcomes", out var outcomes))
        {
            File.WriteAllText(outcomes, ShapeProbeEvaluator.RenderOutcomes(rows, models), new System.Text.UTF8Encoding(false));
        }

        return 0;
    }

    private static int RunCheckLeakage(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var sets = LeakageCheck.Load(corpus, data);
        var gold = options.TryGetValue("gold", out var g) ? Path.GetFullPath(g) : RunningTextEvaluator.DefaultGold(repository);
        if (File.Exists(gold))
        {
            sets.Add(LeakageCheck.FromGold("UD-EWT test", gold));
        }
        else if (options.ContainsKey("gold"))
        {
            // An explicit path that cannot be used would otherwise leave the treebank test file out of the check
            throw new FileNotFoundException($"The gold file '{gold}' given by --gold does not exist.", gold);
        }

        if (options.TryGetValue("gold-dir", out var goldDirectory))
        {
            // The three treebank files; the test file given by --gold keeps its place
            sets.AddRange(LeakageCheck.FromExplicitGoldDirectory(Path.GetFullPath(goldDirectory)).Where(s => sets.All(x => x.Name != s.Name)).ToList());
        }

        if (options.TryGetValue("read-set", out var readSet))
        {
            sets.Add(LeakageCheck.FromExplicitReadSet(Path.GetFullPath(readSet)));
        }

        var cache = options.TryGetValue("cache", out var c) ? Path.GetFullPath(c) : SemCor.CacheDirectory(repository);
        var archive = Path.Join(cache, SemCor.ArchiveName);
        if (File.Exists(archive))
        {
            var (semcor, _) = SemCor.ReadArchive(archive, SemCor.ArchiveSha256);
            sets.Add(new LeakageSet("SemCor (independent sense evaluation)", LeakageRole.External, [.. semcor.Select(s => (s.Id, string.Join(' ', s.Words)))]));
        }

        var report = LeakageCheck.Check(sets);
        var text = report.Render();

        // The second all-words gold set must also share no document with the rest of the repository
        var documentProblems = Gold2.ValidateDocuments(corpus);
        if (documentProblems.Count > 0)
        {
            text += "\n### Document check of the second all-words gold set\n\n" + string.Concat(documentProblems.Select(p => "- LEAK: " + p + "\n"));
        }

        output.Write(text);
        WriteReport(options, text);
        return report.Leaks.Any() || documentProblems.Count > 0 ? 1 : 0;
    }

    private static int RunTiers(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var rows = Tiers.Build(Corpus.ReadDirectory(Path.Join(corpus, "anchors")), Corpus.ReadDirectory(Path.Join(corpus, "holdout")), Tiers.ReadTagCounts(data));
        output.Write(Tiers.RenderSummary(rows));
        if (options.TryGetValue("output", out var report))
        {
            File.WriteAllText(report, Tiers.RenderRows(rows), new System.Text.UTF8Encoding(false));
            output.WriteLine($"Wrote the quota report of {rows.Count.ToString("N0", CultureInfo.InvariantCulture)} senses to {report}.");
        }

        return 0;
    }

    private static int RunAllWordsSample(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var directory = Path.GetFullPath(Require(options, "batches"));
        var data = options.TryGetValue("data", out var dataFolder) ? Path.GetFullPath(dataFolder) : Path.Join(repository, "data");
        var perSplit = IntOption(options, "per-split", 600, 1);
        var perLemma = IntOption(options, "max-per-lemma", 3, 1);
        var seed = IntOption(options, "seed", 1);
        var size = IntOption(options, "batch-size", 100, 1);
        var samples = new List<AllWordsSample>();
        foreach (var split in new[] { AllWordsGold.Dev, AllWordsGold.Test })
        {
            var drawn = AllWordsGold.Draw(corpus, split, perSplit, perLemma, seed);
            output.WriteLine($"{split}: {drawn.Count.ToString(CultureInfo.InvariantCulture)} tokens of {drawn.Select(s => s.Lemma).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture)} lemmas.");
            samples.AddRange(drawn);
        }

        var batches = AllWordsGold.WriteBatches(data, samples, directory, size);
        output.WriteLine($"Wrote {batches.ToString(CultureInfo.InvariantCulture)} batches and sample.tsv to {directory}.");
        return 0;
    }

    private static int RunAllWordsMerge(Dictionary<string, string> options, string corpus, TextWriter output)
    {
        var directory = Path.GetFullPath(Require(options, "batches"));
        var target = options.TryGetValue("target", out var t) ? Path.GetFullPath(t) : Path.Join(corpus, AllWordsGold.Folder);
        var (gold, agreement) = AllWordsGold.Merge(directory, Require(options, "first"), Require(options, "second"));
        Directory.CreateDirectory(target);

        // A split with no kept token still gets a (header-only) file, or an earlier file of the split would stay and be read as gold
        foreach (var split in agreement.Select(a => a.Split).Union(gold.Keys, StringComparer.Ordinal))
        {
            File.WriteAllText(Path.Join(target, split + ".tsv"), AllWordsGold.Render(split, gold.GetValueOrDefault(split) ?? []), new System.Text.UTF8Encoding(false));
        }

        var text = AllWordsGold.RenderAgreement(agreement);
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }

    private static async Task<int> RunEvaluateAllWordsAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var halves = new List<(string Split, List<AllWordsItem> Items)>();
        foreach (var split in new[] { AllWordsGold.Dev, AllWordsGold.Test })
        {
            var items = AllWordsGold.Read(corpus, split);
            if (items.Count > 0)
            {
                halves.Add((split, items));
            }
        }

        if (halves.Count == 0)
        {
            throw new InvalidDataException($"The all-words gold set is not in '{Path.Join(corpus, AllWordsGold.Folder)}'.");
        }

        var scores = new List<AllWordsModelScore>();
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
            foreach (var (split, items) in halves)
            {
                scores.Add(await AllWordsEvaluator.MeasureAsync(ModelTable.DisplayName(info.Name), model, split, items, cancellationToken).ConfigureAwait(false));
            }

            output.WriteLine($"{info.Name}: all-words gold set done.");
        }

        var baselines = halves.Select(h => AllWordsEvaluator.Baselines(h.Split, h.Items)).ToList();
        var dev = halves.FirstOrDefault(h => h.Split == AllWordsGold.Dev).Items;
        var sweep = dev is null ? null : AllWordsEvaluator.Sweep(dev, AllWordsEvaluator.SweepValues);
        var lemmas = halves.ToDictionary(h => h.Split, h => h.Items.Select(i => i.Lemma).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var text = AllWordsEvaluator.Render(scores, baselines, sweep, lemmas);
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }

    private static void WriteReport(Dictionary<string, string> options, string text)
    {
        if (options.TryGetValue("output", out var report))
        {
            File.WriteAllText(report, text, new System.Text.UTF8Encoding(false));
        }
    }
}
