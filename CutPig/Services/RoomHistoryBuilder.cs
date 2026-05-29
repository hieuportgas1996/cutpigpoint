using System.Text.Json;
using CutPig.Domain;
using CutPig.Dtos;

namespace CutPig.Services;

/// <summary>Helper shared between RoomsController and RoomHub for serialising a finished room as a history DTO.</summary>
public static class RoomHistoryBuilder
{
    public static RoomHistoryDto Build(Room room)
    {
        List<RoomFinalScoreEntryDto> scores = new();
        if (!string.IsNullOrEmpty(room.FinalScoresJson))
        {
            try { scores = JsonSerializer.Deserialize<List<RoomFinalScoreEntryDto>>(room.FinalScoresJson) ?? new(); } catch { }
        }
        List<RoomSponsorEntryDto>? sponsor = null;
        if (!string.IsNullOrEmpty(room.SponsorPlanJson))
        {
            try { sponsor = JsonSerializer.Deserialize<List<RoomSponsorEntryDto>>(room.SponsorPlanJson); } catch { }
        }
        List<Guid>? decided = null;
        if (!string.IsNullOrEmpty(room.SponsorDecisionsJson))
        {
            try { decided = JsonSerializer.Deserialize<List<Guid>>(room.SponsorDecisionsJson); } catch { }
        }
        LuckyWheelDto? wheel = null;
        if (!string.IsNullOrEmpty(room.LuckyWheelJson))
        {
            try { wheel = JsonSerializer.Deserialize<LuckyWheelDto>(room.LuckyWheelJson); } catch { }
        }
        LuckyWheelPreviewDto? preview = null;
        if (!string.IsNullOrEmpty(room.LuckyWheelPreviewJson))
        {
            try { preview = JsonSerializer.Deserialize<LuckyWheelPreviewDto>(room.LuckyWheelPreviewJson); } catch { }
        }
        return new RoomHistoryDto(
            room.Id,
            room.Code,
            room.Name,
            room.MaxSeats,
            string.IsNullOrWhiteSpace(room.HostUser?.DisplayName) ? (room.HostUser?.Username ?? "") : room.HostUser!.DisplayName,
            room.CreatedAt,
            room.FinishedAt,
            scores,
            sponsor,
            wheel,
            decided,
            preview
        );
    }
}
