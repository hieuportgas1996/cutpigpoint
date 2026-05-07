using CutPig.Domain;
using CutPig.Dtos;

namespace CutPig.Services;

public class TienLenScoringService
{
    private const int RankFirstScore = 2;
    private const int RankSecondScore = 1;
    private const int RankThirdScore = -1;
    private const int RankFourthScore = -2;

    private const int BlackPigPoint = 1;
    private const int RedPigPoint = 2;

    private const int ThreePairsPoint = 3;
    private const int FourOfAKindPoint = 4;
    private const int FourPairsPoint = 5;

    private const int WhiteWinSelfPoint = 6;
    private const int WhiteWinLossPoint = 2;

    private const int JudgeSelfPoint = 12;
    private const int JudgeLossPoint = 4;

    public List<RoundResult> Compute(List<PlayerRoundInputDto> inputs, bool manualScoring)
    {
        if (manualScoring)
        {
            var results = inputs.Select(i => new RoundResult
            {
                PlayerId = i.PlayerId,
                Rank = i.Rank,
                Score = i.ManualScore ?? 0
            }).ToList();
            ValidateZeroSum(results);
            return results;
        }

        if (inputs.Count != 4)
            throw new InvalidOperationException("Tien Len Mien Nam requires exactly 4 players.");

        var whiteWinners = inputs.Where(i => i.WhiteWin).ToList();
        var judges = inputs.Where(i => i.Judge).ToList();

        if (whiteWinners.Count > 1)
            throw new InvalidOperationException("Chỉ một người được về trắng mỗi round.");
        if (judges.Count > 1)
            throw new InvalidOperationException("Chỉ một người được phán xét mỗi round.");
        if (whiteWinners.Count == 1 && judges.Count == 1)
            throw new InvalidOperationException("Không thể đồng thời về trắng và phán xét.");

        if (whiteWinners.Count == 1)
            return ComputeWhiteWin(inputs, whiteWinners[0]);

        if (judges.Count == 1)
            return ComputeJudge(inputs, judges[0]);

        return ComputeNormal(inputs);
    }

    private static List<RoundResult> ComputeWhiteWin(List<PlayerRoundInputDto> inputs, PlayerRoundInputDto winner)
    {
        var totals = inputs.ToDictionary(i => i.PlayerId, _ => 0);
        totals[winner.PlayerId] = WhiteWinSelfPoint;
        foreach (var p in inputs.Where(i => i.PlayerId != winner.PlayerId))
            totals[p.PlayerId] = -WhiteWinLossPoint;

        return inputs.Select(i => Snapshot(i, totals[i.PlayerId])).ToList();
    }

    private static List<RoundResult> ComputeJudge(List<PlayerRoundInputDto> inputs, PlayerRoundInputDto judge)
    {
        var totals = inputs.ToDictionary(i => i.PlayerId, _ => 0);

        totals[judge.PlayerId] = JudgeSelfPoint;
        foreach (var p in inputs.Where(i => i.PlayerId != judge.PlayerId))
            totals[p.PlayerId] = -JudgeLossPoint;

        foreach (var victim in inputs.Where(i => i.PlayerId != judge.PlayerId))
        {
            int extra = victim.BlackPigsHeld * BlackPigPoint
                      + victim.RedPigsHeld * RedPigPoint
                      + (victim.HasThreePairsHeld ? ThreePairsPoint : 0)
                      + (victim.HasFourOfAKindHeld ? FourOfAKindPoint : 0)
                      + (victim.HasFourPairsHeld ? FourPairsPoint : 0);

            totals[judge.PlayerId] += extra;
            totals[victim.PlayerId] -= extra;
        }

        return inputs.Select(i => Snapshot(i, totals[i.PlayerId])).ToList();
    }

