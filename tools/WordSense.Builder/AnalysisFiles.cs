using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     File names and helpers shared by the analysis commands (<c>learning-curve</c>, <c>error-diagnosis</c> and
///     <c>weak-lemmas</c>), which exchange tab-separated files in one analysis directory.
/// </summary>
internal static class AnalysisFiles
{
    /// <summary>
    ///     The learning-curve points per lemma, set and anchor count.
    /// </summary>
    public const string Curve = "learning-curve.tsv";

    /// <summary>
    ///     The learning-curve points aggregated over all lemmas.
    /// </summary>
    public const string CurveSummary = "learning-curve-summary.tsv";

    /// <summary>
    ///     The plateau estimate per lemma and set.
    /// </summary>
    public const string Plateau = "learning-curve-plateau.tsv";

    /// <summary>
    ///     The diagnosis of every misclassified or abstained evaluation sentence.
    /// </summary>
    public const string Errors = "error-diagnosis.tsv";

    /// <summary>
    ///     The error counts per lemma, set and class.
    /// </summary>
    public const string ErrorLemmas = "error-diagnosis-lemmas.tsv";

    /// <summary>
    ///     The similarity statistics per diagnosis class, used to calibrate the thresholds.
    /// </summary>
    public const string ErrorSummary = "error-diagnosis-summary.tsv";

    /// <summary>
    ///     The ranked weak lemmas with their recommendation.
    /// </summary>
    public const string Weak = "weak-lemmas.tsv";

    /// <summary>
    ///     The evaluation set of all holdout rows.
    /// </summary>
    public const string SetHoldout = "holdout";

    /// <summary>
    ///     The evaluation set of the holdout rows whose provenance origin is <c>real</c>.
    /// </summary>
    public const string SetReal = "real";

    /// <summary>
    ///     The evaluation set of the separate real-text evaluation sentences.
    /// </summary>
    public const string SetRealEval = "real-eval";

    /// <summary>
    ///     The sets in report order.
    /// </summary>
    public static readonly string[] Sets = [SetHoldout, SetReal, SetRealEval];

    /// <summary>
    ///     Formats a number with four decimals in the invariant culture.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Num(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats an integer in the invariant culture.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Writes a file into the analysis directory as UTF-8 without a byte-order mark.
    /// </summary>
    /// <param name="directory">The analysis directory, created when missing.</param>
    /// <param name="name">The file name.</param>
    /// <param name="content">The content.</param>
    public static void Write(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), content, new UTF8Encoding(false));
    }

    /// <summary>
    ///     Reads a tab-separated file with a header line and a fixed number of columns.
    /// </summary>
    /// <param name="directory">The analysis directory.</param>
    /// <param name="name">The file name.</param>
    /// <param name="columns">The required number of columns.</param>
    /// <returns>The data rows.</returns>
    /// <exception cref="InvalidDataException">Thrown when a row has the wrong number of columns.</exception>
    public static List<string[]> Read(string directory, string name, int columns)
    {
        var rows = new List<string[]>();
        var lines = File.ReadAllLines(Path.Combine(directory, name), new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length != columns)
            {
                throw new InvalidDataException($"{name}:{i + 1}: expected {columns} tab-separated columns but found {parts.Length}.");
            }

            rows.Add(parts);
        }

        return rows;
    }

    /// <summary>
    ///     Parses a number written by <see cref="Num"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="name">The file name, for the error message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the text is not a number.</exception>
    public static double ParseNum(string text, string name)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{name}: '{text}' is not a number.");
    }

    /// <summary>
    ///     Parses an integer.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="name">The file name, for the error message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">Thrown when the text is not an integer.</exception>
    public static int ParseInt(string text, string name)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{name}: '{text}' is not an integer.");
    }
}
