using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Phản xạ": game phản xạ nhanh cho ĐÚNG 4 người, dùng bộ bài 52 lá. 3 lượt, mỗi lượt:
///  1. Cooldown 3s: lưới 4×4 (16 lá ngẫu nhiên DUY NHẤT) bị ẩn ("?") + đếm ngược chuẩn bị.
///  2. Play: hiện lưới + đề "Tìm 3 lá: A B C" — player click đúng 3 lá đó (chọn đủ 3 lá = chốt), 15s.
/// ĐÚNG khi chọn đúng CẢ 3 lá chỉ định; ai đúng + nhanh nhất (theo lúc chọn lá thứ 3) hạng cao (giống Trí nhớ).
/// Engine thuần — MatchManager giữ state + timer. Card encoding dùng chung GameEngine.Card (rank 3..15).
/// </summary>
public static class ReflexGameEngine
{
    public const int GridSize = 16;      // 4×4
    public const int NumRounds = 3;
    public const int NumTargets = 3;     // tìm 3 lá

    /// <summary>1 lượt: lưới 16 lá + 3 index lá đáp án (đề bài = 3 lá đó).</summary>
    public class ReflexRound
    {
        public List<Card> Grid { get; set; } = new();
        public List<int> TargetIndexes { get; set; } = new();  // 3 ô đáp án (sorted để so khớp)
        public IEnumerable<Card> TargetCards => TargetIndexes.Select(i => Grid[i]);
    }

    /// <summary>Sinh <see cref="NumRounds"/> lượt, mỗi lượt lưới 4×4 = 16 lá DUY NHẤT + 3 ô target.</summary>
    public static List<ReflexRound> BuildRounds(Random rng)
    {
        var rounds = new List<ReflexRound>();
        for (int r = 0; r < NumRounds; r++)
            rounds.Add(BuildRound(rng));
        return rounds;
    }

    private static ReflexRound BuildRound(Random rng)
    {
        // 16 lá ngẫu nhiên DUY NHẤT từ bộ 52.
        var deck = Deck.Shuffle(Deck.Build(), rng);
        var grid = deck.Take(GridSize).ToList();
        // 3 ô target khác nhau.
        var cells = Enumerable.Range(0, GridSize).ToList();
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }
        var targets = cells.Take(NumTargets).OrderBy(x => x).ToList();
        return new ReflexRound { Grid = grid, TargetIndexes = targets };
    }

    /// <summary>True nếu tập <paramref name="picked"/> (các ô đã chọn) khớp ĐÚNG 3 ô target.</summary>
    public static bool IsCorrect(ReflexRound round, IEnumerable<int> picked)
    {
        var set = new HashSet<int>(picked);
        return set.Count == NumTargets && set.SetEquals(round.TargetIndexes);
    }

    /// <summary>Xếp hạng GIỐNG HỆT Trí nhớ/Tính toán (dùng chung MathAnswer + MathQuizEngine.Rank).</summary>
    public static List<Guid> Rank(IReadOnlyList<Guid> playerIds, IReadOnlyDictionary<Guid, List<MathAnswer>> answers)
        => MathQuizEngine.Rank(playerIds, answers);
}
