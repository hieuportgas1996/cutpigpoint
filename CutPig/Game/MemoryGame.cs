using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Trí nhớ": game ghi nhớ logo CLB bóng đá cho ĐÚNG 4 người. 2 pha:
///  1. View: hiện lưới 3×3 gồm 9 logo CLB ngẫu nhiên (khác nhau), đếm ngược 10s cho quan sát/ghi nhớ.
///  2. Quiz: ẩn lưới, hỏi 3 câu "Ô số X là đội nào?" — mỗi câu 4 đáp án LOGO (1 đúng + 3 nhiễu từ 8 đội còn lại trong lưới).
/// Ai trả lời ĐÚNG và NHANH NHẤT hạng cao (giống game Tính toán). Engine thuần — MatchManager giữ state + timer.
/// Mã CLB ("slug") khớp tên file logo client: client/src/img/club/&lt;slug&gt;.png.
/// </summary>
public static class MemoryGameEngine
{
    public const int GridSize = 9;       // 3×3
    public const int NumQuestions = 3;
    public const int NumOptions = 4;

    /// <summary>Danh mục CLB: slug (= tên file logo) → tên hiển thị. Mirror client CLUB_NAME.</summary>
    public static readonly IReadOnlyList<(string Slug, string Name)> Clubs = new[]
    {
        ("ajax", "Ajax"),
        ("alt", "Atlético Madrid"),
        ("arsenal", "Arsenal"),
        ("aston", "Aston Villa"),
        ("barca", "Barcelona"),
        ("bayern", "Bayern Munich"),
        ("bour", "Bournemouth"),
        ("brigton", "Brighton"),
        ("chelsea", "Chelsea"),
        ("dortmund", "Dortmund"),
        ("liv", "Liverpool"),
        ("mc", "Man City"),
        ("mu", "Man United"),
        ("new", "Newcastle"),
        ("psg", "PSG"),
        ("real", "Real Madrid"),
        ("tot", "Tottenham"),
        ("westham", "West Ham"),
    };

    /// <summary>1 câu hỏi: vị trí ô (0-8) được hỏi + slug đúng + 4 slug đáp án (đã xáo) + index đúng.</summary>
    public class MemoryQuestion
    {
        public int CellIndex { get; set; }              // ô 0-8 đang hỏi
        public string AnswerSlug { get; set; } = "";    // slug đúng
        public List<string> Options { get; set; } = new(); // 4 slug đáp án (chứa AnswerSlug)
        public int CorrectIndex { get; set; }
    }

    /// <summary>State 1 ván Trí nhớ: lưới 9 slug + 3 câu hỏi.</summary>
    public class MemoryBoard
    {
        public List<string> Grid { get; set; } = new();         // 9 slug theo ô 0-8
        public List<MemoryQuestion> Questions { get; set; } = new();
    }

    /// <summary>Random 9 CLB khác nhau vào lưới 3×3 + sinh 3 câu hỏi (3 ô khác nhau, nhiễu từ 8 đội còn lại).</summary>
    public static MemoryBoard BuildBoard(Random rng)
    {
        // Chọn 9 slug khác nhau từ danh mục.
        var pool = Clubs.Select(c => c.Slug).ToList();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        var grid = pool.Take(GridSize).ToList();

        // Chọn NumQuestions ô KHÁC nhau để hỏi.
        var cells = Enumerable.Range(0, GridSize).ToList();
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }
        var askCells = cells.Take(NumQuestions).ToList();

        var board = new MemoryBoard { Grid = grid };
        foreach (var cell in askCells)
        {
            var answer = grid[cell];
            // 3 nhiễu từ 8 đội CÒN LẠI trong lưới.
            var distractPool = grid.Where(s => s != answer).ToList();
            for (int i = distractPool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (distractPool[i], distractPool[j]) = (distractPool[j], distractPool[i]);
            }
            var options = distractPool.Take(NumOptions - 1).ToList();
            options.Add(answer);
            // Xáo đáp án.
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }
            board.Questions.Add(new MemoryQuestion
            {
                CellIndex = cell,
                AnswerSlug = answer,
                Options = options,
                CorrectIndex = options.IndexOf(answer),
            });
        }
        return board;
    }

    /// <summary>
    /// Xếp hạng theo (số câu đúng desc, tổng thời gian câu đúng asc, tổng thời gian mọi câu asc, thứ tự ổn định).
    /// GIỐNG hệt MathQuizEngine.Rank — dùng chung kiểu MathAnswer cho lời giải.
    /// </summary>
    public static List<Guid> Rank(IReadOnlyList<Guid> playerIds, IReadOnlyDictionary<Guid, List<MathAnswer>> answers)
        => MathQuizEngine.Rank(playerIds, answers);
}
