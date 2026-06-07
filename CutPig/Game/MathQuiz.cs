using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Tính toán": game tính nhẩm cho ĐÚNG 4 người. Hai pha:
///  1. PickNumber: mỗi người chọn 1 chữ số 0-9 (nhìn realtime), 10s; hết giờ auto random.
///  2. Quiz: server ghép ĐỦ 4 số đã chọn thành 2 phép tính ngẫu nhiên (có ngoặc + ưu tiên ×/ trước),
///     mỗi phép → 1 câu trắc nghiệm 4 đáp án (1 đúng). Mỗi câu 5s; ai trả lời ĐÚNG và NHANH NHẤT hạng cao.
/// Xếp hạng cuối: nhiều câu đúng hơn → hạng cao; bằng số câu đúng → tổng thời gian (cho các câu đúng) ít hơn → hạng cao;
/// bằng nốt → giữ thứ tự (ổn định). Engine thuần (không state mạng) — MatchManager giữ state + timer.
/// </summary>
public enum MathOp { Add, Sub, Mul, Div }

/// <summary>
/// 1 token hiển thị trong biểu thức. IsCard=true → 1 lá bài (Rank/Suit, dùng cho chữ số 1-9, suit random,
/// 1→A/Xì rank14, 2→rank15, 3-9→rank đó); IsCard=false → text thuần (toán tử, ngoặc, hoặc số "0").
/// </summary>
public class MathToken
{
    public bool IsCard { get; set; }
    public string Text { get; set; } = "";   // chỉ ý nghĩa khi !IsCard ("+", "(", "0"…)
    public int Rank { get; set; }             // chỉ ý nghĩa khi IsCard (3..15)
    public int Suit { get; set; }             // chỉ ý nghĩa khi IsCard (0..3, random)

    public static MathToken T(string text) => new() { IsCard = false, Text = text };
}

/// <summary>1 phép tính: biểu thức hiển thị (có ngoặc) + đáp số đúng + 4 đáp án trắc nghiệm.</summary>
public class MathQuestion
{
    public string Expression { get; set; } = "";   // vd "1 + (3 × 5) - 5" (text thuần, để log/dedup)
    public List<MathToken> ExprTokens { get; set; } = new(); // biểu thức dạng token (số 1-9 → lá bài)
    public int Answer { get; set; }                 // đáp số đúng
    public List<int> Options { get; set; } = new(); // 4 đáp án (đã xáo), chứa Answer
    public int CorrectIndex { get; set; }           // index của Answer trong Options
}

/// <summary>Một lần trả lời của 1 player cho 1 câu: chọn index nào + đúng không + thời gian (ms từ lúc mở câu).</summary>
public class MathAnswer
{
    public int ChosenIndex { get; set; } = -1;      // -1 = chưa/không trả lời
    public bool Correct { get; set; }
    public long ElapsedMs { get; set; }             // thời gian trả lời (ms); lớn nếu không trả lời
    public bool Answered => ChosenIndex >= 0;
}

public static class MathQuizEngine
{
    public const int NumQuestions = 2;
    public const int NumOptions = 4;

    private static readonly MathOp[] AllOps = { MathOp.Add, MathOp.Sub, MathOp.Mul, MathOp.Div };

    private static string OpSymbol(MathOp op) => op switch
    {
        MathOp.Add => "+",
        MathOp.Sub => "-",
        MathOp.Mul => "×",
        MathOp.Div => "÷",
        _ => "?",
    };

    /// <summary>
    /// Sinh <see cref="NumQuestions"/> phép tính KHÁC NHAU dùng ĐÚNG 4 chữ số đã cho (giữ nguyên các số,
    /// xáo thứ tự + chọn toán tử + cách đặt ngoặc ngẫu nhiên). Bảo đảm: chia hết (kết quả nguyên),
    /// không chia 0, kết quả khác nhau giữa 2 câu nếu có thể.
    /// </summary>
    public static List<MathQuestion> BuildQuestions(IReadOnlyList<int> digits, Random rng)
    {
        var questions = new List<MathQuestion>();
        var seenExpr = new HashSet<string>();
        var seenAnswer = new HashSet<int>();
        int guard = 0;
        while (questions.Count < NumQuestions && guard++ < 4000)
        {
            var q = TryBuildOne(digits, rng);
            if (q == null) continue;
            if (seenExpr.Contains(q.Expression)) continue;
            // Cố gắng cho 2 câu ra đáp số khác nhau (đỡ nhàm) — nhưng nếu bí thì vẫn chấp nhận trùng.
            if (questions.Count > 0 && seenAnswer.Contains(q.Answer) && guard < 2000) continue;
            seenExpr.Add(q.Expression);
            seenAnswer.Add(q.Answer);
            BuildOptions(q, rng);
            questions.Add(q);
        }
        // Fallback cực hiếm (không sinh đủ): lặp lại câu đầu (vẫn hợp lệ) cho đủ số lượng.
        while (questions.Count < NumQuestions && questions.Count > 0)
            questions.Add(CloneWithFreshOptions(questions[0], rng));
        return questions;
    }

