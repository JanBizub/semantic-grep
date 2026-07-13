using Segrep.Search;

namespace Segrep.Tests.Search;

public class TermSearchHelperTests
{
    private static int CountMatches(string term, string text) =>
        TermSearch.BuildTermRegex(term).Matches(text).Count;

    [Fact]
    public void MatchesWholeWordsOnly()
    {
        Assert.Equal(2, CountMatches("cat", "The cat sat. Another cat."));
        Assert.Equal(0, CountMatches("cat", "concatenate catalog scatter"));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.Equal(3, CountMatches("keter", "Keter, KETER and keter."));
    }

    [Fact]
    public void TermsWithNonWordEdgesStillMatch()
    {
        // \b would fail on a term ending in '#'; the lookaround boundary must not.
        Assert.Equal(1, CountMatches("C#", "Written in C# today."));
        Assert.Equal(1, CountMatches(".NET", "Runs on .NET now."));
    }

    [Fact]
    public void RegexMetacharactersInTermAreEscaped()
    {
        Assert.Equal(1, CountMatches("a+b", "compute a+b here"));
        Assert.Equal(0, CountMatches("a+b", "compute aab here"));
    }

    [Fact]
    public void TermAtStartAndEndOfTextMatches()
    {
        Assert.Equal(2, CountMatches("edge", "edge in the middle edge"));
    }

    [Fact]
    public void SharedOverlapLengthFindsSuffixPrefixOverlap()
    {
        Assert.Equal(6, TermSearch.SharedOverlapLength("head modelX", "modelX tail"));
    }

    [Fact]
    public void SharedOverlapLengthIsZeroWithoutOverlap()
    {
        Assert.Equal(0, TermSearch.SharedOverlapLength("first chunk", "second chunk"));
    }

    [Fact]
    public void SharedOverlapLengthReturnsLongestOverlap()
    {
        // "abcabc" suffix vs "abcabcxyz" prefix: the full 6 chars, not just the last 3.
        Assert.Equal(6, TermSearch.SharedOverlapLength("zzabcabc", "abcabcxyz"));
    }

    [Fact]
    public void SharedOverlapLengthIsBoundedByMaxChunkOverlap()
    {
        var repeated = new string('x', 1000);
        Assert.Equal(400, TermSearch.SharedOverlapLength(repeated, repeated));
    }

    [Fact]
    public void SharedOverlapLengthHandlesShortStrings()
    {
        Assert.Equal(2, TermSearch.SharedOverlapLength("ab", "ab"));
        Assert.Equal(0, TermSearch.SharedOverlapLength("", "anything"));
    }
}
