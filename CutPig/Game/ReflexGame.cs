using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Phản xạ": game phản xạ nhanh cho ĐÚNG 4 người. 3 lượt, mỗi lượt:
///  1. Cooldown 3s: hiện lưới 3×3 (9 hình ngẫu nhiên, mỗi ô 1 cặp (hình,màu) DUY NHẤT) + đếm ngược chuẩn bị.
///  2. Play: hiện đề "Tìm hình &lt;shape&gt; màu &lt;color&gt;" — player click đúng ô đó, 10s.
/// Ai click ĐÚNG ô và NHANH NHẤT hạng cao (giống Trí nhớ). Engine thuần — MatchManager giữ state + timer.
/// Mã shape/color khớp client để vẽ SVG + hiện tên tiếng Việt.
/// </summary>
public static class ReflexGameEngine
{
    public const int GridSize = 9;       // 3×3
    public const int NumRounds = 3;

    /// <summary>Hình + tên hiển thị (mirror client SHAPE_NAME).</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> Shapes = new[]
    {
        ("circle", "hình tròn"),
        ("square", "hình vuông"),
        ("oval", "hình bầu dục"),
        ("rectangle", "hình chữ nhật"),
        ("triangle", "hình tam giác"),
        ("trapezoid", "hình thang"),
        ("pentagon", "hình ngũ giác"),
        ("star", "hình ngôi sao"),
    };

    /// <summary>Màu + tên hiển thị + mã hex (mirror client COLOR).</summary>
    public static readonly IReadOnlyList<(string Key, string Name, string Hex)> Colors = new[]
    {
        ("red", "đỏ", "#e23b3b"),
        ("blue", "xanh dương", "#3b82f6"),
        ("green", "xanh lá", "#22c55e"),
        ("yellow", "vàng", "#f5c518"),
        ("orange", "cam", "#f97316"),
        ("purple", "tím", "#a855f7"),
        ("pink", "hồng", "#ec4899"),
        ("cyan", "xanh ngọc", "#06b6d4"),
        ("white", "trắng", "#f3f4f6"),
        ("brown", "nâu", "#92633a"),
    };

    /// <summary>1 ô trong lưới: hình + màu.</summary>
    public class ReflexCell
    {
        public string Shape { get; set; } = "";
        public string Color { get; set; } = "";
    }

    /// <summary>1 lượt: lưới 9 ô + index ô đáp án (đề bài = shape+color của ô đó).</summary>
    public class ReflexRound
    {
        public List<ReflexCell> Grid { get; set; } = new();
        public int TargetIndex { get; set; }
        public string TargetShape => Grid[TargetIndex].Shape;
        public string TargetColor => Grid[TargetIndex].Color;
    }

    /// <summary>Sinh <see cref="NumRounds"/> lượt, mỗi lượt 1 lưới 3×3 gồm 9 cặp (hình,màu) DUY NHẤT + 1 ô target.</summary>
    public static List<ReflexRound> BuildRounds(Random rng)
    {
        var rounds = new List<ReflexRound>();
        for (int r = 0; r < NumRounds; r++)
            rounds.Add(BuildRound(rng));
        return rounds;
    }

    private static ReflexRound BuildRound(Random rng)
    {
        // Tạo mọi cặp (shape,color) rồi xáo, lấy 9 cặp DUY NHẤT.
        var pairs = new List<(string Shape, string Color)>();
        foreach (var s in Shapes)
            foreach (var c in Colors)
                pairs.Add((s.Key, c.Key));
        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }
        var grid = pairs.Take(GridSize)
            .Select(p => new ReflexCell { Shape = p.Shape, Color = p.Color })
            .ToList();
        return new ReflexRound { Grid = grid, TargetIndex = rng.Next(GridSize) };
    }

    /// <summary>Xếp hạng GIỐNG HỆT Trí nhớ/Tính toán (dùng chung MathAnswer + MathQuizEngine.Rank).</summary>
    public static List<Guid> Rank(IReadOnlyList<Guid> playerIds, IReadOnlyDictionary<Guid, List<MathAnswer>> answers)
        => MathQuizEngine.Rank(playerIds, answers);
}
