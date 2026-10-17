using NetcShared;
using Xunit;

namespace NetcTests;

public class TestScoringTests
{
    private static TestQuestion Q(QuestionKind kind, double points = 1) => new() { Kind = kind, Points = points, Text = "q" };

    private static TestDefinition Def(params TestQuestion[] qs) => new() { Questions = qs.ToList() };

    [Fact]
    public void Single_UsesOriginalIndex()
    {
        var q = Q(QuestionKind.Single);
        q.Options = new() { "a", "b", "c" };
        q.CorrectIndex = 1;

        var good = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, SelectedIndices = { 1 } } }, true, "submit", 0, "s");
        Assert.Equal(1, good.Score);
        Assert.True(good.Answers[0].Correct);

        var bad = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, SelectedIndices = { 2 } } }, true, "submit", 0, "s");
        Assert.Equal(0, bad.Score);
        Assert.False(bad.Answers[0].Correct);
    }

    [Fact]
    public void Multi_PartialCredit_NeverNegative()
    {
        var q = Q(QuestionKind.Multi, 2);
        q.Options = new() { "a", "b", "c" };
        q.CorrectIndices = new() { 0, 1 };
        q.PartialCredit = true;

        // 1 верный из 2 и 1 лишний → (1-1)/2 = 0.
        var r = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, SelectedIndices = { 0, 2 } } }, true, "submit", 0, "s");
        Assert.Equal(0, r.Score);

        // Только верный набор → полный балл.
        var full = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, SelectedIndices = { 0, 1 } } }, true, "submit", 0, "s");
        Assert.Equal(2, full.Score);
        Assert.True(full.Answers[0].Correct);
    }

    [Fact]
    public void Numeric_Tolerance()
    {
        var q = Q(QuestionKind.Numeric);
        q.CorrectNumber = 10;
        q.Tolerance = 0.5;

        Assert.Equal(1, TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, NumberSet = true, NumberAnswer = 10.4 } }, true, "submit", 0, "s").Score);
        Assert.Equal(0, TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, NumberSet = true, NumberAnswer = 10.6 } }, true, "submit", 0, "s").Score);
        // Ответ не задан (NumberSet=false) — не верно.
        Assert.Equal(0, TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, NumberAnswer = 10 } }, true, "submit", 0, "s").Score);
    }

    [Fact]
    public void Matching_SameIndexIsCorrect()
    {
        var q = Q(QuestionKind.Matching, 3);
        q.MatchLeft = new() { "a", "b", "c" };
        q.MatchRight = new() { "x", "y", "z" };

        var ok = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, MatchPicks = { 0, 1, 2 } } }, true, "submit", 0, "s");
        Assert.Equal(3, ok.Score);

        var partial = TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, MatchPicks = { 0, 1, 0 } } }, true, "submit", 0, "s");
        Assert.Equal(0, partial.Score);
    }

    [Fact]
    public void Essay_IsPendingManual_AndNotInMax()
    {
        var q = Q(QuestionKind.Essay, 5);
        var r = TestScoring.Evaluate(Def(q), Array.Empty<TestAnswer>(), true, "submit", 0, "s");
        Assert.Equal(1, r.ManualCount);
        Assert.Equal(5, r.PendingManual);
        Assert.Equal(0, r.MaxScore);
    }

    [Fact]
    public void ShortText_IgnoreCase_AndWhitespace()
    {
        var q = Q(QuestionKind.ShortText);
        q.CorrectText = new() { "Hello  World" };
        q.IgnoreCase = true;
        Assert.True(TestScoring.Evaluate(Def(q), new[] { new TestAnswer { QuestionIndex = 0, TextAnswer = "hello world" } }, true, "submit", 0, "s").Answers[0].Correct);
    }

    [Fact]
    public void DuplicateQuestionIndex_UsesLast()
    {
        var q = Q(QuestionKind.Single);
        q.Options = new() { "a", "b" };
        q.CorrectIndex = 1;
        var answers = new[]
        {
            new TestAnswer { QuestionIndex = 0, SelectedIndices = { 0 } },
            new TestAnswer { QuestionIndex = 0, SelectedIndices = { 1 } }
        };
        Assert.Equal(1, TestScoring.Evaluate(Def(q), answers, true, "submit", 0, "s").Score);
    }

    [Fact]
    public void Passed_WhenThresholdNotSet()
    {
        var def = Def(Q(QuestionKind.Single));
        def.PassPercent = 0;
        var r = TestScoring.Evaluate(def, Array.Empty<TestAnswer>(), true, "submit", 0, "s");
        Assert.True(r.Passed);
    }
}
