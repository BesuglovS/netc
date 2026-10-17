namespace NetcShared;

/// <summary>
/// Единая логика проверки ответов и подсчёта баллов теста.
/// Используется агентом (автопроверка при сдаче) и клиентом учителя (пересчёт/предпросмотр).
/// </summary>
public static class TestScoring
{
    /// <summary>Проверить ответы теста и собрать результат. Индексы ответов — в исходном порядке вопросов.</summary>
    public static TestResult Evaluate(
        TestDefinition test,
        IReadOnlyList<TestAnswer> answers,
        bool completed,
        string endReason,
        long durationMs,
        string student)
    {
        var result = new TestResult
        {
            TestId = test.Id,
            Title = test.Title,
            Student = student,
            Completed = completed,
            EndReason = endReason,
            DurationMs = durationMs
        };

        double score = 0, max = 0, pending = 0;
        int manual = 0;
        // GroupBy — на случай дубликатов QuestionIndex (несколько ответов на один вопрос).
        var byIndex = answers.GroupBy(a => a.QuestionIndex).ToDictionary(g => g.Key, g => g.Last());

        for (int i = 0; i < test.Questions.Count; i++)
        {
            var q = test.Questions[i];
            var a = byIndex.TryGetValue(i, out var found) ? found : new TestAnswer { QuestionIndex = i };

            a.Correct = false;
            a.Manual = false;
            a.Awarded = 0;

            switch (q.Kind)
            {
                case QuestionKind.Essay:
                    a.Manual = true;
                    manual++;
                    pending += q.Points;
                    break;

                case QuestionKind.Numeric:
                    max += q.Points;
                    if (a.NumberSet && Math.Abs(a.NumberAnswer - q.CorrectNumber) <= Math.Max(q.Tolerance, 1e-9))
                    {
                        a.Correct = true;
                        a.Awarded = q.Points;
                    }
                    break;

                case QuestionKind.ShortText:
                    max += q.Points;
                    if (MatchesText(q, a.TextAnswer)) { a.Correct = true; a.Awarded = q.Points; }
                    break;

                case QuestionKind.Multi:
                {
                    max += q.Points;
                    var correct = new HashSet<int>(CorrectSet(q));
                    var selected = new HashSet<int>(a.SelectedIndices);
                    if (correct.Count > 0 && selected.SetEquals(correct))
                    {
                        a.Correct = true;
                        a.Awarded = q.Points;
                    }
                    else if (q.PartialCredit && correct.Count > 0)
                    {
                        int good = selected.Count(correct.Contains);
                        int bad = selected.Count - good;
                        double frac = Math.Max(0, (double)(good - bad) / correct.Count);
                        a.Awarded = Math.Round(q.Points * frac, 2);
                    }
                    break;
                }

                case QuestionKind.Matching:
                {
                    max += q.Points;
                    int pairs = q.MatchLeft.Count;
                    if (pairs > 0)
                    {
                        int good = 0;
                        for (int p = 0; p < pairs; p++)
                        {
                            // Верная пара — тот же индекс (левый[i] ↔ правый[i]).
                            if (p < a.MatchPicks.Count && a.MatchPicks[p] == p) good++;
                        }
                        if (good == pairs) { a.Correct = true; a.Awarded = q.Points; }
                        else if (q.PartialCredit && good > 0) a.Awarded = Math.Round(q.Points * good / pairs, 2);
                    }
                    break;
                }

                default: // Single, TrueFalse
                {
                    max += q.Points;
                    int correct = q.CorrectIndices.Count > 0 ? q.CorrectIndices[0] : q.CorrectIndex;
                    if (correct >= 0 && a.SelectedIndices.Count > 0 && a.SelectedIndices[0] == correct)
                    {
                        a.Correct = true;
                        a.Awarded = q.Points;
                    }
                    break;
                }
            }

            score += a.Awarded;
        }

        result.Score = Math.Round(score, 2);
        result.MaxScore = Math.Round(max, 2);
        result.PendingManual = Math.Round(pending, 2);
        result.ManualCount = manual;
        // Проходной порог не задан (0) — тест считается пройденным; иначе сравниваем процент.
        result.Passed = test.PassPercent <= 0 || result.Percent >= test.PassPercent;
        result.Answers = test.Questions
            .Select((_, i) => byIndex.TryGetValue(i, out var a) ? a : new TestAnswer { QuestionIndex = i })
            .ToList();
        return result;
    }

    /// <summary>Индексы верных вариантов вопроса (Single/TrueFalse дают один элемент).</summary>
    public static List<int> CorrectSet(TestQuestion q)
    {
        if (q.CorrectIndices.Count > 0) return q.CorrectIndices;
        return q.CorrectIndex >= 0 ? new List<int> { q.CorrectIndex } : new List<int>();
    }

    /// <summary>Совпадает ли текстовый ответ с одним из допустимых.</summary>
    public static bool MatchesText(TestQuestion q, string? answer)
    {
        string a = NormalizeText(answer);
        var cmp = q.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var variant in q.CorrectText)
        {
            if (a.Equals(NormalizeText(variant), cmp)) return true;
        }
        return false;
    }

    /// <summary>Обрезает пробелы по краям и схлопывает повторяющиеся пробелы внутри (для сравнения).</summary>
    private static string NormalizeText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(c);
                space = false;
            }
        }
        return sb.ToString();
    }
}
