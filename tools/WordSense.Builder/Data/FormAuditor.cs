using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     Audits the generated inflected forms of the form table: a stratified, seeded sample per provenance is checked against an
///     independent inflection oracle and the error rate of every stratum is reported with a Wilson confidence interval.
/// </summary>
/// <remarks>
///     The oracle is a development-time checker only: it is a Python script that asks LemmInflect (installed with pip, not a
///     dependency, nothing shipped from it) whether the spelling is an inflected form of the lemma. A disagreement is an error
///     unless the adjudications file records our own verdict, decided from our sources, that the row is valid (a valid rare word
///     that the oracle does not know); the forms of the oracle are never adopted or committed. The oracle shares no data with the generator, but the rows the
///     generator admits were selected by the dictionary and the inflection database, so the rates describe the shipped rows,
///     not the rule outputs before the corroboration.
/// </remarks>
internal static class FormAuditor
{
    /// <summary>The provenance values that the generator derives by rule or by reverse rule; the gate applies to them.</summary>
    public static readonly string[] GeneratedProvenance = ["rule", "rule_variant", "agid", "moby_reverse"];

    /// <summary>The oracle script, relative to the repository.</summary>
    public const string OracleScript = "tools/WordSense.Builder/audit/lemminflect_oracle.py";

    /// <summary>The default adjudications file, relative to the repository.</summary>
    public const string AdjudicationsFile = "tools/WordSense.Builder/audit/adjudications.tsv";

    private const double Z = 1.959963984540054;

    /// <summary>One audited row.</summary>
    /// <param name="Spelling">The spelling.</param>
    /// <param name="Lemma">The lemma.</param>
    /// <param name="Pos">The part-of-speech letter.</param>
    /// <param name="Features">The features.</param>
    /// <param name="Stratum">The stratum label.</param>
    internal sealed record Row(string Spelling, string Lemma, string Pos, string Features, string Stratum);

    /// <summary>The audit result of one stratum.</summary>
    /// <param name="Stratum">The label.</param>
    /// <param name="Population">The inflected rows of the stratum.</param>
    /// <param name="Checked">The sampled rows the oracle could check.</param>
    /// <param name="Disagreed">The rows the oracle disagreed with.</param>
    /// <param name="Errors">The rows that are errors after the adjudication.</param>
    internal sealed record StratumResult(string Stratum, int Population, int Checked, int Disagreed, int Errors)
    {
        /// <summary>Gets the error rate.</summary>
        public double Rate => Checked == 0 ? 0 : (double)Errors / Checked;

        /// <summary>Gets the lower bound of the 95% Wilson interval.</summary>
        public double Low => Wilson(Errors, Checked).Low;

        /// <summary>Gets the upper bound of the 95% Wilson interval.</summary>
        public double High => Wilson(Errors, Checked).High;
    }

    /// <summary>The whole audit.</summary>
    /// <param name="Strata">The strata.</param>
    /// <param name="Failures">The error rows.</param>
    /// <param name="Weighted">The error rate of the generated strata weighted by population.</param>
    /// <param name="Passed">Tells whether every generated stratum is below the limit.</param>
    internal sealed record AuditResult(List<StratumResult> Strata, List<string> Failures, double Weighted, bool Passed);

