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

    private const int ThreePairsStraightPoint = 3;
    private const int FourOfAKindPoint = 4;
    private const int FourPairsStraightPoint = 5;
    private const int WhiteWinPoint = 6;

    public List<RoundResult> Compute(List<PlayerRoundInputDto> inputs, bool manualScoring)
    {
        if (manualScoring)
        {
            return inputs.Select(i => new RoundResult
            {
                PlayerId = i.PlayerId,
                Rank = i.Rank,
                Score = i.ManualScore ?? 0
            }).ToList();
        }

        if (inputs.Count != 4)
            throw new InvalidOperationException("Tien Len Mien Nam requires exactly 4 players.");

        var ranks = inputs.Where(i => i.Rank.HasValue).Select(i => i.Rank!.Value).ToList();
        if (ranks.Distinct().Count() != 4 || ranks.Min() != 1 || ranks.Max() != 4)
            throw new InvalidOperationException("Each player must have a unique rank between 1 and 4.");

        var results = new Dictionary<Guid, int>();
        foreach (var p in inputs)
        {
            results[p.PlayerId] = RankPoints(p.Rank!.Value);
        }

        foreach (var p in inputs)
        {
            int cutPoints = p.BlackPigsCut * BlackPigPoint + p.RedPigsCut * RedPigPoint;
            int lostPoints = p.BlackPigsLost * BlackPigPoint + p.RedPigsLost * RedPigPoint;

            results[p.PlayerId] += cutPoints;
            results[p.PlayerId] -= lostPoints;
        }

        foreach (var p in inputs)
        {
            int bonus = 0;
            if (p.ThreePairsStraight) bonus += ThreePairsStraightPoint;
            if (p.FourOfAKind) bonus += FourOfAKindPoint;
            if (p.FourPairsStraight) bonus += FourPairsStraightPoint;
            if (p.WhiteWin) bonus += WhiteWinPoint;

            if (bonus == 0) continue;

            results[p.PlayerId] += bonus * 3;
            foreach (var other in inputs.Where(o => o.PlayerId != p.PlayerId))
            {
                results[other.PlayerId] -= bonus;
            }
        }

        return inputs.Select(i => new RoundResult
        {
            PlayerId = i.PlayerId,
            Rank = i.Rank,
            BlackPigsCut = i.BlackPigsCut,
            RedPigsCut = i.RedPigsCut,
            BlackPigsLost = i.BlackPigsLost,
            RedPigsLost = i.RedPigsLost,
            ThreePairsStraight = i.ThreePairsStraight,
            FourOfAKind = i.FourOfAKind,
            FourPairsStraight = i.FourPairsStraight,
            WhiteWin = i.WhiteWin,
            Score = results[i.PlayerId]
        }).ToList();
    }

    private static int RankPoints(int rank) => rank switch
    {
        1 => RankFirstScore,
        2 => RankSecondScore,
        3 => RankThirdScore,
        4 => RankFourthScore,
        _ => 0
    };
}
