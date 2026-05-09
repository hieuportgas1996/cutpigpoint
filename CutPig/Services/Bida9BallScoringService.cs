using System.Text.Json;
using CutPig.Domain;
using CutPig.Dtos;

namespace CutPig.Services;

public class Bida9BallScoringService
{
    public const int MinBallCount = 1;
    public const int MaxBallCount = 9;
    public const int RequiredPlayerCount = 3;

    public List<RoundResult> Compute(
        List<PlayerRoundInputDto> inputs,
        List<BallConfigDto> ballConfig,
        bool manualScoring)
    {
        if (manualScoring)
        {
            var manual = inputs.Select(i => new RoundResult
            {
                PlayerId = i.PlayerId,
                Score = i.ManualScore ?? 0
            }).ToList();
            ValidateZeroSum(manual);
            return manual;
        }

        if (inputs.Count != RequiredPlayerCount)
            throw new InvalidOperationException("Bida 9 Ball cần đúng 3 người chơi.");

        ValidateBallConfig(ballConfig);
        var pointsByBall = ballConfig.ToDictionary(c => c.Ball, c => c.Points);
        var playerIds = inputs.Select(i => i.PlayerId).ToHashSet();

        var breakers = inputs.Where(i => i.BreakAndCleared).ToList();
        if (breakers.Count > 1)
            throw new InvalidOperationException("Chỉ một người được phá-chấm mỗi round.");

        var totals = inputs.ToDictionary(i => i.PlayerId, _ => 0);

        if (breakers.Count == 1)
        {
            var breaker = breakers[0];
            if (inputs.Any(i => i.BallHits != null && i.BallHits.Count > 0))
                throw new InvalidOperationException("Phá-chấm: không nhập ăn bi cho bất kỳ ai.");

            int totalBallPoints = ballConfig.Sum(b => b.Points);
            int losers = inputs.Count - 1;
            if (losers <= 0)
                throw new InvalidOperationException("Phá-chấm cần ít nhất 2 người chơi.");
            int winnerScore = totalBallPoints * 2;
            if (winnerScore % losers != 0)
                throw new InvalidOperationException("Tổng điểm các bi không chia đều được cho số người thua. Hãy điều chỉnh điểm bi.");
            int loserScore = -(winnerScore / losers);

            totals[breaker.PlayerId] = winnerScore;
            foreach (var p in inputs.Where(i => i.PlayerId != breaker.PlayerId))
                totals[p.PlayerId] = loserScore;

            var results = inputs.Select(i => Snapshot(i, totals[i.PlayerId])).ToList();
            ValidateZeroSum(results);
            return results;
        }

        foreach (var p in inputs)
        {
            var hits = p.BallHits ?? new List<BallHitDto>();
            foreach (var hit in hits)
            {
                if (!pointsByBall.TryGetValue(hit.Ball, out var configuredPoints))
                    throw new InvalidOperationException($"Bi {hit.Ball} không thuộc cấu hình ván.");
                if (hit.Points != configuredPoints)
                    throw new InvalidOperationException($"Điểm của bi {hit.Ball} không khớp cấu hình ván.");
                if (hit.VictimPlayerId == p.PlayerId)
                    throw new InvalidOperationException("Người ăn bi không thể tự là nạn nhân.");
                if (!playerIds.Contains(hit.VictimPlayerId))
                    throw new InvalidOperationException("Người bị trừ không thuộc bàn chơi.");

                totals[p.PlayerId] += hit.Points;
                totals[hit.VictimPlayerId] -= hit.Points;
            }
        }

        var resultsNormal = inputs.Select(i => Snapshot(i, totals[i.PlayerId])).ToList();
        ValidateZeroSum(resultsNormal);
        return resultsNormal;
    }

    public static List<BallConfigDto> ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Ván Bida 9 Ball thiếu cấu hình bi.");
        try
        {
            return JsonSerializer.Deserialize<List<BallConfigDto>>(json) ?? new List<BallConfigDto>();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Cấu hình bi không hợp lệ.");
        }
    }

    public static string SerializeConfig(List<BallConfigDto> config)
        => JsonSerializer.Serialize(config);

    public static List<BallHitDto>? ParseHits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<BallHitDto>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? SerializeHits(List<BallHitDto>? hits)
        => hits == null || hits.Count == 0 ? null : JsonSerializer.Serialize(hits);

    public static void ValidateBallConfig(List<BallConfigDto> ballConfig)
    {
        if (ballConfig.Count < MinBallCount || ballConfig.Count > MaxBallCount)
            throw new InvalidOperationException($"Số bi tính điểm phải từ {MinBallCount} đến {MaxBallCount}.");
        if (ballConfig.Any(b => b.Ball is < 1 or > 9))
            throw new InvalidOperationException("Bi tính điểm phải nằm trong 1..9.");
        if (ballConfig.Select(b => b.Ball).Distinct().Count() != ballConfig.Count)
            throw new InvalidOperationException("Các bi tính điểm không được trùng nhau.");
        if (ballConfig.Any(b => b.Points <= 0))
            throw new InvalidOperationException("Điểm của bi phải lớn hơn 0.");
    }

    private static void ValidateZeroSum(List<RoundResult> results)
    {
        if (!results.Any(r => r.Score > 0) || !results.Any(r => r.Score < 0))
            throw new InvalidOperationException("Round phải có cả người được điểm dương và người bị điểm âm.");
        var sum = results.Sum(r => r.Score);
        if (sum != 0)
            throw new InvalidOperationException($"Tổng điểm của round phải bằng 0 (hiện tại: {(sum > 0 ? "+" : "")}{sum}).");
    }

    private static RoundResult Snapshot(PlayerRoundInputDto i, int score) => new()
    {
        PlayerId = i.PlayerId,
        BreakAndCleared = i.BreakAndCleared,
        BallHitsJson = SerializeHits(i.BallHits),
        Score = score
    };
}