    /// <summary>
    ///     Computes the Wilson score interval of a proportion.
    /// </summary>
    /// <param name="errors">The errors.</param>
    /// <param name="n">The sample size.</param>
    /// <returns>The lower and upper bound.</returns>
    public static (double Low, double High) Wilson(int errors, int n)
    {
        if (n == 0)
        {
            return (0, 1);
        }

        var p = (double)errors / n;
        var z2 = Z * Z;
        var denominator = 1 + (z2 / n);
        var center = (p + (z2 / (2 * n))) / denominator;
        var half = Z * Math.Sqrt((p * (1 - p) / n) + (z2 / (4.0 * n * n))) / denominator;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    ///     Selects the stratified sample.
    /// </summary>
    /// <param name="data">The data folder.</param>
    /// <param name="sample">The rows per stratum.</param>
    /// <param name="seed">The random seed.</param>
    /// <returns>The sampled rows and the population of every stratum.</returns>
    public static (List<Row> Sample, Dictionary<string, int> Population) Select(string data, int sample, int seed)
    {
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        var entries = DataTables.Read(data, schema.Table("entry")).ToDictionary(e => e[0], e => e, StringComparer.Ordinal);
        var strata = new SortedDictionary<string, List<Row>>(StringComparer.Ordinal);
        foreach (var f in DataTables.Read(data, schema.Table("form")))
        {
            string lemma;
            string pos;
            if (f[2].Length > 0 && entries.TryGetValue(f[2], out var entry))
            {
                lemma = entry[1];
                pos = entry[2];
            }
            else
            {
                lemma = f[6];
                pos = f[7];
            }

            if (!IsInflected(f[3]))
            {
                continue;
            }

            var key = $"{f[8]}/{(f[2].Length > 0 ? "entry" : "surface")}";
            if (!strata.TryGetValue(key, out var list))
            {
                strata[key] = list = [];
            }

            list.Add(new Row(f[0], lemma, pos, f[3], key));
        }

        var population = strata.ToDictionary(s => s.Key, s => s.Value.Count, StringComparer.Ordinal);
        var picked = new List<Row>();
        foreach (var rows in strata.Values)
        {
            var ordered = rows.OrderBy(r => Rank(seed, r), StringComparer.Ordinal).ToList();
            picked.AddRange(ordered.Take(sample).OrderBy(r => r.Spelling, StringComparer.Ordinal));
        }

        return (picked, population);
    }

    /// <summary>
    ///     Runs the oracle script on the sample.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="sample">The sampled rows.</param>
    /// <returns>The verdict by row key: <c>ok</c>, <c>bad</c> or <c>skip</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the oracle cannot run.</exception>
    public static Dictionary<string, string> RunOracle(string repository, List<Row> sample)
    {
        var script = Path.Combine(repository, OracleScript.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(script))
        {
            throw new InvalidOperationException($"The oracle script '{script}' does not exist.");
        }

        var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var verdicts = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            File.WriteAllLines(input, sample.Select(r => string.Join('\t', r.Spelling, r.Lemma, r.Pos, r.Features)), new UTF8Encoding(false));
#pragma warning disable S4036 // The checker is the python of the build machine, as documented
            var start = new ProcessStartInfo("python") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
#pragma warning restore S4036
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(input);
            start.ArgumentList.Add(verdicts);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Python could not be started.");
            var stderr = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"The oracle failed (install it with 'pip install lemminflect'): {stderr.Trim()}");
            }

