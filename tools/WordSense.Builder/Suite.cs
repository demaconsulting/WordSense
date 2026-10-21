using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     How well a model did on one row of the suite, and the strength of an expectation (the strongest outcome a model
///     is expected to keep): a higher grade includes the lower ones.
/// </summary>
internal enum SuiteGrade
{
    /// <summary>The model's answer is wrong at its own level, or it does not answer (a known miss).</summary>
    Miss = 0,

    /// <summary>The answer is right at the level the model answered.</summary>
    Answer = 1,

    /// <summary>The answer is right at its level and the most probable ranked sense is an acceptable sense of the row.</summary>
    Fine = 2,
}

/// <summary>
///     One reviewed sentence of the suite with what a right answer is.
/// </summary>
/// <param name="Id">The unique id of the row, for example <c>H-use-1</c>.</param>
/// <param name="List">The list of the row: <c>H</c> (common heteronyms), <c>M</c> (words with several parts of speech) or <c>C</c> (classic heteronyms).</param>
/// <param name="Lemma">The lemma of the word list of the row (informative; the analysis names the lemma that is scored).</param>
/// <param name="AnalysisLemma">The lemma the selected analysis must have.</param>
/// <param name="PartOfSpeech">The part of speech the selected analysis must have.</param>
/// <param name="Features">The features (<c>Name=Value</c>) the selected analysis must have; empty when none is required.</param>
/// <param name="SenseIds">The acceptable fine senses; empty for a row that only asks for the analysis.</param>
/// <param name="Words">The words of the sentence, without braces.</param>
/// <param name="TargetIndex">The index of the target word in <paramref name="Words"/>.</param>
/// <param name="Note">The note of the reviewer, possibly empty.</param>
internal sealed record SuiteRow(
    string Id,
    string List,
    string Lemma,
    string AnalysisLemma,
    PartOfSpeech PartOfSpeech,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> SenseIds,
    IReadOnlyList<string> Words,
    int TargetIndex,
    string Note);

/// <summary>
///     The suite: its rows and the expectation of each model for each row.
/// </summary>
/// <param name="Rows">The rows in file order.</param>
/// <param name="Expectations">The expectation of every model (by model name) for every row (by id).</param>
internal sealed record SuiteData(
    IReadOnlyList<SuiteRow> Rows,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, SuiteGrade>> Expectations);

/// <summary>
///     Reads the reviewed suite (<c>corpus/suite</c>), grades the answer of a model on a row and writes the expectation file.
/// </summary>
/// <remarks>
///     <para>
///     <c>suite.tsv</c> has the header <c>id, list, lemma, analysis, senses, target_index, sentence, note</c>. The
///     analysis is <c>lemma/Part[/Feature=Value+...]</c>; the senses are the acceptable fine senses separated by
///     <c>|</c> (empty for a row that only asks for the analysis); the sentence is the words separated by single
///     spaces with the target word in braces, and <c>target_index</c> is its zero-based index.
///     <c>expectations.tsv</c> has the header <c>id, basic, minilm, bert-base-cased</c> and one line for every row, with
///     the strongest grade the model is expected to keep (<c>fine</c>, <c>answer</c> or <c>miss</c>).
///     </para>
///     <para>
///     This file has no dependency on the rest of the builder: the test projects of the three models compile it, so
///     that the reader and the grading rule of the tests are the ones of the builder.
///     </para>
/// </remarks>
internal static class SuiteFile
{
    /// <summary>The name of the file with the rows.</summary>
    public const string SuiteName = "suite.tsv";

    /// <summary>The name of the file with the expectations.</summary>
    public const string ExpectationsName = "expectations.tsv";

    /// <summary>The models of the expectation file, in column order.</summary>
    public static readonly IReadOnlyList<string> Models = ["basic", "minilm", "bert-base-cased"];

    private const string SuiteHeader = "id\tlist\tlemma\tanalysis\tsenses\ttarget_index\tsentence\tnote";
    private const string ExpectationsHeader = "id\tbasic\tminilm\tbert-base-cased";

    /// <summary>
    ///     Finds the suite directory (<c>corpus/suite</c>) of the repository that contains the running program.
    /// </summary>
    /// <returns>The directory.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when no ancestor of the program directory is the repository root.</exception>
    public static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "WordSense.slnx")))
            {
                return Path.Join(dir.FullName, "corpus", "suite");
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    /// <summary>
    ///     Reads the rows and the expectations of a suite directory.
    /// </summary>
    /// <param name="directory">The directory with <c>suite.tsv</c> and <c>expectations.tsv</c>.</param>
    /// <returns>The suite.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static SuiteData Read(string directory)
    {
        var rows = ReadRows(Path.Join(directory, SuiteName));
        return new SuiteData(rows, ReadExpectations(Path.Join(directory, ExpectationsName), rows));
    }

    /// <summary>
    ///     Reads the rows of a suite file and validates every one.
    /// </summary>
    /// <remarks>
    ///     A row is rejected when it does not have eight columns, repeats an id, has no or more than one braced target, a
    ///     <c>target_index</c> that is not the index of the braced word, an analysis that is not
    ///     <c>lemma/Part[/Feature=Value+...]</c>, or a sense that is not in the catalog or whose lemma and part of speech are
    ///     not those of the analysis.
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <returns>The rows in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static IReadOnlyList<SuiteRow> ReadRows(string path)
    {
        var name = Path.GetFileName(path);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || !string.Equals(lines[0].TrimEnd('\r'), SuiteHeader, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}:1: the header must be '{SuiteHeader.Replace("\t", ", ", StringComparison.Ordinal)}'.");
        }

        var rows = new List<SuiteRow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var where = $"{name}:{(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var columns = line.Split('\t');
            if (columns.Length != 8)
            {
                throw new InvalidDataException($"{where}: expected 8 tab-separated columns but found {columns.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            var row = ParseRow(columns, where);
            if (!ids.Add(row.Id))
            {
                throw new InvalidDataException($"{where}: the id '{row.Id}' occurs twice.");
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    ///     Reads the expectation file of a suite.
    /// </summary>
    /// <remarks>
    ///     The file is rejected when its header is wrong, a value is not <c>fine</c>, <c>answer</c> or <c>miss</c>, a line
    ///     names an unknown or repeated row, or a row of the suite has no line (so no expectation of any model).
    /// </remarks>
    /// <param name="path">The file.</param>
    /// <param name="rows">The rows of the suite.</param>
    /// <returns>The expectations by model name and row id.</returns>
    /// <exception cref="InvalidDataException">Thrown with the first problem found.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, SuiteGrade>> ReadExpectations(string path, IReadOnlyList<SuiteRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var name = Path.GetFileName(path);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        if (lines.Length == 0 || !string.Equals(lines[0].TrimEnd('\r'), ExpectationsHeader, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}:1: the header must be '{ExpectationsHeader.Replace("\t", ", ", StringComparison.Ordinal)}'.");
        }

        var known = new HashSet<string>(rows.Select(r => r.Id), StringComparer.Ordinal);
        var perModel = Models.ToDictionary(m => m, _ => new Dictionary<string, SuiteGrade>(StringComparer.Ordinal), StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var where = $"{name}:{(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var columns = line.Split('\t');
            if (columns.Length != 1 + Models.Count)
            {
                throw new InvalidDataException($"{where}: expected {(1 + Models.Count).ToString(CultureInfo.InvariantCulture)} columns but found {columns.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (!known.Contains(columns[0]))
            {
                throw new InvalidDataException($"{where}: the row '{columns[0]}' is not in the suite.");
            }

            for (var m = 0; m < Models.Count; m++)
            {
                if (!TryParseGrade(columns[m + 1], out var grade))
                {
                    throw new InvalidDataException($"{where}: '{columns[m + 1]}' is not an expectation (fine, answer or miss).");
                }

                if (!perModel[Models[m]].TryAdd(columns[0], grade))
                {
                    throw new InvalidDataException($"{where}: the row '{columns[0]}' has two expectations.");
                }
            }
        }

        foreach (var model in Models)
        {
            var missing = rows.FirstOrDefault(r => !perModel[model].ContainsKey(r.Id));
            if (missing is not null)
            {
                throw new InvalidDataException($"{name}: the row '{missing.Id}' has no expectation for the model {model}.");
            }
        }

        return perModel.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, SuiteGrade>)p.Value, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Finds the most probable ranked sense of a result: the sense with the largest sum, over the analyses that list
    ///     it, of the probability of the analysis times its own probability (the first on a tie).
    /// </summary>
    /// <param name="result">The result of the target word.</param>
    /// <returns>The sense id, or null when the result ranks no sense.</returns>
    public static string? BestSense(ClassifyResult result)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var analysis in result.Analyses)
        {
            foreach (var sense in analysis.Senses)
            {
                scores[sense.SenseId] = scores.GetValueOrDefault(sense.SenseId) + (analysis.Probability * sense.Probability);
            }
        }

        string? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var (senseId, score) in scores)
        {
            if (score > bestScore)
            {
                bestScore = score;
                best = senseId;
            }
        }

        return best;
    }

    /// <summary>
    ///     Grades the result of the target word of a row.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The answer is judged at the level the model gave it. A row without acceptable senses only asks for the
    ///     analysis: the selected analysis must have the lemma, the part of speech and the features of the row. A row with
    ///     acceptable senses is answered right at the sense level when the answered sense is one of them, at the group level
    ///     when the answered group has one of them, and at the part-of-speech level when the analysis is right; a word
    ///     that is not answered is a miss.
    ///     </para>
    ///     <para>
    ///     The grade is <see cref="SuiteGrade.Fine"/> when the answer is right and, for a row with acceptable senses, the
    ///     most probable ranked sense (see <see cref="BestSense"/>) is one of them; a model that does not rank senses for
    ///     a word never reaches it on such a row.
    ///     </para>
    /// </remarks>
    /// <param name="row">The row.</param>
    /// <param name="result">The result of the target word.</param>
    /// <returns>The grade.</returns>
    public static SuiteGrade Grade(SuiteRow row, ClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(row);
        var analysisRight = result.Details is { } d
            && string.Equals(d.Lemma, row.AnalysisLemma, StringComparison.Ordinal)
            && d.PartOfSpeech == row.PartOfSpeech
            && row.Features.All(f => FeatureHolds(d, f));
        var answerRight = row.SenseIds.Count == 0
            ? analysisRight
            : result.Level switch
            {
                SenseLevel.Sense => result.SenseId is { } sense && row.SenseIds.Contains(sense),
                SenseLevel.Group => result.GroupId is { } group && WordLexicon.TryGetGroup(group, out var info) && info.SenseIds.Any(row.SenseIds.Contains),
                SenseLevel.PartOfSpeech => analysisRight,
                _ => false,
            };
        if (!answerRight)
        {
            return SuiteGrade.Miss;
        }

        return row.SenseIds.Count == 0 || (BestSense(result) is { } best && row.SenseIds.Contains(best)) ? SuiteGrade.Fine : SuiteGrade.Answer;
    }

    /// <summary>
    ///     Classifies the sentence of every row with a model and grades the answer for its target word.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grades parallel to the rows.</returns>
    public static async Task<IReadOnlyList<SuiteGrade>> GradeAsync(IWordSenseModel model, IReadOnlyList<SuiteRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        var grades = new List<SuiteGrade>(rows.Count);
        foreach (var row in rows)
        {
            var results = await model.ClassifyAsync(row.Words, cancellationToken).ConfigureAwait(false);
            grades.Add(Grade(row, results[row.TargetIndex]));
        }

        return grades;
    }

    /// <summary>
    ///     Lists the rows whose grade is below the expectation, the regressions of a model.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="expectation">The expectation by row id.</param>
    /// <param name="grades">The grades parallel to the rows.</param>
    /// <returns>One line for each regression: the id, the expected grade and the actual grade.</returns>
    public static IReadOnlyList<string> Regressions(IReadOnlyList<SuiteRow> rows, IReadOnlyDictionary<string, SuiteGrade> expectation, IReadOnlyList<SuiteGrade> grades)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(grades);
        var lines = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (grades[i] < expectation[rows[i].Id])
            {
                lines.Add($"{rows[i].Id}: expected {Name(expectation[rows[i].Id])}, got {Name(grades[i])}");
            }
        }

        return lines;
    }

    /// <summary>
    ///     Renders the expectation file for the grades of the models.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="gradesByModel">The grades parallel to the rows, by model name; every model of <see cref="Models"/> is needed.</param>
    /// <returns>The file text with line feeds.</returns>
    public static string RenderExpectations(IReadOnlyList<SuiteRow> rows, IReadOnlyDictionary<string, IReadOnlyList<SuiteGrade>> gradesByModel)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(gradesByModel);
        var text = new StringBuilder(ExpectationsHeader).Append('\n');
        for (var i = 0; i < rows.Count; i++)
        {
            text.Append(rows[i].Id);
            foreach (var model in Models)
            {
                text.Append('\t').Append(Name(gradesByModel[model][i]));
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Gets the name of a grade as written in the expectation file.
    /// </summary>
    /// <param name="grade">The grade.</param>
    /// <returns><c>fine</c>, <c>answer</c> or <c>miss</c>.</returns>
    public static string Name(SuiteGrade grade) => grade switch
    {
        SuiteGrade.Fine => "fine",
        SuiteGrade.Answer => "answer",
        _ => "miss",
    };

    private static bool TryParseGrade(string text, out SuiteGrade grade)
    {
        switch (text)
        {
            case "fine":
                grade = SuiteGrade.Fine;
                return true;
            case "answer":
                grade = SuiteGrade.Answer;
                return true;
            case "miss":
                grade = SuiteGrade.Miss;
                return true;
            default:
                grade = SuiteGrade.Miss;
                return false;
        }
    }

    private static bool FeatureHolds(WordDetails details, string feature)
    {
        var equals = feature.IndexOf('=', StringComparison.Ordinal);
        return details.TryGetFeature(feature[..equals], out var value) && string.Equals(value, feature[(equals + 1)..], StringComparison.Ordinal);
    }

    private static SuiteRow ParseRow(string[] columns, string where)
    {
        var (id, list, lemma, analysis, senses, indexText, sentence, note) = (columns[0], columns[1], columns[2], columns[3], columns[4], columns[5], columns[6], columns[7]);
        if (id.Length == 0 || list.Length == 0 || lemma.Length == 0)
        {
            throw new InvalidDataException($"{where}: the id, the list and the lemma must not be empty.");
        }

        var parts = analysis.Split('/');
        if (parts.Length is < 2 or > 3 || parts[0].Length == 0 || !Enum.TryParse<PartOfSpeech>(parts[1], false, out var pos) || !Enum.IsDefined(pos))
        {
            throw new InvalidDataException($"{where}: the analysis '{analysis}' must be lemma/Part[/Feature=Value+...].");
        }

        var features = parts.Length == 3 ? parts[2].Split('+') : [];
        if (features.Any(f => f.IndexOf('=', StringComparison.Ordinal) < 1))
        {
            throw new InvalidDataException($"{where}: the features of '{analysis}' must be Name=Value items joined by '+'.");
        }

        var senseIds = senses.Length == 0 ? [] : senses.Split('|');
        foreach (var senseId in senseIds)
        {
            if (!WordLexicon.TryGetSense(senseId, out var sense))
            {
                throw new InvalidDataException($"{where}: the sense '{senseId}' is not in the sense catalog.");
            }

            if (sense.PartOfSpeech != pos || !string.Equals(sense.Lemma, parts[0], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{where}: the sense '{senseId}' is not a sense of {parts[0]} ({pos}).");
            }
        }

        var tokens = sentence.Split(' ');
        var targets = tokens.Select((t, i) => (Token: t, Index: i)).Where(t => t.Token.Length > 2 && t.Token[0] == '{' && t.Token[^1] == '}').ToList();
        var braces = sentence.Count(c => c is '{' or '}');
        if (targets.Count != 1 || braces != 2 || tokens.Any(t => t.Length == 0))
        {
            throw new InvalidDataException($"{where}: the sentence must have single-space separated words and exactly one braced target word.");
        }

        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var targetIndex) || targetIndex != targets[0].Index)
        {
            throw new InvalidDataException($"{where}: the target_index '{indexText}' is not the index of the braced word ({targets[0].Index.ToString(CultureInfo.InvariantCulture)}).");
        }

        var words = tokens.Select(t => t.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal)).ToArray();
        return new SuiteRow(id, list, lemma, parts[0], pos, features, senseIds, words, targetIndex, note);
    }
}