    /// <summary>Sinh 1 phép tính hợp lệ từ 4 số (hoặc null nếu tổ hợp random này không hợp lệ — caller retry).</summary>
    private static MathQuestion? TryBuildOne(IReadOnlyList<int> digits, Random rng)
    {
        // Xáo thứ tự 4 số.
        var nums = digits.ToList();
        for (int i = nums.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (nums[i], nums[j]) = (nums[j], nums[i]);
        }
        var ops = new[] { AllOps[rng.Next(4)], AllOps[rng.Next(4)], AllOps[rng.Next(4)] };

        // Mỗi toán hạng (theo VỊ TRÍ a/b/c/d) → 1 token HIỂN THỊ: số 1-9 thành lá bài (suit random), số 0 giữ "0".
        var tokA = OperandToken(nums[0], rng);
        var tokB = OperandToken(nums[1], rng);
        var tokC = OperandToken(nums[2], rng);
        var tokD = OperandToken(nums[3], rng);
        // Token SỐ (giá trị thuần) — chỉ để dựng chuỗi Expression numeric (log/dedup/test).
        MathToken numTok(int n) => MathToken.T(n.ToString());

        // 5 dạng đặt ngoặc cho 4 toán hạng a b c d với 3 toán tử (chọn ngẫu nhiên 1 dạng).
        // Mỗi dạng là 1 cây nhị phân; eval theo dạng (KHÔNG theo precedence chuỗi — ngoặc đã định hình thứ tự).
        int shape = rng.Next(5);
        double a = nums[0], b = nums[1], c = nums[2], d = nums[3];
        double val = shape switch
        {
            0 => ApplyOp(ApplyOp(ApplyOp(a, ops[0], b), ops[1], c), ops[2], d),                 // ((a∘b)∘c)∘d
            1 => ApplyOp(ApplyOp(a, ops[0], ApplyOp(b, ops[1], c)), ops[2], d),                 // (a∘(b∘c))∘d
            2 => ApplyOp(ApplyOp(a, ops[0], b), ops[1], ApplyOp(c, ops[2], d)),                 // (a∘b)∘(c∘d)
            3 => ApplyOp(a, ops[0], ApplyOp(ApplyOp(b, ops[1], c), ops[2], d)),                 // a∘((b∘c)∘d)
            _ => ApplyOp(a, ops[0], ApplyOp(b, ops[1], ApplyOp(c, ops[2], d))),                 // a∘(b∘(c∘d))
        };

        if (double.IsNaN(val) || double.IsInfinity(val)) return null;
        // Chỉ nhận kết quả nguyên (không lẻ do chia) và trong khoảng hợp lý hiển thị trắc nghiệm.
        if (Math.Abs(val - Math.Round(val)) > 1e-9) return null;
        int answer = (int)Math.Round(val);
        if (answer < -200 || answer > 999) return null;

        var tokens = BuildTokens(shape, ops, tokA, tokB, tokC, tokD);
        // Chuỗi numeric (giá trị thuần, có khoảng trắng) cho Expression — giữ tương thích log/dedup/test.
        var numTokens = BuildTokens(shape, ops, numTok(nums[0]), numTok(nums[1]), numTok(nums[2]), numTok(nums[3]));
        return new MathQuestion { Expression = NumericString(numTokens), ExprTokens = tokens, Answer = answer };
    }

    /// <summary>Chữ số → token hiển thị: 1-9 thành lá bài (1→A rank14, 2→rank15, 3-9→rank đó; suit random 0-3); 0 giữ text "0".</summary>
    private static MathToken OperandToken(int digit, Random rng)
    {
        if (digit == 0) return MathToken.T("0");
        int rank = digit == 1 ? 14 : digit == 2 ? 15 : digit; // 1→A(14), 2→"2"(15), 3-9→rank
        return new MathToken { IsCard = true, Rank = rank, Suit = rng.Next(4) };
    }

