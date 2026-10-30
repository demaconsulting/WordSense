using System.Globalization;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One measure of the second all-words gold set with the improvement it must be able to detect and the discordance that
///     the sample size is computed from.
/// </summary>
/// <param name="Name">The measure.</param>
/// <param name="Mdi">The minimum detectable improvement of a paired comparison, as a share (0.01 is one point).</param>
/// <param name="Discordance">The share of tokens on which the two models are expected to differ (one right, the other wrong), from the spikes.</param>
/// <param name="Evidence">Where the discordance comes from.</param>
/// <param name="PlannedTest">The planned number of test tokens that bear the measure.</param>
internal sealed record Gold2Measure(string Name, double Mdi, double Discordance, string Evidence, int PlannedTest);

/// <summary>
///     The power calculation of the second all-words gold set: how many tokens a paired comparison of two models needs to
///     detect an improvement, and what improvement a given number of tokens can detect.
/// </summary>
/// <remarks>
///     <para>
///     A paired comparison on the same tokens is a McNemar test. With <c>d</c> the share of tokens on which the two models
///     differ and <c>δ</c> the true difference of their accuracies (a share of all tokens), the normal approximation needs
///     <c>n = (z(1-α/2)·√d + z(power)·√(d-δ²))² / δ²</c> tokens.
///     </para>
/// </remarks>
internal static class Gold2Power
{
    /// <summary>The significance level (two-sided).</summary>
    public const double Alpha = 0.05;

    /// <summary>The power.</summary>
    public const double Power = 0.8;

    /// <summary>
    ///     Gets the measures with their minimum detectable improvement and the discordance observed in the spikes.
    /// </summary>
    public static IReadOnlyList<Gold2Measure> Measures { get; } =
    [
        new("Part of speech (all tokens)", 0.01, 0.035, "POS head vs Basic: 755 of 21,482 open-corpus test tokens differ (3.5%); 4.9% on UD-EWT test and 5.9% on the ambiguous words alone; the gold set holds ambiguous words (more discordance) and control words (none)", 3000),
        new("Sense, untrained polysemous lemmas", 0.04, 0.24, "nearest-neighbor vs shipped on 209 untrained test tokens: 95% interval half-width 6.9 points (d about 26%); on all 397: 4.3 points (d about 19%)", 1200),
        new("Sense, trained polysemous lemmas", 0.05, 0.20, "same intervals on the 188 trained test tokens", 630),
        new("Tense (same-spelling verbs)", 0.05, 0.14, "POS head vs Basic: 145 of 994 treebank tokens differ (14.6%), 27 of 198 'read' tokens (13.6%), 18 of 110 open-corpus tokens (16.4%)", 480),
        new("Lemma (lemma-ambiguous forms)", 0.05, 0.12, "assumed like the same-lemma tense split (no spike measured it)", 380),
    ];

    /// <summary>
    ///     Computes the number of tokens that a paired comparison needs.
    /// </summary>
    /// <param name="discordance">The share of tokens on which the models differ.</param>
    /// <param name="difference">The difference of the accuracies to detect (a share of all tokens).</param>
    /// <param name="alpha">The two-sided significance level.</param>
    /// <param name="power">The power.</param>
    /// <returns>The number of tokens, rounded up.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the difference is not positive or larger than the discordance allows.</exception>
    public static int PairedSampleSize(double discordance, double difference, double alpha = Alpha, double power = Power)
    {
        if (difference <= 0 || difference * difference >= discordance)
        {
            throw new ArgumentOutOfRangeException(nameof(difference), "The difference must be positive and its square below the discordance.");
        }

        var root = (Quantile(1 - (alpha / 2)) * Math.Sqrt(discordance)) + (Quantile(power) * Math.Sqrt(discordance - (difference * difference)));
        return (int)Math.Ceiling(root * root / (difference * difference));
    }

