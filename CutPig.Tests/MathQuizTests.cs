using System;
using System.Collections.Generic;
using System.Linq;
using CutPig.GameEngine;
using Xunit;

namespace CutPig.Tests;

/// <summary>
/// Tests cho "Giải lao — Tính toán": sinh phép tính từ 4 chữ số (đúng đáp số, nguyên, không lẻ), 4 đáp án
/// (1 đúng + 3 nhiễu phân biệt), và xếp hạng theo (số câu đúng desc, tổng thời gian câu đúng asc).
/// </summary>
public class MathQuizTests
{
    // Eval lại biểu thức hiển thị để xác nhận Answer khớp (parser nhỏ hỗ trợ + - × ÷ và ngoặc).
    private static double EvalExpr(string s)
    {
        int pos = 0;
        s = s.Replace("×", "*").Replace("÷", "/");
        double ParseExpr()
        {
            double v = ParseTerm();
            while (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
            {
                char op = s[pos++];
                double r = ParseTerm();
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }
        double ParseTerm()
        {
            double v = ParseFactor();
            while (pos < s.Length && (s[pos] == '*' || s[pos] == '/'))
            {
                char op = s[pos++];
                double r = ParseFactor();
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }
        double ParseFactor()
        {
            while (pos < s.Length && s[pos] == ' ') pos++;
            if (s[pos] == '(')
            {
                pos++; // (
                double v = ParseExpr();
                while (pos < s.Length && s[pos] == ' ') pos++;
                pos++; // )
                while (pos < s.Length && s[pos] == ' ') pos++;
                return v;
            }
            int start = pos;
            if (s[pos] == '-') pos++;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            double num = double.Parse(s.Substring(start, pos - start));
            while (pos < s.Length && s[pos] == ' ') pos++;
            return num;
        }
        return ParseExpr();
    }

    [Fact]
    public void BuildQuestions_ProducesTwoQuestions_AnswersMatchExpressions()
    {
        var rng = new Random(12345);
        for (int trial = 0; trial < 500; trial++)
        {
            var digits = Enumerable.Range(0, 4).Select(_ => rng.Next(0, 10)).ToList();
            var qs = MathQuizEngine.BuildQuestions(digits, rng);
            Assert.Equal(MathQuizEngine.NumQuestions, qs.Count);
            foreach (var q in qs)
            {
                double eval = EvalExpr(q.Expression);
                Assert.True(Math.Abs(eval - q.Answer) < 1e-6,
                    $"Expr '{q.Expression}' eval={eval} but Answer={q.Answer}");
                Assert.Equal(MathQuizEngine.NumOptions, q.Options.Count);
                Assert.Equal(MathQuizEngine.NumOptions, q.Options.Distinct().Count()); // 4 đáp án phân biệt
                Assert.Contains(q.Answer, q.Options);
                Assert.Equal(q.Answer, q.Options[q.CorrectIndex]);
            }
        }
    }

    [Fact]
    public void BuildQuestions_UsesExactlyTheFourDigits()
    {
        var rng = new Random(7);
        var digits = new List<int> { 2, 3, 5, 7 };
        var qs = MathQuizEngine.BuildQuestions(digits, rng);
        foreach (var q in qs)
        {
            // Trích các số nguyên trong biểu thức → phải là hoán vị đúng của 4 chữ số (mỗi số 1 lần).
            var nums = System.Text.RegularExpressions.Regex.Matches(q.Expression, @"\d+")
                .Select(m => int.Parse(m.Value)).OrderBy(x => x).ToList();
            Assert.Equal(digits.OrderBy(x => x).ToList(), nums);
        }
    }

    [Fact]
    public void Rank_MoreCorrect_RanksHigher()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var ans = new Dictionary<Guid, List<MathAnswer>>
        {
            // P0: 1 đúng. P1: 2 đúng. P2: 0 đúng. P3: 2 đúng nhưng chậm hơn P1.
            [ids[0]] = new() { new() { Correct = true, ChosenIndex = 0, ElapsedMs = 100 }, new() { Correct = false, ChosenIndex = 1, ElapsedMs = 5000 } },
            [ids[1]] = new() { new() { Correct = true, ChosenIndex = 0, ElapsedMs = 200 }, new() { Correct = true, ChosenIndex = 0, ElapsedMs = 300 } },
            [ids[2]] = new() { new() { Correct = false, ChosenIndex = -1, ElapsedMs = 5000 }, new() { Correct = false, ChosenIndex = -1, ElapsedMs = 5000 } },
            [ids[3]] = new() { new() { Correct = true, ChosenIndex = 0, ElapsedMs = 1000 }, new() { Correct = true, ChosenIndex = 0, ElapsedMs = 1000 } },
        };
        var rank = MathQuizEngine.Rank(ids, ans);
        Assert.Equal(ids[1], rank[0]); // 2 đúng, tổng time câu đúng = 500 (nhanh nhất)
        Assert.Equal(ids[3], rank[1]); // 2 đúng, tổng = 2000
        Assert.Equal(ids[0], rank[2]); // 1 đúng
        Assert.Equal(ids[2], rank[3]); // 0 đúng
    }

    [Fact]
    public void Rank_SameCorrect_FasterRanksHigher()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var ans = new Dictionary<Guid, List<MathAnswer>>
        {
            [ids[0]] = new() { new() { Correct = true, ElapsedMs = 3000 } },
            [ids[1]] = new() { new() { Correct = true, ElapsedMs = 1000 } },
        };
        var rank = MathQuizEngine.Rank(ids, ans);
        Assert.Equal(ids[1], rank[0]);
        Assert.Equal(ids[0], rank[1]);
    }
}