    /// <summary>Ghép token toán hạng + toán tử + ngoặc theo 1 trong 5 dạng cây (khớp shape ở TryBuildOne).</summary>
    private static List<MathToken> BuildTokens(int shape, MathOp[] ops, MathToken a, MathToken b, MathToken c, MathToken d)
    {
        MathToken op(int i) => MathToken.T(OpSymbol(ops[i]));
        MathToken lp() => MathToken.T("(");
        MathToken rp() => MathToken.T(")");
        return shape switch
        {
            // ((a ∘ b) ∘ c) ∘ d
            0 => new() { lp(), lp(), a, op(0), b, rp(), op(1), c, rp(), op(2), d },
            // (a ∘ (b ∘ c)) ∘ d
            1 => new() { lp(), a, op(0), lp(), b, op(1), c, rp(), rp(), op(2), d },
            // (a ∘ b) ∘ (c ∘ d)
            2 => new() { lp(), a, op(0), b, rp(), op(1), lp(), c, op(2), d, rp() },
            // a ∘ ((b ∘ c) ∘ d)
            3 => new() { a, op(0), lp(), lp(), b, op(1), c, rp(), op(2), d, rp() },
            // a ∘ (b ∘ (c ∘ d))
            _ => new() { a, op(0), lp(), b, op(1), lp(), c, op(2), d, rp(), rp() },
        };
    }

    /// <summary>
    /// Chuỗi numeric có khoảng trắng từ các token SỐ thuần (toán tử/ngoặc + số) — dùng cho Expression
    /// (log/dedup/test). Quy tắc cách: cách quanh toán tử, không cách sau "(" / trước ")".
    /// </summary>
    private static string NumericString(List<MathToken> tokens)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            string s = tokens[i].Text;
            bool isOp = s is "+" or "-" or "×" or "÷";
            if (isOp) { sb.Append(' ').Append(s).Append(' '); }
            else sb.Append(s);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Áp toán tử. Chia: trả NaN nếu chia 0 hoặc không chia hết (→ caller loại bỏ phép này).</summary>
    private static double ApplyOp(double l, MathOp op, double r) => op switch
    {
        MathOp.Add => l + r,
        MathOp.Sub => l - r,
        MathOp.Mul => l * r,
        MathOp.Div => (r == 0 || Math.Abs(l % r) > 1e-9) ? double.NaN : l / r,
        _ => double.NaN,
    };

    /// <summary>Sinh 4 đáp án: đáp số đúng + 3 nhiễu gần đúng (lệch nhỏ), xáo trộn. Set CorrectIndex.</summary>
    private static void BuildOptions(MathQuestion q, Random rng)
    {
        var set = new HashSet<int> { q.Answer };
        int guard = 0;
        while (set.Count < NumOptions && guard++ < 200)
        {
            // Nhiễu: lệch ±1..±9 quanh đáp số (đôi khi ±10/±gấp đôi cho đa dạng).
            int delta = rng.Next(2) == 0 ? rng.Next(1, 10) : rng.Next(1, 4) * (rng.Next(2) == 0 ? 5 : 2);
            if (rng.Next(2) == 0) delta = -delta;
            set.Add(q.Answer + delta);
        }
        var opts = set.ToList();
        // Xáo trộn.
        for (int i = opts.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (opts[i], opts[j]) = (opts[j], opts[i]);
        }
        q.Options = opts;
        q.CorrectIndex = opts.IndexOf(q.Answer);
    }

    private static MathQuestion CloneWithFreshOptions(MathQuestion src, Random rng)
    {
        var q = new MathQuestion { Expression = src.Expression, ExprTokens = src.ExprTokens, Answer = src.Answer };
        BuildOptions(q, rng);
        return q;
    }

    /// <summary>
    /// Xếp hạng <paramref name="playerIds"/> theo kết quả các câu. Mỗi player có danh sách <see cref="MathAnswer"/>
    /// (1 phần tử / câu). Hạng cao = nhiều câu ĐÚNG hơn; bằng → tổng thời gian các câu ĐÚNG ít hơn; bằng nốt → tổng
    /// thời gian MỌI câu ít hơn; bằng nữa → giữ thứ tự đầu vào. Trả về list userId từ hạng 1..n.
    /// </summary>
    public static List<Guid> Rank(IReadOnlyList<Guid> playerIds, IReadOnlyDictionary<Guid, List<MathAnswer>> answers)
    {
        return playerIds
            .Select((id, idx) => (id, idx, ans: answers.TryGetValue(id, out var a) ? a : new List<MathAnswer>()))
            .OrderByDescending(x => x.ans.Count(a => a.Correct))                       // nhiều câu đúng trước
            .ThenBy(x => x.ans.Where(a => a.Correct).Sum(a => a.ElapsedMs))            // các câu đúng: tổng time ít hơn
            .ThenBy(x => x.ans.Sum(a => a.ElapsedMs))                                  // tie-break: tổng time mọi câu
            .ThenBy(x => x.idx)                                                        // ổn định
            .Select(x => x.id)
            .ToList();
    }
}