    /// <summary>
    ///     Finds the smallest improvement that a number of tokens detects.
    /// </summary>
    /// <param name="tokens">The number of tokens.</param>
    /// <param name="discordance">The share of tokens on which the models differ.</param>
    /// <param name="alpha">The two-sided significance level.</param>
    /// <param name="power">The power.</param>
    /// <returns>The difference of the accuracies, as a share.</returns>
    public static double MinimumDetectable(int tokens, double discordance, double alpha = Alpha, double power = Power)
    {
        double low = 1e-6, high = Math.Sqrt(discordance) - 1e-9;
        for (var i = 0; i < 80; i++)
        {
            var middle = (low + high) / 2;
            if (PairedSampleSize(discordance, middle, alpha, power) > tokens)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return high;
    }

    /// <summary>
    ///     Computes the quantile of the standard normal distribution (the algorithm of Acklam, relative error below 1.2e-9).
    /// </summary>
    /// <param name="p">The probability, between 0 and 1 exclusive.</param>
    /// <returns>The quantile.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="p"/> is not between 0 and 1.</exception>
    public static double Quantile(double p)
    {
        if (p is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "The probability must be between 0 and 1.");
        }

        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        const double split = 0.02425;
        if (p < split)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            var numerator = (((((c[0] * q) + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5];
            var denominator = ((((d[0] * q) + d[1]) * q + d[2]) * q + d[3]) * q + 1;
            return numerator / denominator;
        }

        if (p > 1 - split)
        {
            return -Quantile(1 - p);
        }

        var r = p - 0.5;
        var s = r * r;
        var central = ((((((a[0] * s) + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r;
        var scale = (((((b[0] * s) + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1;
        return central / scale;
    }

    /// <summary>
    ///     Computes the exact two-sided McNemar p value: the probability, when the models are equally good, of a split of the
    ///     discordant tokens at least as uneven as the observed one.
    /// </summary>
    /// <param name="wins">The tokens that only the second model got right.</param>
    /// <param name="losses">The tokens that only the first model got right.</param>
    /// <returns>The p value (1 when there are no discordant tokens).</returns>
    public static double ExactMcNemar(int wins, int losses)
    {
        var n = wins + losses;
        if (n == 0)
        {
            return 1.0;
        }

        var small = Math.Min(wins, losses);
        var logFactorial = new double[n + 1];
        for (var i = 2; i <= n; i++)
        {
            logFactorial[i] = logFactorial[i - 1] + Math.Log(i);
        }

        var tail = 0.0;
        for (var k = 0; k <= small; k++)
        {
            tail += Math.Exp(logFactorial[n] - logFactorial[k] - logFactorial[n - k] - (n * Math.Log(2)));
        }

        return Math.Min(1.0, 2 * tail);
    }

    /// <summary>
    ///     Renders the power plan as Markdown.
    /// </summary>
    /// <returns>The text with line feeds.</returns>
    public static string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("### Power plan of the second all-words gold set\n\n");
        text.Append("Paired comparison of two models on the same tokens (McNemar), two-sided α = 0.05, power 80%.\n\n");
        text.Append("| Measure | Minimum detectable improvement | Discordance | Test tokens needed | Test tokens planned | Smallest improvement detected with the planned tokens |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var m in Measures)
        {
            text.Append("| ").Append(m.Name)
                .Append(" | ").Append((m.Mdi * 100).ToString("0.0", inv)).Append(" points")
                .Append(" | ").Append((m.Discordance * 100).ToString("0.0", inv)).Append('%')
                .Append(" | ").Append(PairedSampleSize(m.Discordance, m.Mdi).ToString("N0", inv))
                .Append(" | ").Append(m.PlannedTest.ToString("N0", inv))
                .Append(" | ").Append((MinimumDetectable(m.PlannedTest, m.Discordance) * 100).ToString("0.00", inv)).Append(" points |\n");
        }

        text.Append("\nDiscordance evidence:\n\n");
        foreach (var m in Measures)
        {
            text.Append("- ").Append(m.Name).Append(": ").Append(m.Evidence).Append(".\n");
        }

        return text.ToString();
    }
}
