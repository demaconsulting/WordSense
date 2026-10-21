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
        var measured = new List<SenseEvalModelResult>();
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
            measured.Add(await SenseEvaluator.MeasureAsync(ModelTable.DisplayName(info.Name), model, selected, output, cancellationToken).ConfigureAwait(false));
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
        var (sets, notes) = RunningTextEvaluator.Load(corpus, options.GetValueOrDefault("split", "test"), gold);
        var scoresByModel = new List<(string Model, List<TaggerScore> Scores)>();
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await model.EnsureModelAsync(null, cancellationToken).ConfigureAwait(false);
            var scores = await RunningTextEvaluator.RunAsync(model, sets, cancellationToken).ConfigureAwait(false);
            scoresByModel.Add((ModelTable.DisplayName(info.Name), scores));
            output.WriteLine($"{info.Name}: running text done.");
            if (options.TryGetValue("table", out var table))
            {
                ModelTable.Update(table, info.Name, RunningTextEvaluator.TableCells(scores));
            }
        }

        var text = RunningTextEvaluator.Render(scoresByModel, notes);
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }

    private static async Task<int> RunEvaluateSuiteAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var directory = Path.Join(corpus, "suite");
        var rows = SuiteFile.ReadRows(Path.Join(directory, SuiteFile.SuiteName));
        var expectationsFile = Path.Join(directory, SuiteFile.ExpectationsName);
        var expectations = File.Exists(expectationsFile) ? SuiteFile.ReadExpectations(expectationsFile, rows) : null;
        var grades = await SuiteEvaluator.RunAsync(repository, rows, output, cancellationToken).ConfigureAwait(false);
        var text = SuiteEvaluator.Render(rows, grades, expectations);
        output.Write(text);
        WriteReport(options, text);
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

    private static int RunCheckLeakage(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var sets = LeakageCheck.Load(corpus, data);
        var gold = options.TryGetValue("gold", out var g) ? Path.GetFullPath(g) : RunningTextEvaluator.DefaultGold(repository);
        if (File.Exists(gold))
        {
            sets.Add(LeakageCheck.FromGold("UD-EWT test", gold));
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
        output.Write(text);
        WriteReport(options, text);
        return report.Leaks.Any() ? 1 : 0;
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

    private static void WriteReport(Dictionary<string, string> options, string text)
    {
        if (options.TryGetValue("output", out var report))
        {
            File.WriteAllText(report, text, new System.Text.UTF8Encoding(false));
        }
    }
}
