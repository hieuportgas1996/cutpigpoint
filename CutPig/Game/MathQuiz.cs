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

/// <summary>1 phép tính: biểu thức hiển thị (có ngoặc) + đáp số đúng + 4 đáp án trắc nghiệm.</summary>
public class MathQuestion
{
    public string Expression { get; set; } = "";   // vd "1 + (3 × 5) - 5"
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

        // 5 dạng đặt ngoặc cho 4 toán hạng a b c d với 3 toán tử (chọn ngẫu nhiên 1 dạng).
        // Mỗi dạng là 1 cây nhị phân; eval theo dạng (KHÔNG theo precedence chuỗi — ngoặc đã định hình thứ tự).
        int shape = rng.Next(5);
        double a = nums[0], b = nums[1], c = nums[2], d = nums[3];
        var (val, expr) = shape switch
        {
            // ((a ∘ b) ∘ c) ∘ d
            0 => EvalLeftChain(a, ops, b, c, d),
            // (a ∘ (b ∘ c)) ∘ d
            1 => EvalChainLeftGroup(a, ops, b, c, d),
            // (a ∘ b) ∘ (c ∘ d)
            2 => EvalTwoGroups(a, ops, b, c, d),
            // a ∘ ((b ∘ c) ∘ d)
            3 => EvalRightHeavy(a, ops, b, c, d),
            // a ∘ (b ∘ (c ∘ d))
            _ => EvalRightChain(a, ops, b, c, d),
        };

        if (double.IsNaN(val) || double.IsInfinity(val)) return null;
        // Chỉ nhận kết quả nguyên (không lẻ do chia) và trong khoảng hợp lý hiển thị trắc nghiệm.
        if (Math.Abs(val - Math.Round(val)) > 1e-9) return null;
        int answer = (int)Math.Round(val);
        if (answer < -200 || answer > 999) return null;
        return new MathQuestion { Expression = expr, Answer = answer };
    }

    private static string F(double x) => ((int)Math.Round(x)).ToString();

    // ---- Các helper eval: trả về (giá trị, biểu thức chuỗi). Kiểm tra chia hết/chia-0 trong ApplyOp. ----

    // ((a ∘ b) ∘ c) ∘ d
    private static (double, string) EvalLeftChain(double a, MathOp[] ops, double b, double c, double d)
    {
        double l1 = ApplyOp(a, ops[0], b);
        double l2 = ApplyOp(l1, ops[1], c);
        double v = ApplyOp(l2, ops[2], d);
        string e = $"(({F(a)} {OpSymbol(ops[0])} {F(b)}) {OpSymbol(ops[1])} {F(c)}) {OpSymbol(ops[2])} {F(d)}";
        return (v, e);
    }

    // (a ∘ (b ∘ c)) ∘ d
    private static (double, string) EvalChainLeftGroup(double a, MathOp[] ops, double b, double c, double d)
    {
        double inner = ApplyOp(b, ops[1], c);
        double left = ApplyOp(a, ops[0], inner);
        double v = ApplyOp(left, ops[2], d);
        string e = $"({F(a)} {OpSymbol(ops[0])} ({F(b)} {OpSymbol(ops[1])} {F(c)})) {OpSymbol(ops[2])} {F(d)}";
        return (v, e);
    }

    // (a ∘ b) ∘ (c ∘ d)
    private static (double, string) EvalTwoGroups(double a, MathOp[] ops, double b, double c, double d)
    {
        double l = ApplyOp(a, ops[0], b);
        double r = ApplyOp(c, ops[2], d);
        double v = ApplyOp(l, ops[1], r);
        string e = $"({F(a)} {OpSymbol(ops[0])} {F(b)}) {OpSymbol(ops[1])} ({F(c)} {OpSymbol(ops[2])} {F(d)})";
        return (v, e);
    }

    // a ∘ ((b ∘ c) ∘ d)
    private static (double, string) EvalRightHeavy(double a, MathOp[] ops, double b, double c, double d)
    {
        double inner = ApplyOp(b, ops[1], c);
        double right = ApplyOp(inner, ops[2], d);
        double v = ApplyOp(a, ops[0], right);
        string e = $"{F(a)} {OpSymbol(ops[0])} (({F(b)} {OpSymbol(ops[1])} {F(c)}) {OpSymbol(ops[2])} {F(d)})";
        return (v, e);
    }

    // a ∘ (b ∘ (c ∘ d))
    private static (double, string) EvalRightChain(double a, MathOp[] ops, double b, double c, double d)
    {
        double inner = ApplyOp(c, ops[2], d);
        double right = ApplyOp(b, ops[1], inner);
        double v = ApplyOp(a, ops[0], right);
        string e = $"{F(a)} {OpSymbol(ops[0])} ({F(b)} {OpSymbol(ops[1])} ({F(c)} {OpSymbol(ops[2])} {F(d)}))";
        return (v, e);
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
        var q = new MathQuestion { Expression = src.Expression, Answer = src.Answer };
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
