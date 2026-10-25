namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests of the usage prior of the senses of a lemma and part of speech: the ranking by the WordNet 3.0 tag counts
///     with add-k smoothing and the inventory order as the tie-break.
/// </summary>
public class UsagePriorTests
{
    private static readonly (string, int)[] Counts = [("w#a", 3), ("w#b", 1), ("w#c", 0)];

    /// <summary>
    ///     The probability of a sense is its count plus k over the sum of that over the senses of the slot, and a sense
    ///     without a count keeps a probability above zero.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_ProbabilityIsTheCountPlusKOverTheSum()
    {
        // Act
        var ranked = UsagePrior.Rank(Counts, 1.0);

        // Assert: (3 + 1), (1 + 1) and (0 + 1) over 7
        Assert.Equal(4.0 / 7, ranked[0].Probability, 12);
        Assert.Equal(2.0 / 7, ranked[1].Probability, 12);
        Assert.Equal(1.0 / 7, ranked[2].Probability, 12);
    }

    /// <summary>
    ///     The probabilities of the ranked senses sum to one.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_ProbabilitiesSumToOne()
    {
        // Act
        var ranked = UsagePrior.Rank(Counts, 0.3);

        // Assert
        Assert.Equal(1.0, ranked.Sum(s => s.Probability), 12);
    }

    /// <summary>
    ///     The ranking of the catalog uses the smoothing constant 1.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_UsesTheSmoothingConstantOne()
    {
        // Arrange
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);

        // Act
        var ranked = UsagePrior.RankOf("bank", PartOfSpeech.Noun);

