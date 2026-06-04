namespace CutPig.GameEngine;

/// <summary>Lựa chọn Oẳn Tù Xì. None = chưa chọn.</summary>
public enum RpsChoice
{
    None = 0,
    Rock = 1,   // Búa
    Paper = 2,  // Bao
    Scissors = 3, // Kéo
}

/// <summary>Kết quả so 1 ván Oẳn Tù Xì giữa A và B.</summary>
public enum RpsOutcome
{
    Draw = 0,
    AWins = 1,
    BWins = 2,
}

public static class RpsEngine
{
    /// <summary>So kéo-búa-bao. Búa thắng Kéo, Kéo thắng Bao, Bao thắng Búa. Giống nhau = hòa.</summary>
    public static RpsOutcome Resolve(RpsChoice a, RpsChoice b)
    {
        if (a == b) return RpsOutcome.Draw;
        bool aWins = (a == RpsChoice.Rock && b == RpsChoice.Scissors)
                  || (a == RpsChoice.Scissors && b == RpsChoice.Paper)
                  || (a == RpsChoice.Paper && b == RpsChoice.Rock);
        return aWins ? RpsOutcome.AWins : RpsOutcome.BWins;
    }

    public static RpsChoice RandomChoice(System.Random rng) => (RpsChoice)(rng.Next(3) + 1);
}

/// <summary>
/// Một cặp đấu Oẳn Tù Xì trong giải lao (best-of-N). Theo dõi điểm 2 bên + lựa chọn ván hiện tại.
/// Hòa → đánh lại (không tính ván). Ai chạm WinTarget trước thì thắng cặp.
/// </summary>
public class RpsMatchup
{
    public System.Guid PlayerAId { get; set; }
    public System.Guid PlayerBId { get; set; }
    public int WinTarget { get; set; }           // 3 (best-of-3) hoặc 5 (best-of-5)
    public int WinsA { get; set; }
    public int WinsB { get; set; }
    public RpsChoice ChoiceA { get; set; } = RpsChoice.None;
    public RpsChoice ChoiceB { get; set; } = RpsChoice.None;
    /// <summary>Số ván (game) đã chơi xong trong cặp (không tính ván hòa). Dùng cho hiển thị.</summary>
    public int GamesPlayed { get; set; }
    /// <summary>UserId người thắng cặp (null = chưa xong).</summary>
    public System.Guid? WinnerId { get; set; }
    /// <summary>UserId người thua cặp (null = chưa xong).</summary>
    public System.Guid? LoserId { get; set; }

    // Snapshot ván vừa chốt (để client lật bài cho xem) — reset choices nhưng giữ Last* tới ván kế.
    public RpsChoice LastChoiceA { get; set; } = RpsChoice.None;
    public RpsChoice LastChoiceB { get; set; } = RpsChoice.None;
    public RpsOutcome LastOutcome { get; set; } = RpsOutcome.Draw;
    public bool HasLast { get; set; }

    public bool IsDone => WinnerId.HasValue;
    public bool BothChosen => ChoiceA != RpsChoice.None && ChoiceB != RpsChoice.None;

    /// <summary>
    /// Chốt 1 ván khi cả 2 đã chọn: hòa → reset choices (đánh lại, không tăng điểm);
    /// có người thắng → +1 cho người đó, reset choices; nếu chạm WinTarget → set WinnerId/LoserId.
    /// Trả về outcome của ván vừa chốt (Draw nếu đánh lại).
    /// </summary>
    public RpsOutcome ResolveCurrentGame()
    {
        var outcome = RpsEngine.Resolve(ChoiceA, ChoiceB);
        if (outcome == RpsOutcome.AWins) WinsA++;
        else if (outcome == RpsOutcome.BWins) WinsB++;
        if (outcome != RpsOutcome.Draw) GamesPlayed++;

        LastChoiceA = ChoiceA;
        LastChoiceB = ChoiceB;
        LastOutcome = outcome;
        HasLast = true;

        ChoiceA = RpsChoice.None;
        ChoiceB = RpsChoice.None;

        if (WinsA >= WinTarget) { WinnerId = PlayerAId; LoserId = PlayerBId; }
        else if (WinsB >= WinTarget) { WinnerId = PlayerBId; LoserId = PlayerAId; }
        return outcome;
    }
}

