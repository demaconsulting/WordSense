using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using WordSense.Builder.Data;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The commands of the builder that make, label and evaluate the second all-words gold set: <c>gold2-power</c>,
///     <c>gold2-sample</c>, <c>gold2-batches</c>, <c>gold2-merge</c>, <c>evaluate-gold2</c> and <c>compare-gold2</c>.
/// </summary>
internal static partial class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    ///     Runs <c>gold2-power</c>: prints the power plan.
    /// </summary>
    /// <param name="output">The writer for the plan.</param>
    /// <returns>0.</returns>
    private static int RunGold2Power(TextWriter output)
    {
        output.Write(Gold2Power.Render());
        return 0;
    }

    /// <summary>
    ///     Runs <c>gold2-sample</c>: draws the tokens from a pool of sentences and writes the frozen manifest and the documents.
    /// </summary>
    /// <param name="options">The options (<c>pool</c>, <c>docs</c>, <c>seed</c>, <c>target</c>, <c>data</c>).</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer for the report of the draw.</param>
    /// <returns>0.</returns>
    private static int RunGold2Sample(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var data = options.TryGetValue("data", out var dataFolder) ? Path.GetFullPath(dataFolder) : Path.Join(repository, "data");
        var poolPath = Path.GetFullPath(Require(options, "pool"));
        var docsPath = Path.GetFullPath(Require(options, "docs"));
        var seed = IntOption(options, "seed", 1);
        var target = options.TryGetValue("target", out var t) ? Path.GetFullPath(t) : Gold2.Directory(corpus);
        var plan = Gold2Plan.Default;

        // The sentences of the pool that any other part of the repository holds are left out, and so are the documents it uses
        var known = new HashSet<string>(LeakageCheck.Load(corpus, data).Where(s => !s.Name.StartsWith("all-words v2", StringComparison.Ordinal)).SelectMany(s => s.Sentences.Select(x => Corpus.Normalize(x.Sentence))), StringComparer.Ordinal);
        var (usedIds, usedUrls) = Gold2.UsedDocuments(corpus);
        var documents = Gold2.ReadDocumentsFile(docsPath);
        var allowed = documents.Where(d => !usedIds.Contains(d.DocId) && !usedUrls.Contains(d.Url)).ToDictionary(d => d.DocId, StringComparer.Ordinal);
        var pool = new List<Gold2Sentence>();
        var skippedKnown = 0;
        var skippedDocument = 0;
        foreach (var line in File.ReadLines(poolPath, Utf8NoBom).Skip(1))
        {
            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length < 4)
            {
                continue;
            }

            if (!allowed.ContainsKey(c[1]))
            {
                skippedDocument++;
                continue;
            }

            var words = c[3].Split(' ');
            if (known.Contains(Corpus.Normalize(string.Join(' ', words))))
            {
                skippedKnown++;
                continue;
            }

            pool.Add(new Gold2Sentence(c[0], c[1], c[2], words));
        }

        output.WriteLine($"Pool: {pool.Count.ToString("N0", CultureInfo.InvariantCulture)} sentences of {pool.Select(s => s.DocId).Distinct(StringComparer.Ordinal).Count().ToString("N0", CultureInfo.InvariantCulture)} documents ({skippedKnown.ToString("N0", CultureInfo.InvariantCulture)} sentences already in the repository and {skippedDocument.ToString("N0", CultureInfo.InvariantCulture)} of used documents left out).");
        var trained = Corpus.ReadWeightedLemmas(Path.Join(corpus, "anchors"));
        var (bytes, _) = ImageCompiler.Build(data);
        var image = WordImage.Open(bytes);
        var heteronyms = Heteronyms.Build(data, Path.Join(corpus, "curation", "heteronyms.tsv"), image).Select(h => h.Spelling).ToHashSet(StringComparer.Ordinal);
        var tokens = Gold2Sampler.Draw(pool, plan, trained, heteronyms, seed);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Join(target, Gold2.ManifestName), Gold2.RenderManifest(tokens), Utf8NoBom);
        var used = tokens.Select(x => x.DocId).ToHashSet(StringComparer.Ordinal);
        File.WriteAllText(Path.Join(target, Gold2.SourcesName), Gold2.RenderDocuments(documents.Where(d => used.Contains(d.DocId)).OrderBy(d => d.DocId, StringComparer.Ordinal)), Utf8NoBom);
        output.Write(RenderDraw(tokens));
        return 0;
    }

    /// <summary>
    ///     Renders the report of the draw: the tokens, documents, sentences and strata of each split and the tokens of each source.
    /// </summary>
    /// <param name="tokens">The drawn tokens.</param>
    /// <returns>The Markdown text.</returns>
    private static string RenderDraw(IReadOnlyList<Gold2Token> tokens)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("### The draw\n\n| Split | Tokens | Documents | Sentences |");
        foreach (var stratum in Gold2Strata.DrawOrder)
        {
            text.Append(' ').Append(stratum).Append(" |");
        }

        text.Append("\n| --- | ---: | ---: | ---: |").Append(string.Concat(Enumerable.Repeat(" ---: |", Gold2Strata.DrawOrder.Count))).Append('\n');
        foreach (var split in new[] { Gold2.Dev, Gold2.Test })
        {
            var part = tokens.Where(x => x.Split == split).ToList();
            var counts = Gold2Sampler.CountStrata(part);
            text.Append("| ").Append(split).Append(" | ").Append(part.Count.ToString("N0", inv))
                .Append(" | ").Append(part.Select(x => x.DocId).Distinct(StringComparer.Ordinal).Count().ToString("N0", inv))
                .Append(" | ").Append(part.Select(x => x.SentenceId).Distinct(StringComparer.Ordinal).Count().ToString("N0", inv)).Append(" |");
            foreach (var stratum in Gold2Strata.DrawOrder)
            {
                text.Append(' ').Append(counts[stratum].ToString("N0", inv)).Append(" |");
            }

            text.Append('\n');
        }

        text.Append("\n| Source | Dev tokens | Test tokens |\n| --- | ---: | ---: |\n");
        foreach (var family in tokens.Select(x => x.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            text.Append("| ").Append(family).Append(" | ").Append(tokens.Count(x => x.Source == family && x.Split == Gold2.Dev).ToString("N0", inv))
                .Append(" | ").Append(tokens.Count(x => x.Source == family && x.Split == Gold2.Test).ToString("N0", inv)).Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Runs <c>gold2-batches</c>: writes the labeling or the adjudication batches of the manifest.
    /// </summary>
    /// <param name="options">The options (<c>dir</c>, <c>stage</c>, <c>splits</c>, <c>batch-size</c>, <c>tag</c>, <c>missing</c>, <c>first</c>, <c>second</c>, <c>data</c>).</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer for the report.</param>
    /// <returns>0.</returns>
    /// <exception cref="InvalidDataException">Thrown when the stage is unknown, the manifest has no tokens, or a labeler has not answered a token that goes to the adjudicator.</exception>
    private static int RunGold2Batches(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var directory = Path.GetFullPath(Require(options, "dir"));
        var data = options.TryGetValue("data", out var dataFolder) ? Path.GetFullPath(dataFolder) : Path.Join(repository, "data");
        var stage = options.GetValueOrDefault("stage", "label");
        if (stage is not ("label" or "adjudicate"))
        {
            throw new InvalidDataException($"--stage must be label or adjudicate, not '{stage}'.");
        }

        var size = IntOption(options, "batch-size", 100, 1);
        var tag = options.GetValueOrDefault("tag", string.Empty);
        var splits = options.GetValueOrDefault("splits", "dev,test").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = Gold2.ReadManifest(corpus).Where(x => splits.Contains(x.Split)).ToList();
        if (tokens.Count == 0)
        {
            throw new InvalidDataException($"The manifest '{Path.Join(Gold2.Directory(corpus), Gold2.ManifestName)}' has no tokens.");
        }

        if (stage == "label")
        {
            if (options.TryGetValue("missing", out var suffix))
            {
                var (answers, _) = Gold2Labeling.ReadAnswers(directory, suffix, false);
                tokens = [.. tokens.Where(x => !answers.ContainsKey(x.Id))];

                // The retry batches sit beside the earlier ones under a tag of their own: the table of the earlier round keeps its mapping
                tag = Gold2Labeling.RetryTag(directory, "label", tag);
                output.WriteLine($"Retry round '{tag}' for the {tokens.Count.ToString("N0", CultureInfo.InvariantCulture)} tokens without an answer.");
            }

            var count = Gold2Labeling.WriteBatches(data, tokens, directory, size, "label", null, tag);
            output.WriteLine($"Wrote {count.ToString(CultureInfo.InvariantCulture)} labeling batches for {tokens.Count.ToString("N0", CultureInfo.InvariantCulture)} tokens to {Path.Join(directory, "batches")}.");
            return 0;
        }

        var (first, firstProblems) = Gold2Labeling.ReadAnswers(directory, Require(options, "first"), false);
        var (second, secondProblems) = Gold2Labeling.ReadAnswers(directory, Require(options, "second"), false);
        foreach (var problem in firstProblems.Concat(secondProblems))
        {
            output.WriteLine(problem);
        }

        var (settled, pending, missing) = Gold2Labeling.Compare(tokens, first, second);
        output.WriteLine($"Settled by the labelers: {settled.Count.ToString("N0", CultureInfo.InvariantCulture)}; to adjudicate: {pending.Count.ToString("N0", CultureInfo.InvariantCulture)}; without an answer of a labeler: {missing.Count.ToString("N0", CultureInfo.InvariantCulture)}.");
        if (missing.Count > 0)
        {
            throw new InvalidDataException("Some tokens have no answer of a labeler; label them first (gold2-batches --missing).");
        }

        if (options.TryGetValue("missing", out var adjudicator))
        {
            var (judged, _) = Gold2Labeling.ReadAnswers(directory, adjudicator, true);
            pending = [.. pending.Where(x => !judged.ContainsKey(x.Id))];
            tag = Gold2Labeling.RetryTag(directory, "adjudicate", tag);
            output.WriteLine($"Retry round '{tag}' for the {pending.Count.ToString("N0", CultureInfo.InvariantCulture)} tokens without a verdict.");
        }

        var pairs = pending.ToDictionary(x => x.Id, x => (first[x.Id], second[x.Id]), StringComparer.Ordinal);
        var written = Gold2Labeling.WriteBatches(data, pending, directory, size, "adjudicate", pairs, tag);
        output.WriteLine($"Wrote {written.ToString(CultureInfo.InvariantCulture)} adjudication batches for {pending.Count.ToString("N0", CultureInfo.InvariantCulture)} tokens to {Path.Join(directory, "batches")}.");
        return 0;
    }

    /// <summary>
    ///     Runs <c>gold2-merge</c>: merges the answers of the labelers and the adjudicator into the labeled rows and the raw labels.
    /// </summary>
    /// <param name="options">The options (<c>dir</c>, <c>first</c>, <c>second</c>, <c>adjudicator</c>, <c>target</c>, <c>output</c>).</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer for the agreement table, or for the problems.</param>
    /// <returns>0, or 1 (nothing written) when an answer is missing, invalid or for a token the manifest does not have, a document is not listed, or a row would not match the manifest.</returns>
    private static int RunGold2Merge(Dictionary<string, string> options, string corpus, TextWriter output)
    {
        var directory = Path.GetFullPath(Require(options, "dir"));
        var target = options.TryGetValue("target", out var t) ? Path.GetFullPath(t) : Gold2.Directory(corpus);
        var tokens = Gold2.ReadManifestFile(Path.Join(target, Gold2.ManifestName));
        var (first, p1) = Gold2Labeling.ReadAnswers(directory, Require(options, "first"), false);
        var (second, p2) = Gold2Labeling.ReadAnswers(directory, Require(options, "second"), false);
        var (judge, p3) = Gold2Labeling.ReadAnswers(directory, Require(options, "adjudicator"), true);
        var (outcomes, problems) = Gold2Labeling.Merge(tokens, first, second, judge);
        problems.AddRange(p1.Concat(p2).Concat(p3));
        var manifestIds = tokens.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, answers) in new[] { ("first labeler", first), ("second labeler", second), ("adjudicator", judge) })
        {
            problems.AddRange(answers.Keys.Where(id => !manifestIds.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{id}: the {name} answered a token that {Gold2.ManifestName} does not have."));
        }

        var documents = Gold2.ReadDocumentsFile(Path.Join(target, Gold2.SourcesName)).ToDictionary(d => d.DocId, StringComparer.Ordinal);
        var rowsOf = new Dictionary<string, List<Gold2Row>>(StringComparer.Ordinal);
        foreach (var split in new[] { Gold2.Dev, Gold2.Test })
        {
            rowsOf[split] = [.. outcomes.Where(o => o.Token.Split == split && documents.ContainsKey(o.Token.DocId)).Select(o => Gold2Labeling.ToRow(o, documents[o.Token.DocId].TermsUrl))];
            problems.AddRange(Gold2.ValidateRows(tokens, rowsOf[split], split));
        }

        problems.AddRange(outcomes.Where(o => !documents.ContainsKey(o.Token.DocId)).Select(o => $"{o.Token.Id}: its document '{o.Token.DocId}' is not in {Gold2.SourcesName}."));
        if (problems.Count > 0)
        {
            foreach (var problem in problems.Take(50))
            {
                output.WriteLine(problem);
            }

            output.WriteLine($"{problems.Count.ToString("N0", CultureInfo.InvariantCulture)} problems: nothing was written.");
            return 1;
        }

        var counts = new List<Gold2Agreement>();
        foreach (var split in new[] { Gold2.Dev, Gold2.Test })
        {
            File.WriteAllText(Path.Join(target, split + ".tsv"), Gold2.RenderRows(split, rowsOf[split]), Utf8NoBom);
            counts.Add(Gold2Labeling.Count(split, [.. outcomes.Where(o => o.Token.Split == split)]));
        }

        File.WriteAllText(Path.Join(target, Gold2.LabelsName), Gold2Labeling.RenderLabels(outcomes), Utf8NoBom);
        var text = Gold2Labeling.RenderAgreement(counts);
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }

    /// <summary>
    ///     Runs <c>evaluate-gold2</c>: measures the models on the splits and compares every pair of models.
    /// </summary>
    /// <param name="options">The options (<c>model</c>, <c>splits</c>, <c>output</c>, <c>outcomes</c>).</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer for the progress and the report.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>0.</returns>
    /// <exception cref="InvalidDataException">Thrown when the labeled set is missing or its rows do not match the manifest.</exception>
    private static async Task<int> RunEvaluateGold2Async(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var splits = options.GetValueOrDefault("splits", "dev,test").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var halves = splits.Select(s => (Split: s, Rows: Gold2.ReadVerifiedRows(corpus, s))).Where(h => h.Rows.Count > 0).ToList();
        if (halves.Count == 0)
        {
            throw new InvalidDataException($"The labeled second all-words gold set is not in '{Gold2.Directory(corpus)}'.");
        }

        var (trained, basis) = Gold2.TrainedLemmas(repository, corpus);
        var results = halves.ToDictionary(h => h.Split, _ => new List<(string Model, IReadOnlyList<Gold2Scored> Scored)>(), StringComparer.Ordinal);
        foreach (var info in ModelCatalog.Select(options.GetValueOrDefault("model")))
        {
            using var model = ModelCatalog.Open(info, repository);
            await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
            var display = ModelTable.DisplayName(info.Name);
            foreach (var (split, rows) in halves)
            {
                results[split].Add((display, await Gold2Evaluator.MeasureAsync(model, rows, cancellationToken).ConfigureAwait(false)));
            }

            output.WriteLine($"{info.Name}: second all-words gold set done.");
        }

        var text = new StringBuilder("### Second all-words gold set (document-disjoint, two labelers and an adjudicator)\n\n");
        text.Append("Trained lemmas (the lemmas that have weights): ").Append(basis).Append(".\n\n");
        foreach (var (split, rows) in halves)
        {
            var models = results[split];
            var withBaselines = new List<(string, IReadOnlyList<Gold2Scored>)>(models)
            {
                ("first sense in inventory order (told the gold lemma and part of speech)", Gold2Evaluator.Baseline(rows, false)),
                ("most frequent sense by WordNet 3.0 tag count (told the gold lemma and part of speech)", Gold2Evaluator.Baseline(rows, true)),
            };
            text.Append(Gold2Evaluator.RenderStrata(split, rows, trained));
            text.Append(Gold2Evaluator.RenderPower(split, rows, models[0].Scored, trained));
            text.Append(Gold2Evaluator.Render(split, rows, withBaselines, trained));
            if (options.TryGetValue("outcomes", out var directory))
            {
                Directory.CreateDirectory(directory);
                foreach (var (name, scored) in models)
                {
                    File.WriteAllText(Path.Join(directory, $"gold2-{split}-{name.ToLowerInvariant().Replace(' ', '-')}.tsv"), Gold2Evaluator.RenderOutcomes(scored), Utf8NoBom);
                }
            }

            if (models.Count > 1)
            {
                text.Append("##### Paired comparisons (").Append(split).Append(")\n\n");
                for (var a = 0; a < models.Count; a++)
                {
                    for (var b = a + 1; b < models.Count; b++)
                    {
                        text.Append(Gold2Evaluator.RenderComparison(split, models[a].Model, models[b].Model, Gold2Evaluator.Compare(rows, models[a].Scored, models[b].Scored, trained)));
                    }
                }
            }
        }

        output.Write(text.ToString());
        WriteReport(options, text.ToString());
        return 0;
    }

    /// <summary>
    ///     Runs <c>compare-gold2</c>: compares two outcome files on the same tokens.
    /// </summary>
    /// <param name="options">The options (<c>first</c>, <c>second</c>, <c>split</c>, <c>name-first</c>, <c>name-second</c>, <c>output</c>).</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="output">The writer for the report.</param>
    /// <returns>0.</returns>
    /// <exception cref="InvalidDataException">Thrown when the split has no rows, its rows do not match the manifest, or an outcome file does not cover every token.</exception>
    private static int RunCompareGold2(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var split = options.GetValueOrDefault("split", Gold2.Test);
        var rows = Gold2.ReadVerifiedRows(corpus, split);
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"The labeled second all-words gold set has no '{split}' split in '{Gold2.Directory(corpus)}'.");
        }

        var first = Gold2Evaluator.ReadOutcomes(Path.GetFullPath(Require(options, "first")));
        var second = Gold2Evaluator.ReadOutcomes(Path.GetFullPath(Require(options, "second")));
        var missing = rows.Where(r => !first.ContainsKey(r.Token.Id) || !second.ContainsKey(r.Token.Id)).Select(r => r.Token.Id).Take(5).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"The outcome files do not cover every token of the {split} split (for example {string.Join(", ", missing)}); both files must come from the same split.");
        }

        var (trained, _) = Gold2.TrainedLemmas(repository, corpus);
        var comparisons = Gold2Evaluator.Compare(rows, [.. rows.Select(r => first[r.Token.Id])], [.. rows.Select(r => second[r.Token.Id])], trained);
        var text = Gold2Evaluator.RenderComparison(split, options.GetValueOrDefault("name-first", "first"), options.GetValueOrDefault("name-second", "second"), comparisons);
        output.Write(text);
        WriteReport(options, text);
        return 0;
    }
}