    private static List<RoundResult> ComputeNormal(List<PlayerRoundInputDto> inputs)
    {
        var ranks = inputs.Where(i => i.Rank.HasValue).Select(i => i.Rank!.Value).ToList();
        if (ranks.Count != 4 || ranks.Distinct().Count() != 4 || ranks.Min() != 1 || ranks.Max() != 4)
            throw new InvalidOperationException("Mỗi người chơi phải có hạng từ 1 đến 4 và không trùng nhau.");

        var totals = inputs.ToDictionary(i => i.PlayerId, i => RankPoints(i.Rank!.Value));

        // Heo: zero-sum 1-vs-1, cut += N, lost -= N
        foreach (var p in inputs)
        {
            int cut = p.BlackPigsCut * BlackPigPoint + p.RedPigsCut * RedPigPoint;
            int lost = p.BlackPigsLost * BlackPigPoint + p.RedPigsLost * RedPigPoint;
            totals[p.PlayerId] += cut;
            totals[p.PlayerId] -= lost;
        }

        // Bonus 1-vs-1: winner +N, victim -N
        foreach (var p in inputs)
        {
            ApplyBonus(totals, p.PlayerId, p.ThreePairsStraight, p.ThreePairsVictimId, ThreePairsPoint, "3 đôi thông", inputs);
            ApplyBonus(totals, p.PlayerId, p.FourOfAKind, p.FourOfAKindVictimId, FourOfAKindPoint, "tứ quý", inputs);
            ApplyBonus(totals, p.PlayerId, p.FourPairsStraight, p.FourPairsVictimId, FourPairsPoint, "4 đôi thông", inputs);
        }

        return inputs.Select(i => Snapshot(i, totals[i.PlayerId])).ToList();
    }

    private static void ApplyBonus(
        Dictionary<Guid, int> totals,
        Guid winnerId,
        bool flag,
        Guid? victimId,
        int points,
        string bonusName,
        List<PlayerRoundInputDto> inputs)
    {
        if (!flag) return;
        if (victimId == null)
            throw new InvalidOperationException($"Cần chọn người thua khi ăn {bonusName}.");
        if (victimId == winnerId)
            throw new InvalidOperationException($"Người ăn {bonusName} không thể tự thua chính mình.");
        if (!inputs.Any(i => i.PlayerId == victimId.Value))
            throw new InvalidOperationException($"Người thua {bonusName} không thuộc bàn chơi.");
        totals[winnerId] += points;
        totals[victimId.Value] -= points;
    }

    private static int RankPoints(int rank) => rank switch
    {
        1 => RankFirstScore,
        2 => RankSecondScore,
        3 => RankThirdScore,
        4 => RankFourthScore,
        _ => 0
    };

    private static void ValidateZeroSum(List<RoundResult> results)
    {
        var sum = results.Sum(r => r.Score);
        if (sum != 0)
            throw new InvalidOperationException($"Tổng điểm phải bằng 0 (hiện tại {sum}).");
    }

    private static RoundResult Snapshot(PlayerRoundInputDto i, int score) => new()
    {
        PlayerId = i.PlayerId,
        Rank = i.Rank,
        BlackPigsCut = i.BlackPigsCut,
        RedPigsCut = i.RedPigsCut,
        BlackPigsLost = i.BlackPigsLost,
        RedPigsLost = i.RedPigsLost,
        ThreePairsStraight = i.ThreePairsStraight,
        ThreePairsVictimId = i.ThreePairsVictimId,
        FourOfAKind = i.FourOfAKind,
        FourOfAKindVictimId = i.FourOfAKindVictimId,
        FourPairsStraight = i.FourPairsStraight,
        FourPairsVictimId = i.FourPairsVictimId,
        WhiteWin = i.WhiteWin,
        Judge = i.Judge,
        BlackPigsHeld = i.BlackPigsHeld,
        RedPigsHeld = i.RedPigsHeld,
        HasThreePairsHeld = i.HasThreePairsHeld,
        HasFourOfAKindHeld = i.HasFourOfAKindHeld,
        HasFourPairsHeld = i.HasFourPairsHeld,
        Score = score
    };
}
