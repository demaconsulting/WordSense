using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The retained helpers of the blind part-of-speech audit: the stable row id and the reader of the committed
///     <c>corpus/audit/pos-audit.tsv</c>.
/// </summary>
internal static class PosAudit
{
    /// <summary>
    ///     The marker of a row that the audit could not settle.
    /// </summary>
    public const string Undecided = "undecided";

    /// <summary>
    ///     Computes the stable row id: the first 12 hexadecimal characters of SHA-256 over <c>lemma|sentence</c>.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The id.</returns>
    public static string RowId(string lemma, string sentence)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(lemma + "|" + sentence));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    /// <summary>
    ///     A merged row read back from <c>pos-audit.tsv</c>.
    /// </summary>
    /// <param name="Id">The row id.</param>
    /// <param name="Lemma">The lemma.</param>
    /// <param name="Sense">The sense label at audit time.</param>
    /// <param name="Split">The split.</param>
    /// <param name="FinalPos">The final part of speech or <c>undecided</c>.</param>
    /// <param name="FinalForm">The final form.</param>
    /// <param name="Resolution">The resolution.</param>
    public sealed record AuditLine(string Id, string Lemma, string Sense, string Split, string FinalPos, string FinalForm, string Resolution);

    /// <summary>
    ///     Reads <c>pos-audit.tsv</c>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The lines.</returns>
    public static List<AuditLine> ReadResults(string path)
    {
        return File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split('\t'))
            .Select(c => new AuditLine(c[0], c[1], c[2], c[3], c[13], c[14], c[16])).ToList();
    }
}
