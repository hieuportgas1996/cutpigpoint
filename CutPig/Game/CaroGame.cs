using System;
using System.Collections.Generic;

namespace CutPig.GameEngine;

/// <summary>
/// "Giải lao — Caro đồng đội": cờ caro 10×10 cho ĐÚNG 4 người chia 2 team (2 người/team).
/// Team 1 ký hiệu X, team 2 ký hiệu O. Đánh xen kẽ theo lượt X→O→X→O.
/// Thắng = 5 quân (hoặc hơn) liên tiếp theo ngang/dọc/chéo chính/chéo phụ.
/// KHÔNG áp luật chặn 2 đầu: cứ đủ 5 liên tiếp là thắng dù bị chặn cả 2 đầu.
/// Engine thuần — MatchManager giữ state (board, lượt, deadline) + scoring.
/// Ô trên board: 0 = trống, 1 = team X (1), 2 = team O (2).
/// </summary>
public static class CaroGameEngine
{
    public const int Size = 10;            // 10×10
    public const int CellCount = Size * Size;  // 100
    public const int WinLength = 5;        // 5 quân liên tiếp

    /// <summary>Tạo bàn cờ trống 100 ô (giá trị 0).</summary>
    public static int[] BuildBoard() => new int[CellCount];

    /// <summary>
    /// Kiểm tra team <paramref name="team"/> (1 hoặc 2) có ≥5 quân liên tiếp đi qua ô <paramref name="lastIndex"/>
    /// vừa đặt không. Quét 4 hướng (ngang, dọc, chéo chính ↘, chéo phụ ↙) tính tổng chuỗi liên tiếp 2 phía.
    /// Trả về danh sách index các ô của chuỗi thắng (≥5 ô) nếu thắng; null nếu chưa.
    /// </summary>
    public static List<int>? CheckWin(int[] board, int lastIndex, int team)
    {
        if (lastIndex < 0 || lastIndex >= CellCount || board[lastIndex] != team) return null;
        int row = lastIndex / Size, col = lastIndex % Size;
        // 4 hướng: (dRow, dCol)
        var dirs = new (int dr, int dc)[] { (0, 1), (1, 0), (1, 1), (1, -1) };
        foreach (var (dr, dc) in dirs)
        {
            var line = new List<int> { lastIndex };
            // Đi xuôi theo hướng.
            for (int step = 1; ; step++)
            {
                int r = row + dr * step, c = col + dc * step;
                if (r < 0 || r >= Size || c < 0 || c >= Size) break;
                int idx = r * Size + c;
                if (board[idx] != team) break;
                line.Add(idx);
            }
            // Đi ngược theo hướng.
            for (int step = 1; ; step++)
            {
                int r = row - dr * step, c = col - dc * step;
                if (r < 0 || r >= Size || c < 0 || c >= Size) break;
                int idx = r * Size + c;
                if (board[idx] != team) break;
                line.Add(idx);
            }
            if (line.Count >= WinLength) { line.Sort(); return line; }
        }
        return null;
    }

    /// <summary>True nếu bàn đã đầy (không còn ô trống) → hòa nếu chưa ai thắng.</summary>
    public static bool IsBoardFull(int[] board)
    {
        for (int i = 0; i < board.Length; i++) if (board[i] == 0) return false;
        return true;
    }
}