        // Assert
        Assert.Equal(1.0, UsagePrior.Smoothing);
        Assert.True(UsagePrior.Rank(usage, 1.0).SequenceEqual(ranked));
    }

    /// <summary>
    ///     The senses are sorted by probability, the largest first.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_SortsByProbabilityLargestFirst()
    {
        // Arrange: the counts are not in order
        (string, int)[] usage = [("w#a", 0), ("w#b", 5), ("w#c", 2), ("w#d", 9)];

        // Act
        var ranked = UsagePrior.Rank(usage, 0.5);

        // Assert
        Assert.Equal(["w#d", "w#b", "w#c", "w#a"], ranked.Select(s => s.SenseId));
        Assert.Equal(ranked.OrderByDescending(s => s.Probability).Select(s => s.Probability), ranked.Select(s => s.Probability));
    }

    /// <summary>
    ///     Senses with the same count have the same probability and keep the inventory order (the order of the input).
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_EqualCounts_KeepInventoryOrder()
    {
        // Arrange: the inventory order is a, b, c, d, e
        (string, int)[] usage = [("w#a", 0), ("w#b", 5), ("w#c", 0), ("w#d", 5), ("w#e", 2)];

        // Act
        var ranked = UsagePrior.Rank(usage, 0.5);

        // Assert
        Assert.Equal(["w#b", "w#d", "w#e", "w#a", "w#c"], ranked.Select(s => s.SenseId));
        Assert.Equal(ranked[0].Probability, ranked[1].Probability);
        Assert.Equal(ranked[3].Probability, ranked[4].Probability);
    }

    /// <summary>
    ///     A lemma without any count is ranked by the inventory order alone, with the same probability for every sense.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_NoCounts_IsUniformInInventoryOrder()
    {
        // Act
        var ranked = UsagePrior.Rank([("w#a", 0), ("w#b", 0), ("w#c", 0), ("w#d", 0)], 1.0);

        // Assert
        Assert.Equal(["w#a", "w#b", "w#c", "w#d"], ranked.Select(s => s.SenseId));
        Assert.All(ranked, s => Assert.Equal(0.25, s.Probability, 12));
    }

    /// <summary>
    ///     The smoothing constant never changes the order of the ranking.
    /// </summary>
    /// <param name="k">The smoothing constant.</param>
    [Theory]
    [InlineData(0.01)]
    [InlineData(1.0)]
    [InlineData(100.0)]
    public void UsagePrior_Rank_SmoothingConstant_NeverChangesTheOrder(double k)
    {
        // Act
        var ranked = UsagePrior.Rank(Counts, k);

        // Assert
        Assert.Equal(["w#a", "w#b", "w#c"], ranked.Select(s => s.SenseId));
    }

    /// <summary>
    ///     A smoothing constant that is not a positive number is rejected.
    /// </summary>
    /// <param name="k">The smoothing constant.</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UsagePrior_Rank_SmoothingNotPositive_Throws(double k)
    {
        // Act / Assert: the constant is out of range
        Assert.Throws<ArgumentOutOfRangeException>(() => UsagePrior.Rank(Counts, k));
    }

    /// <summary>
    ///     A negative count is rejected.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_NegativeCount_Throws()
    {
        // Act / Assert: a count below zero is out of range
        Assert.Throws<ArgumentOutOfRangeException>(() => UsagePrior.Rank([("w#a", -1), ("w#b", 2)], 1.0));
    }

    /// <summary>
    ///     A missing list of senses is rejected.
    /// </summary>
    [Fact]
    public void UsagePrior_Rank_MissingList_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => UsagePrior.Rank(null!, 1.0));
    }

    /// <summary>
    ///     A missing lemma is rejected.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_MissingLemma_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => UsagePrior.RankOf(null!, PartOfSpeech.Noun));
    }

    /// <summary>
    ///     For a polysemous lemma of the catalog the ranking lists every sense of the slot once, sorted by probability,
    ///     summing to one, with the senses of the slot in inventory order among equal probabilities.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_PolysemousLemma_RanksEverySenseOfTheSlot()
    {
        // Act
        var ranked = UsagePrior.RankOf("bank", PartOfSpeech.Noun);
        var inventory = WordLexicon.SensesOf("bank", PartOfSpeech.Noun).Select(s => s.SenseId).ToList();
        var usage = WordLexicon.Current.UsageOf("bank", PartOfSpeech.Noun);

        // Assert
        Assert.True(ranked.Length >= 2);
        Assert.Equal(inventory.Order(StringComparer.Ordinal), ranked.Select(s => s.SenseId).Order(StringComparer.Ordinal));
        Assert.Equal(1.0, ranked.Sum(s => s.Probability), 9);
        Assert.Equal(ranked.OrderByDescending(s => s.Probability).Select(s => s.Probability), ranked.Select(s => s.Probability));
        Assert.Equal(usage.Max(u => u.TagCount), usage.First(u => u.SenseId == ranked[0].SenseId).TagCount);

        // A tie is in inventory order
        for (var i = 1; i < ranked.Length; i++)
        {
            if (Math.Abs(ranked[i].Probability - ranked[i - 1].Probability) < 1e-15)
            {
                Assert.True(inventory.IndexOf(ranked[i - 1].SenseId) < inventory.IndexOf(ranked[i].SenseId));
            }
        }
    }

    /// <summary>
    ///     A lemma and part of speech that is not in the catalog has no ranking.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_NotInTheCatalog_IsEmpty()
    {
        // Act
        var unknownLemma = UsagePrior.RankOf("zzqxjw", PartOfSpeech.Noun);
        var unknownPartOfSpeech = UsagePrior.RankOf("bank", PartOfSpeech.Determiner);

        // Assert
        Assert.Empty(unknownLemma);
        Assert.Empty(unknownPartOfSpeech);
    }

    /// <summary>
    ///     A lemma and part of speech with exactly one sense has no ranking.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_SingleSense_IsEmpty()
    {
        // Arrange: a slot of the catalog with a single sense
        var single = WordLexicon.Current.AllSenses().GroupBy(s => (s.Lemma, s.PartOfSpeech)).First(g => g.Count() == 1).Key;

        // Act
        var ranked = UsagePrior.RankOf(single.Lemma, single.PartOfSpeech);

        // Assert
        Assert.Empty(ranked);
    }

    /// <summary>
    ///     A sense without a sense key (a project sense) has no count: it is ranked with the probability of a sense that was
    ///     never tagged, after every sense that was.
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_ProjectSense_CountsZero()
    {
        // Arrange: a slot with a project sense and at least one other sense
        var slot = WordLexicon.Current.AllSenses()
            .GroupBy(s => (s.Lemma, s.PartOfSpeech))
            .First(g => g.Count() >= 2 && g.Any(s => s.SenseKey is null) && g.Any(s => s.SenseKey is not null))
            .Key;
        var project = WordLexicon.SensesOf(slot.Lemma, slot.PartOfSpeech).First(s => s.SenseKey is null).SenseId;

        // Act
        var ranked = UsagePrior.RankOf(slot.Lemma, slot.PartOfSpeech);
        var usage = WordLexicon.Current.UsageOf(slot.Lemma, slot.PartOfSpeech);

        // Assert
        Assert.Equal(0, usage.First(u => u.SenseId == project).TagCount);
        var position = ranked.ToList().FindIndex(s => s.SenseId == project);
        Assert.All(ranked.Take(position), s => Assert.True(s.Probability >= ranked[position].Probability));
        Assert.Equal(UsagePrior.Smoothing / (usage.Sum(u => u.TagCount) + (UsagePrior.Smoothing * usage.Length)), ranked[position].Probability, 12);
    }

    /// <summary>
    ///     The ranking is the same on every call (it is cached, and the cache returns the same list).
    /// </summary>
    [Fact]
    public void UsagePrior_RankOf_RepeatedCalls_GiveTheSameRanking()
    {
        // Act
        var first = UsagePrior.RankOf("bass", PartOfSpeech.Noun);
        var second = UsagePrior.RankOf("bass", PartOfSpeech.Noun);
        var other = UsagePrior.RankOf("bass", PartOfSpeech.Adjective);

        // Assert
        Assert.True(first.SequenceEqual(second));
        Assert.False(first.IsDefaultOrEmpty);
        Assert.False(first.SequenceEqual(other));
    }
}
