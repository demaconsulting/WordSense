namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     The code of the C# samples of the user guide, compiled by the test project and run by
///     <see cref="UserGuideSampleTests"/> where they need no download.
/// </summary>
/// <remarks>
///     The text between the <c>&lt;sample:name&gt;</c> and <c>&lt;/sample:name&gt;</c> markers must equal the
///     matching code block of <c>docs/user_guide/introduction.md</c> (marked <c>&lt;!-- sample:name --&gt;</c>).
///     The statements after the end marker only use the variables of the sample.
/// </remarks>
internal static class UserGuideSamples
{
    /// <summary>
    ///     The "quick-start" sample. It downloads the model when it is not in the per-user store, so it is
    ///     compiled but not run by the tests.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task QuickStartAsync()
    {
        // <sample:quick-start>
        // Downloads the model on first use (mirror, Hugging Face, GitHub LFS), verifies its SHA-256 and reuses it later
        using var classifier = await WordSenseClassifier.CreateAsync();

        string[] words = ["The", "record", "was", "broken", "so", "they", "will", "record", "the", "new", "album"];
        WordSense?[] senses = await classifier.ClassifyAsync(words);

        for (var i = 0; i < words.Length; i++)
        {
            if (senses[i] is { } sense)
            {
                Console.WriteLine($"{words[i]}: {sense.Sense} [{string.Join(", ", sense.Tags)}]");
            }
        }

        // </sample:quick-start>
    }

    /// <summary>
    ///     The "text" sample.
    /// </summary>
    /// <param name="classifier">The classifier.</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task TextAsync(WordSenseClassifier classifier)
    {
        // <sample:text>
        IReadOnlyList<WordSenseToken> tokens = await classifier.ClassifyAsync("They will record the record.");

        foreach (var token in tokens)
        {
            Console.WriteLine($"{token.Start,3} {token.Text,-8} {token.Sense?.ToString() ?? "-"}");
        }

        // </sample:text>
    }

    /// <summary>
    ///     The "detail" sample.
    /// </summary>
    /// <param name="classifier">The classifier.</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task DetailAsync(WordSenseClassifier classifier)
    {
        // <sample:detail>
        string[] words = ["They", "will", "record", "the", "record"];
        WordSenseDetail?[] details = await classifier.ClassifyDetailAsync(words);

        foreach (var detail in details.OfType<WordSenseDetail>())
        {
            Console.WriteLine(
                $"{detail.Sense} margin={detail.Margin:F2} runner-up={detail.RunnerUp} abstained={detail.Abstained}");
        }

        // </sample:detail>
    }

    /// <summary>
    ///     The "tags" sample.
    /// </summary>
    /// <param name="classifier">The classifier.</param>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task TagsAsync(WordSenseClassifier classifier)
    {
        // <sample:tags>
        string[] words = ["They", "will", "record", "the", "record"];
        string?[] pos = await classifier.ClassifyTagAsync(words, ["noun", "verb"]); // "noun", "verb" or null
        bool?[] isVerb = await classifier.IsTagAsync(words, "verb");                // true, false or null

        for (var i = 0; i < words.Length; i++)
        {
            Console.WriteLine($"{words[i]}: pos={pos[i] ?? "-"} isVerb={isVerb[i]?.ToString() ?? "-"}");
        }

        // </sample:tags>
    }

    /// <summary>
    ///     The "senses" sample.
    /// </summary>
    /// <param name="classifier">The classifier.</param>
    public static void Senses(WordSenseClassifier classifier)
    {
        // <sample:senses>
        Console.WriteLine($"{classifier.KnownLemmas.Count} lemmas");

        foreach (var sense in classifier.GetSenses("record"))
        {
            var tags = string.Join(",", sense.Tags.Order());
            Console.WriteLine($"{sense.Lemma}: {sense.Sense} (group {sense.Group}) tags={tags}");
        }

        // </sample:senses>
    }

    /// <summary>
    ///     The "tuning" sample. It needs the model in the per-user store, so it is compiled but not run.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task TuningAsync()
    {
        // <sample:tuning>
        var options = new WordSenseClassifierOptions
        {
            AbstainMarginScale = 0.5, // below 1 answers more often, above 1 abstains more often; 0 never abstains
            IntraOpThreads = 2,       // ONNX Runtime threads; 0 selects the default
            MaxSequenceLength = 128,  // model window, 8 to 512 tokens; longer texts use a window around each target
        };

        using var tuned = await WordSenseClassifier.CreateAsync(options);
        // </sample:tuning>
    }

    /// <summary>
    ///     The "download" sample. It downloads the model, so it is compiled but not run.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task DownloadAsync()
    {
        // <sample:download>
        var downloaderOptions = new WordSenseModelDownloaderOptions
        {
            StoreRoot = Path.Combine(Path.GetTempPath(), "wordsense-models"), // default: the per-user directory
            RequestTimeout = TimeSpan.FromMinutes(2),                          // per request; default 5 minutes
            Progress = new Progress<WordSenseModelDownloadProgress>(
                p => Console.WriteLine($"{p.FileName} from {p.Source}: {p.BytesTransferred} bytes")),
        };

        WordSenseModelLocation location = await WordSenseModelDownloader.EnsureModelAsync(downloaderOptions);

        // Offline use: copy the two verified files to a machine without internet access and point ModelDirectory at them
        using var classifier = await WordSenseClassifier.CreateAsync(
            new WordSenseClassifierOptions { ModelDirectory = location.Directory });
        // </sample:download>
    }

    /// <summary>
    ///     The "mirror" sample.
    /// </summary>
    /// <returns>The options of the sample.</returns>
    public static WordSenseClassifierOptions Mirror()
    {
        // <sample:mirror>
        var options = new WordSenseClassifierOptions
        {
            Downloader = new WordSenseModelDownloaderOptions
            {
                Mirror = new DownloadMirror(new Uri("https://models.example.com/wordsense"), bearerToken: "token"),
            },
        };

        // </sample:mirror>
        return options;
    }

    /// <summary>
    ///     The "errors" sample. It downloads the model, so it is compiled but not run.
    /// </summary>
    /// <returns>A task that completes when the sample has run.</returns>
    public static async Task ErrorsAsync()
    {
        // <sample:errors>
        try
        {
            using var classifier = await WordSenseClassifier.CreateAsync();
        }
        catch (WordSenseModelDownloadException ex)
        {
            foreach (var failure in ex.Failures)
            {
                await Console.Error.WriteLineAsync(failure); // one line per source that was tried
            }
        }

        // </sample:errors>
    }
}
