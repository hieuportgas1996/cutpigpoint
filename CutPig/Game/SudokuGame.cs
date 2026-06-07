using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Trí tuệ": Sudoku 4×4 cho ĐÚNG 4 người. Lưới 4×4 (4 hàng × 4 cột × 4 ô-vuông 2×2);
/// mỗi hàng/cột/ô-vuông chứa đủ 1-4. CHUNG 1 đề cho cả 4 người; ai điền đủ + đúng toàn bộ + nhanh nhất → hạng cao.
/// 1 puzzle = 1 "câu" (Correct khi giải xong, ElapsedMs = lúc điền ô cuối hợp lệ). 60s.
/// Engine thuần — MatchManager giữ state + timer. Xếp hạng dùng chung MathQuizEngine.Rank.
/// </summary>
public static class SudokuGameEngine
{
    public const int N = 4;            // 4×4
    public const int Cells = 16;
    public const int TargetBlanks = 7; // ~6-8 ô trống (giữ ~9 ô cho sẵn)

    /// <summary>1 đề Sudoku: lời giải đủ 16 ô (1-4) + cờ ô cho sẵn (Given=true) / ô trống cần điền.</summary>
    public class SudokuPuzzle
    {
        public int[] Solution { get; set; } = new int[Cells]; // 1-4 mỗi ô
        public bool[] Given { get; set; } = new bool[Cells];  // true = cho sẵn (không sửa)
    }

    /// <summary>
    /// Sinh 1 đề: lời giải hợp lệ ngẫu nhiên + carve các ô trống sao cho NGHIỆM DUY NHẤT.
    /// Cố gắng đạt TargetBlanks ô trống; nếu carve thêm phá tính duy nhất thì dừng ở mức an toàn.
    /// </summary>
    public static SudokuPuzzle Build(Random rng)
    {
        var solution = GenerateSolution(rng);
        var given = Enumerable.Repeat(true, Cells).ToArray();

        // Carve ô trống theo thứ tự ngẫu nhiên, chỉ bỏ nếu vẫn còn nghiệm duy nhất.
        var order = Enumerable.Range(0, Cells).ToList();
        Shuffle(order, rng);
        int blanks = 0;
        foreach (int cell in order)
        {
            if (blanks >= TargetBlanks) break;
            given[cell] = false;
            if (CountSolutions(solution, given, 0, 2) == 1) blanks++;
            else given[cell] = true; // phá duy nhất → trả lại ô cho sẵn
        }
        return new SudokuPuzzle { Solution = solution, Given = given };
    }

    /// <summary>Sinh lời giải 4×4 hợp lệ ngẫu nhiên (backtracking từ lưới rỗng với thứ tự thử random).</summary>
    private static int[] GenerateSolution(Random rng)
    {
        var grid = new int[Cells];
        FillSolve(grid, 0, rng);
        return grid;
    }

    private static bool FillSolve(int[] grid, int pos, Random rng)
    {
        if (pos == Cells) return true;
        var vals = new List<int> { 1, 2, 3, 4 };
        Shuffle(vals, rng);
        foreach (int v in vals)
        {
            if (CanPlace(grid, pos, v))
            {
                grid[pos] = v;
                if (FillSolve(grid, pos + 1, rng)) return true;
                grid[pos] = 0;
            }
        }
        return false;
    }

    /// <summary>Đếm số nghiệm của đề (given→solution[cell], ô trống cần điền). Dừng sớm khi đạt <paramref name="cap"/>.</summary>
    private static int CountSolutions(int[] solution, bool[] given, int pos, int cap)
    {
        // Dựng lưới làm việc từ các ô cho sẵn.
        var work = new int[Cells];
        for (int i = 0; i < Cells; i++) work[i] = given[i] ? solution[i] : 0;
        return SolveCount(work, 0, cap);
    }

    private static int SolveCount(int[] grid, int pos, int cap)
    {
        while (pos < Cells && grid[pos] != 0) pos++;
        if (pos == Cells) return 1;
        int total = 0;
        for (int v = 1; v <= N; v++)
        {
            if (CanPlace(grid, pos, v))
            {
                grid[pos] = v;
                total += SolveCount(grid, pos + 1, cap);
                grid[pos] = 0;
                if (total >= cap) return total; // đủ để biết "không duy nhất"
            }
        }
        return total;
    }

    /// <summary>Đặt giá trị v vào ô pos có hợp lệ không (hàng/cột/ô-vuông 2×2 chưa có v).</summary>
    private static bool CanPlace(int[] grid, int pos, int v)
    {
        int r = pos / N, c = pos % N;
        for (int i = 0; i < N; i++)
        {
            if (grid[r * N + i] == v) return false;     // cùng hàng
            if (grid[i * N + c] == v) return false;     // cùng cột
        }
        int br = (r / 2) * 2, bc = (c / 2) * 2;          // ô-vuông 2×2
        for (int dr = 0; dr < 2; dr++)
            for (int dc = 0; dc < 2; dc++)
                if (grid[(br + dr) * N + (bc + dc)] == v) return false;
        return true;
    }

    /// <summary>True nếu fills (16 ô người chơi điền, 0=trống) khớp HOÀN TOÀN lời giải.</summary>
    public static bool IsSolved(SudokuPuzzle puzzle, IReadOnlyList<int> fills)
    {
        if (fills.Count != Cells) return false;
        for (int i = 0; i < Cells; i++)
            if (fills[i] != puzzle.Solution[i]) return false;
        return true;
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Xếp hạng GIỐNG Trí nhớ/Tính toán/Phản xạ (dùng chung MathAnswer + MathQuizEngine.Rank).</summary>
    public static List<Guid> Rank(IReadOnlyList<Guid> playerIds, IReadOnlyDictionary<Guid, List<MathAnswer>> answers)
        => MathQuizEngine.Rank(playerIds, answers);
}
