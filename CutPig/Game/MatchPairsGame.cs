using System;
using System.Collections.Generic;
using System.Linq;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Cơ hội": game lật cặp lá bài giống nhau (memory match) cho ĐÚNG 4 người. Lưới 4×4 = 16 ô = 8 cặp;
/// mỗi cặp là 2 lá GIỐNG HỆT (cùng rank+chất). Theo lượt (thứ tự quay random). Lật trúng cặp → được đi tiếp;
/// lật trật → úp lại + qua lượt. Hết 8 cặp / hết 120s → kết thúc. Ai tìm nhiều cặp nhất → hạng cao.
/// Engine thuần — MatchManager giữ state + timer + scoring.
/// </summary>
public static class MatchPairsGameEngine
{
    public const int GridSize = 16;   // 4×4
    public const int NumPairs = 8;

    /// <summary>Sinh lưới 16 lá: chọn 8 lá DUY NHẤT từ bộ 52, NHÂN ĐÔI mỗi lá (cặp giống hệt), xáo trộn.</summary>
    public static List<Card> BuildBoard(Random rng)
    {
        var deck = Deck.Shuffle(Deck.Build(), rng);
        var picked = deck.Take(NumPairs).ToList();          // 8 lá khác nhau
        var board = new List<Card>(GridSize);
        foreach (var c in picked) { board.Add(c); board.Add(c); }  // mỗi lá xuất hiện 2 lần
        // Xáo vị trí 16 ô.
        for (int i = board.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (board[i], board[j]) = (board[j], board[i]);
        }
        return board;
    }

    /// <summary>True nếu 2 ô là 1 cặp giống nhau (cùng rank+chất, khác vị trí).</summary>
    public static bool IsMatch(IReadOnlyList<Card> board, int a, int b)
        => a != b && a >= 0 && b >= 0 && a < board.Count && b < board.Count
           && board[a].Rank == board[b].Rank && board[a].Suit == board[b].Suit;
}