            return ReadVerdicts(verdicts);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Python is required for the oracle: {ex.Message}", ex);
        }
        finally
        {
            File.Delete(input);
            File.Delete(verdicts);
        }
    }

    /// <summary>
    ///     Reads a verdict file: spelling, lemma, part of speech, features, verdict.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The verdicts by row key.</returns>
    public static Dictionary<string, string> ReadVerdicts(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }

            var p = line.Split('\t');
            if (p.Length < 5 || p[4] is not ("ok" or "bad" or "skip"))
            {
                throw new InvalidDataException($"The verdict file '{path}' is malformed at line {number}: expected spelling, lemma, part of speech, features and a verdict of ok, bad or skip.");
            }

            result[Key(p[0], p[1], p[2], p[3])] = p[4];
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException($"The verdict file '{path}' holds no verdict.");
        }

        return result;
    }

    /// <summary>
    ///     Reads our adjudications: spelling, lemma, part of speech, verdict (<c>valid</c> or <c>invalid</c>), a note naming our own sources and
    ///     optionally the features the verdict is restricted to.
    /// </summary>
    /// <param name="path">The file, or null.</param>
    /// <returns>The verdict by spelling, lemma and part of speech.</returns>
    public static Dictionary<string, string> ReadAdjudications(string? path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path))
        {
            return result;
        }

        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var p = line.Split('\t');
            if (p.Length < 4 || p[3] is not ("valid" or "invalid"))
            {
                throw new InvalidDataException($"The adjudications file '{path}' is malformed at line {number}: expected spelling, lemma, part of speech and a verdict of valid or invalid.");
            }

            // An optional sixth column restricts the verdict to one feature set
            var features = p.Length >= 6 && p[5].Length > 0 ? "\t" + p[5] : string.Empty;
            result[$"{p[0]}\t{p[1]}\t{p[2]}{features}"] = p[3];
        }

        return result;
    }

    /// <summary>
    ///     Evaluates the verdicts of a sample.
    /// </summary>
    /// <param name="sample">The sampled rows.</param>
    /// <param name="population">The population of every stratum.</param>
    /// <param name="verdicts">The oracle verdicts.</param>
    /// <param name="adjudications">The adjudications.</param>
    /// <param name="maxError">The error rate that the generated strata must stay below.</param>
    /// <returns>The audit result.</returns>
    public static AuditResult Evaluate(List<Row> sample, Dictionary<string, int> population, Dictionary<string, string> verdicts, Dictionary<string, string> adjudications, double maxError)
    {
        var results = new List<StratumResult>();
        var failures = new List<string>();
        foreach (var group in sample.GroupBy(r => r.Stratum, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int n = 0, disagreed = 0, errors = 0;
            foreach (var row in group)
            {
                if (!verdicts.TryGetValue(Key(row.Spelling, row.Lemma, row.Pos, row.Features), out var verdict))
                {
                    throw new InvalidDataException($"The oracle gave no verdict for the sampled row '{row.Spelling}' ({row.Lemma}, {row.Pos}, {row.Features}).");
                }

                var adjudicated = adjudications.GetValueOrDefault($"{row.Spelling}\t{row.Lemma}\t{row.Pos}\t{row.Features}") ?? adjudications.GetValueOrDefault($"{row.Spelling}\t{row.Lemma}\t{row.Pos}");
                if (verdict == "skip" && adjudicated is null)
                {
                    continue;
                }

                n++;

                // Our verdict decides a row where the checker disagrees or cannot judge; a row the checker accepts stays valid
                var bad = verdict != "ok" && (adjudicated is null ? verdict == "bad" : adjudicated == "invalid");
                if (verdict == "bad")
                {
                    disagreed++;
                }

                if (bad)
                {
                    errors++;
                    failures.Add($"{group.Key}\t{row.Spelling}\t{row.Lemma}\t{row.Pos}\t{row.Features}\t{(adjudicated is null ? "unadjudicated" : "invalid")}");
                }
            }

            results.Add(new StratumResult(group.Key, population.GetValueOrDefault(group.Key), n, disagreed, errors));
        }

        // A generated stratum that has rows but none checked proves nothing: the gate fails instead of skipping it
        var unaudited = population.Where(p => p.Value > 0 && IsGenerated(p.Key) && !results.Any(r => r.Stratum == p.Key && r.Checked > 0)).Select(p => p.Key).ToList();
        foreach (var stratum in unaudited)
        {
            failures.Add($"{stratum}\tno row was checked");
            if (!results.Any(r => r.Stratum == stratum))
            {
                results.Add(new StratumResult(stratum, population[stratum], 0, 0, 0));
            }
        }

        var generated = results.Where(r => IsGenerated(r.Stratum) && r.Checked > 0).ToList();
        var total = generated.Sum(r => (double)r.Population);
        var weighted = total == 0 ? 0 : generated.Sum(r => r.Rate * r.Population) / total;
        return new AuditResult(results, failures, weighted, unaudited.Count == 0 && generated.All(r => r.Rate < maxError));
    }

    /// <summary>
    ///     Renders the audit as a report.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="sample">The rows per stratum.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="maxError">The limit.</param>
    /// <returns>The Markdown text.</returns>
    public static string Render(AuditResult result, int sample, int seed, double maxError)
    {
        var text = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        text.Append("# Forms audit\n\n");
        text.Append(c, $"A stratified sample of up to {sample} inflected rows per provenance and entry-backed or surface-only stratum (seed {seed}) is checked against the LemmInflect oracle.\n");
        text.Append("A disagreement is an error unless the adjudications file records our own verdict that the row is valid. The interval is the 95% Wilson interval.\n\n");
        text.Append("| Stratum | Rows | Checked | Oracle disagrees | Errors | Error rate | 95% interval | Gated |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var r in result.Strata)
        {
            text.Append(c, $"| {r.Stratum} | {r.Population} | {r.Checked} | {r.Disagreed} | {r.Errors} | {r.Rate:P2} | {r.Low:P2} to {r.High:P2} | {(IsGenerated(r.Stratum) ? "yes" : "no")} |\n");
        }

        text.Append(c, $"\nWeighted error rate of the generated strata: {result.Weighted:P3}; limit {maxError:P2}; result: {(result.Passed ? "PASS" : "FAIL")}.\n");
        if (result.Failures.Count > 0)
        {
            text.Append("\n## Error rows\n\n```text\n");
            foreach (var f in result.Failures)
            {
                text.Append(f).Append('\n');
            }

            text.Append("```\n");
        }

        return text.ToString();
    }

    private static bool IsGenerated(string stratum) => GeneratedProvenance.Contains(stratum[..stratum.IndexOf('/', StringComparison.Ordinal)], StringComparer.Ordinal);

    private static string Key(string spelling, string lemma, string pos, string features) => $"{spelling}\t{lemma}\t{pos}\t{features}";

    private static bool IsInflected(string features) =>
        features.Split('+').Any(f => f is "Number=Plur" or "Person=3" or "Tense=Past" or "VerbForm=Part" or "VerbForm=Ger" or "Degree=Cmp" or "Degree=Sup");

    // The pseudo-random rank of a row: a hash of the seed and the row, so the sample is fixed by the seed and the data alone
    private static string Rank(int seed, Row row) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\t{row.Stratum}\t{row.Spelling}\t{row.Lemma}\t{row.Pos}\t{row.Features}")));
}
