namespace WordSense.Builder;

/// <summary>
///     The sources that a sentence of the corpus may come from: the one place that decides which provenance rows are
///     allowed (the licence gate), and which of the allowed sources are real text and which are written for the project.
/// </summary>
/// <remarks>
///     <para>
///     A provenance row of <c>corpus/provenance</c> names the <c>source</c> of its sentence and nothing about its licence:
///     the licence follows from the source, and the evidence for each source is in the Sources section of
///     <c>corpus/README.md</c>. A source is added here only together with that evidence.
///     </para>
/// </remarks>
internal static class ProvenanceSources
{
    /// <summary>
    ///     The sources of real text: Project Gutenberg books published in 1930 or earlier (public domain in the United
    ///     States), the staff-written articles of VOA Learning English, pages of USGS, NPS and the National Weather Service
    ///     (<c>nws</c>) and handbooks and advisory circulars of the FAA (works of US federal employees, 17 U.S.C. 105), and
    ///     the sentences of the Tatoeba export that is released under CC0 1.0.
    /// </summary>
    public static readonly IReadOnlyList<string> Real = ["gutenberg", "voa", "usgs", "nps", "nws", "faa", "tatoeba-cc0"];

    /// <summary>
    ///     The sources of sentences written for the project and dedicated to CC0 1.0: <c>llm-corpus</c> (written by a language
    ///     model for the project) and <c>authored</c> (written by the project).
    /// </summary>
    public static readonly IReadOnlyList<string> Written = ["llm-corpus", "authored"];

    /// <summary>
    ///     Gets a value indicating whether a source is allowed.
    /// </summary>
    /// <param name="source">The source column of a provenance row.</param>
    /// <returns>True when the source is a real-text source or a written source.</returns>
    public static bool IsAllowed(string source) => IsReal(source) || IsWritten(source);

    /// <summary>
    ///     Gets a value indicating whether a source is real text (not written for the project).
    /// </summary>
    /// <param name="source">The source column of a provenance row.</param>
    /// <returns>True when the source is one of <see cref="Real" />.</returns>
    public static bool IsReal(string source) => Real.Contains(source, StringComparer.Ordinal);

    /// <summary>
    ///     Gets a value indicating whether a source is written for the project.
    /// </summary>
    /// <param name="source">The source column of a provenance row.</param>
    /// <returns>True when the source is one of <see cref="Written" />.</returns>
    public static bool IsWritten(string source) => Written.Contains(source, StringComparer.Ordinal);
}