/// <summary>
/// Giải lao Oẳn Tù Xì bracket 4 người:
///  - V1 (R1A): seed0 vs seed1, BO3
///  - V2 (R1B): seed2 vs seed3, BO3
///  - V3 (Loser bracket): thua V1 vs thua V2, BO3 → hạng 3 (thắng) & 4 (thua)
///  - V4 (Final): thắng V1 vs thắng V2, BO5 → hạng 1 (thắng) & 2 (thua)
/// </summary>
public enum RpsStage
{
    Round1A = 0,
    Round1B = 1,
    ThirdPlace = 2,
    Final = 3,
    Done = 4,
}

public class RpsTournament
{
    public RpsStage Stage { get; set; } = RpsStage.Round1A;
    public RpsMatchup Round1A { get; set; } = new();
    public RpsMatchup Round1B { get; set; } = new();
    public RpsMatchup ThirdPlace { get; set; } = new();
    public RpsMatchup Final { get; set; } = new();

    /// <summary>Xếp hạng cuối: index 0 = hạng 1 ... index 3 = hạng 4. Rỗng cho tới khi Done.</summary>
    public System.Collections.Generic.List<System.Guid> FinalRanking { get; init; } = new();

    public RpsMatchup Current => Stage switch
    {
        RpsStage.Round1A => Round1A,
        RpsStage.Round1B => Round1B,
        RpsStage.ThirdPlace => ThirdPlace,
        RpsStage.Final => Final,
        _ => Final,
    };

    /// <summary>Khởi tạo bracket từ 4 userId đã xáo trộn (seed0..3).</summary>
    public static RpsTournament Create(System.Collections.Generic.IReadOnlyList<System.Guid> seeds)
    {
        var t = new RpsTournament();
        t.Round1A.PlayerAId = seeds[0]; t.Round1A.PlayerBId = seeds[1]; t.Round1A.WinTarget = 3;
        t.Round1B.PlayerAId = seeds[2]; t.Round1B.PlayerBId = seeds[3]; t.Round1B.WinTarget = 3;
        t.ThirdPlace.WinTarget = 3;
        t.Final.WinTarget = 5;
        return t;
    }

    /// <summary>
    /// Sau khi 1 cặp xong (Current.IsDone), tiến bracket sang stage kế. Khi xong V1+V2 → fill V3/V4.
    /// Khi Final xong → tính FinalRanking + Stage=Done. Trả về true nếu vừa Done.
    /// </summary>
    public bool AdvanceStage()
    {
        switch (Stage)
        {
            case RpsStage.Round1A:
                Stage = RpsStage.Round1B;
                return false;
            case RpsStage.Round1B:
                // Đã biết winner/loser cả 2 cặp → fill ThirdPlace (2 loser) & Final (2 winner).
                ThirdPlace.PlayerAId = Round1A.LoserId!.Value;
                ThirdPlace.PlayerBId = Round1B.LoserId!.Value;
                Final.PlayerAId = Round1A.WinnerId!.Value;
                Final.PlayerBId = Round1B.WinnerId!.Value;
                Stage = RpsStage.ThirdPlace;
                return false;
            case RpsStage.ThirdPlace:
                Stage = RpsStage.Final;
                return false;
            case RpsStage.Final:
                // Hạng 1 = thắng Final, 2 = thua Final, 3 = thắng ThirdPlace, 4 = thua ThirdPlace.
                FinalRanking.Clear();
                FinalRanking.Add(Final.WinnerId!.Value);
                FinalRanking.Add(Final.LoserId!.Value);
                FinalRanking.Add(ThirdPlace.WinnerId!.Value);
                FinalRanking.Add(ThirdPlace.LoserId!.Value);
                Stage = RpsStage.Done;
                return true;
            default:
                return false;
        }
    }
}
