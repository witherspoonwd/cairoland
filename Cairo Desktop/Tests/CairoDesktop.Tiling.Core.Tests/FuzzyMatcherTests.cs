using CairoDesktop.Tiling.Core;

namespace CairoDesktop.Tiling.Core.Tests;

public class FuzzyMatcherTests
{
    private record App(string Name, string? Exe = null, int Frecency = 0);

    private static List<string> RankNames(string query, params App[] apps)
    {
        return FuzzyMatcher.Rank(query, apps, a => a.Name, a => a.Exe, a => a.Frecency).Select(a => a.Name).ToList();
    }

    [Fact]
    public void NonSubsequenceIsNull()
    {
        Assert.Null(FuzzyMatcher.Score("xyz", "Firefox"));
        Assert.Null(FuzzyMatcher.Score("fox fire", "Firefox"));
        Assert.Empty(FuzzyMatcher.MatchPositions("xyz", "Firefox"));
    }

    [Fact]
    public void MatchIsCaseInsensitive()
    {
        Assert.NotNull(FuzzyMatcher.Score("FIREFOX", "firefox"));
        Assert.NotNull(FuzzyMatcher.Score("ff", "FireFox"));
    }

    [Fact]
    public void VscPrefersVisualStudioCode()
    {
        var ranked = RankNames("vsc",
            new App("Microsoft Visual C++ Redist"),
            new App("Visual Studio Code"));
        Assert.Equal("Visual Studio Code", ranked[0]);
        Assert.True(FuzzyMatcher.Score("vsc", "Visual Studio Code") > FuzzyMatcher.Score("vsc", "Microsoft Visual C++ Redist"));
    }

    [Fact]
    public void FfFindsFirefox()
    {
        var ranked = RankNames("ff",
            new App("Microsoft Office"),
            new App("Diff Tool"),
            new App("Firefox"),
            new App("Notepad"));
        Assert.Equal("Firefox", ranked[0]);
        Assert.DoesNotContain("Notepad", ranked);
    }

    [Fact]
    public void MatchPositionsPointAtMatchedCharacters()
    {
        Assert.Equal(new[] { 0, 7, 14 }, FuzzyMatcher.MatchPositions("vsc", "Visual Studio Code"));
        Assert.Equal(new[] { 0, 1, 2, 3 }, FuzzyMatcher.MatchPositions("note", "Notepad"));
    }

    [Fact]
    public void BoundaryAndConsecutiveBonuses()
    {
        Assert.True(FuzzyMatcher.Score("term", "Terminal") > FuzzyMatcher.Score("term", "Windows PowerShell ISE Terminal Mode"));
        Assert.True(FuzzyMatcher.Score("code", "Code") > FuzzyMatcher.Score("code", "C o d e"));
        Assert.True(FuzzyMatcher.Score("gc", "GoogleChrome") > FuzzyMatcher.Score("gc", "Magic"));
    }

    [Fact]
    public void SecondaryKeyMatchesWithPenalty()
    {
        var ranked = RankNames("wt",
            new App("Windows Terminal", "wt"),
            new App("Wordpad Tools", "wordpad"));
        Assert.Equal(2, ranked.Count);

        // only the exe name matches
        var exeOnly = RankNames("msedge", new App("Microsoft Edge", "msedge"));
        Assert.Single(exeOnly);
    }

    [Fact]
    public void FrecencyBreaksTies()
    {
        var ranked = RankNames("note",
            new App("Notepad", null, 1),
            new App("Notepad", "notepad2", 50));
        Assert.Equal(2, ranked.Count);
        var withFrecency = FuzzyMatcher.Rank("note", new[] { new App("Notes", null, 1), new App("Notes", null, 9) }, a => a.Name, a => a.Exe, a => a.Frecency);
        Assert.Equal(9, withFrecency[0].Frecency);
    }

    [Fact]
    public void ShorterNameWinsWhenScoreAndFrecencyTie()
    {
        var ranked = RankNames("calc", new App("Calculator Plus"), new App("Calculator"));
        Assert.Equal("Calculator", ranked[0]);
    }

    [Fact]
    public void EmptyQuerySortsByFrecencyThenAlphabetically()
    {
        var ranked = RankNames("",
            new App("Zoom", null, 0),
            new App("alpha", null, 0),
            new App("Beta", null, 5));
        Assert.Equal(new[] { "Beta", "alpha", "Zoom" }, ranked);
    }
}
