namespace CutPig.GameEngine;

public enum MatchStatus
{
    InProgress = 0,
    Finished = 1,
    WaitingNextRound = 2,        // round ended, waiting for host to start next
    WhiteWinChoice = 3,          // round just dealt, white-win candidates choosing accept/decline
    PendingTrickCut = 4,         // trick about to reset, but someone has 4-pair-run → giving them chance to cut
    VoteReset = 5,               // a player called a re-deal vote during trick 1; players are voting yes/no
    FestivalReveal = 6,          // round lễ hội: đã chia 3 lá, mỗi người đang nặn/lật bài trước khi tính điểm
    XiDachPlaying = 7,           // round Sát Phạt (xì dách): players rồi nhà cái rút bài tuần tự
    XiDachCompare = 8,           // round Sát Phạt: nhà cái lần lượt bấm "So" từng player còn lại
    BreakRps = 9,                // round Giải lao: giải đấu Oẳn Tù Xì (kéo búa bao) bracket 4 người
    BreakMathPick = 10,          // round Giải lao Tính toán: pha mỗi người chọn 1 chữ số 0-9
    BreakMathQuiz = 11,          // round Giải lao Tính toán: pha trả lời câu hỏi trắc nghiệm
    BreakMemoryView = 12,        // round Giải lao Trí nhớ: pha xem lưới 3×3 logo CLB (đếm ngược 10s)
    BreakMemoryQuiz = 13,        // round Giải lao Trí nhớ: pha trả lời "ô X là đội nào?"
    BreakReflexCooldown = 14,    // round Giải lao Phản xạ: pha cooldown 3s chuẩn bị (đã hiện lưới)
    BreakReflexPlay = 15,        // round Giải lao Phản xạ: pha click đúng ô theo đề
}

/// <summary>Loại game trong "Giải lao zui zẻ". None = không phải round giải lao.</summary>
public enum BreakGameType
{
    None = 0,
    Rps = 1,      // Oẳn Tù Xì (kéo búa bao)
    Math = 2,     // Tính toán (tính nhẩm trắc nghiệm)
    Memory = 3,   // Trí nhớ (ghi nhớ logo CLB)
    Reflex = 4,   // Phản xạ (click đúng hình/màu nhanh nhất)
}

public class MatchPlayer
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool HasAvatar { get; init; }
    public int SeatIndex { get; init; }
    public List<Card> Hand { get; set; } = new();
    public int? FinalRank { get; set; } // 1..N when player finishes current round
    public int TotalScore { get; set; } // cumulative across rounds
    public bool PassedThisTrick { get; set; }
    public string? WhiteWinReason { get; set; }
    public bool? WhiteWinAccepted { get; set; } // null = chưa chọn, true = về trắng, false = từ chối
    public bool FinishedWithThreeOfSpades { get; set; } // last play that emptied the hand contained 3♠
    public bool StuckWithThreeOfSpades { get; set; }   // round ended while this player still held 3♠
    public bool HasPlayedThisRound { get; set; }        // true once player has played at least 1 card this round

    // Judge ("Phán xử") flags — set when 1st player finishes and ≥1 other has not played yet
    public bool JudgeIsWinner { get; set; }             // true if this player triggered the judge by winning #1
    public bool JudgeIsVictim { get; set; }             // true if this player is being judged (didn't play this round)
    public bool JudgeIsPardoned { get; set; }           // true if Case B: judge fired but this player had already played
    public int JudgeHeldValue { get; set; }             // pig/3-pair/four/4-pair value held when judged (only for victims)

    public bool Surrendered { get; set; }               // true if this player gave up this round (auto last rank)

    /// <summary>Phiếu vote chia bài lại trong round hiện tại: null = chưa bỏ, true = Đồng ý, false = Bỏ.</summary>
    public bool? VoteResetChoice { get; set; }
    /// <summary>True khi player đã MỞ vote chia bài lại — mỗi người được mở 1 lần / TRẬN (giữ qua các round, không reset ở DealRound). Chỉ initiator tiêu quyền; người chỉ bỏ phiếu (kể cả Đồng ý) KHÔNG mất quyền.</summary>
    public bool HasUsedVoteReset { get; set; }

    /// <summary>True khi player đã dùng quyền "Tổ chức lễ hội" — 1 lần / TRẬN (giữ qua round, không reset ở DealRound).</summary>
    public bool HasUsedFestival { get; set; }

    /// <summary>True khi player đã dùng quyền "Ngôi Sao Hi Vọng" — 1 lần / TRẬN (giữ qua round, không reset ở DealRound).</summary>
    public bool HasUsedStarOfHope { get; set; }
    /// <summary>True khi player này LÀ Ngôi Sao Hi Vọng của round HIỆN TẠI (điểm giao dịch với người này ×2). Reset ở DealRound.</summary>
    public bool IsStarOfHope { get; set; }

    /// <summary>True khi player đã dùng quyền "Sát Phạt" (tổ chức xì dách) — 1 lần / TRẬN (giữ qua round).</summary>
    public bool HasUsedXiDach { get; set; }

    /// <summary>True khi player đã dùng quyền "Giải lao zui zẻ" — 1 lần / TRẬN (giữ qua round).</summary>
    public bool HasUsedBreak { get; set; }

    // ---- Liều Ăn Nhiều (Hot Streak Gamble) ----
    /// <summary>Số ván về Nhất LIÊN TIẾP gần nhất (về trắng/phán-xử thắng cũng = Nhất). Reset 0 khi không về Nhất. Round biến tấu (lễ hội/xì dách) KHÔNG đụng. Giữ qua round. KHÔNG cap — đếm vô hạn (6,7,8…); client hiện "N x 🏆" khi >5.</summary>
    public int WinStreak { get; set; }
    /// <summary>Mốc streak (bội số 5) gần nhất đã mời Liều. Tránh mời lại cùng 1 mốc; mời lại ở mốc kế (5→10→15…). Reset 0 khi WinStreak về 0.</summary>
    public int GambleOfferedAtStreak { get; set; }
    /// <summary>True khi player này LÀ người liều của round HIỆN TẠI (điểm thắng ×2 +6 / điểm thua ×2). Reset ở DealRound.</summary>
    public bool IsGambling { get; set; }
    // ---- Xì Dách (Sát Phạt) round state — reset ở DealRound ----
    /// <summary>True nếu player này là Nhà Cái của round xì dách hiện tại.</summary>
    public bool IsXiDachDealer { get; set; }
    /// <summary>True khi player đã "dừng" rút bài (chốt tay) trong round xì dách.</summary>
    public bool XiDachStood { get; set; }
    /// <summary>True khi cặp player↔nhà cái này đã được chốt điểm (xì dách/vàng lật sớm, hoặc nhà cái đã bấm So).</summary>
    public bool XiDachSettled { get; set; }
    /// <summary>Điểm round xì dách player này nhận (zero-sum với nhà cái, ĐÃ áp đền). Lưu để build round-end.</summary>
    public int XiDachDelta { get; set; }
    /// <summary>Delta player↔nhà cái CHỐT TẠI LÚC XÉT (theo tay nhà cái thời điểm đó), CHƯA áp redirect đền. Nhà cái rút thêm sau không đổi.</summary>
    public int XiDachBaseDelta { get; set; }
    /// <summary>True nếu bài player này đã được lật công khai (đặc biệt sớm / đã so).</summary>
    public bool XiDachRevealed { get; set; }
    /// <summary>True nếu player này thắng round lễ hội (bài cào mạnh nhất) — dùng cho hiển thị/lịch sử.</summary>
    public bool FestivalWinner { get; set; }
    /// <summary>Các index lá bài Cào Rùa player đã lật (0..2) — lật bất kỳ thứ tự nào. Mỗi người tự lật bài mình.</summary>
    public HashSet<int> FestivalRevealedIdx { get; init; } = new();
}

public class Match
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RoomId { get; init; }
    public Guid HostUserId { get; init; }
    /// <summary>Copy của Room.ShowOpponentCardCount lúc tạo trận — quyết định client có hiện số lá đối thủ hay úp lá.</summary>
    public bool ShowOpponentCardCount { get; init; } = true;
    public List<MatchPlayer> Players { get; init; } = new();
    public int CurrentTurnSeatIndex { get; set; }
    public Combo? CurrentTrick { get; set; }
    public Guid? CurrentTrickOwnerId { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.InProgress;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime TurnDeadline { get; set; }
    public int FinishedCount { get; set; }
    public List<Guid> FinishOrder { get; init; } = new();
    public int RoundNumber { get; set; } = 1;
    public Guid? PreviousRoundWinnerId { get; set; }
    /// <summary>
    /// True ở round 1 và ở round ngay sau khi ván trước về trắng. Round có flag này
    /// phải áp luật "ai cầm 3♠ đi đầu, nước đầu phải chứa 3♠" (giống round 1).
    /// </summary>
    public bool EnforceThreeSpadesOpening { get; set; }

    /// <summary>
    /// Được set khi ván vừa kết thúc bằng white-win, tiêu thụ ở DealRound kế tiếp để
    /// ép `EnforceThreeSpadesOpening = true` cho round đó.
    /// </summary>
    public bool NextRoundOpensWithThreeSpades { get; set; }
    public DateTime? NextRoundAt { get; set; }
    public DateTime? WhiteWinDeadline { get; set; }
    public DateTime? TrickCutDeadline { get; set; }
    public DateTime? VoteResetDeadline { get; set; }
    public Guid? VoteResetInitiatorId { get; set; }
    /// <summary>
    /// True khi round đã sang trick thứ 2 trở đi (đã có ít nhất 1 trick reset). Một khi true thì
    /// không cho mở vote chia bài lại nữa (vote chỉ ở trick 1).
    /// </summary>
    public bool PastFirstTrick { get; set; }

    /// <summary>True khi đã có người đặt lịch "Tổ chức lễ hội" → round KẾ TIẾP sẽ là Cào Rùa. Chỉ 1 người/round được đặt.</summary>
    public bool FestivalScheduled { get; set; }
    /// <summary>True khi round HIỆN TẠI là round lễ hội (Cào Rùa 3 lá) thay vì Tiến Lên.</summary>
    public bool IsFestivalRound { get; set; }
    /// <summary>UserId người đã tổ chức lễ hội cho round này (để hiển thị "ai tổ chức").</summary>
    public Guid? FestivalOrganizerId { get; set; }

    /// <summary>UserId người đã kích hoạt "Ngôi Sao Hi Vọng" → round KẾ TIẾP người này là star (điểm ×2). Chỉ 1 người/round được kích. Null = chưa ai.</summary>
    public Guid? StarOfHopeScheduledUserId { get; set; }

    // ---- Liều Ăn Nhiều (Hot Streak Gamble) ----
    /// <summary>UserId người vừa đạt streak ≥5 và ĐANG được mời liều (chưa Đồng ý/Từ chối). Nếu round KẾ là biến tấu (lễ hội/xì dách/star) thì giữ lời mời, hoãn sang round TLMN thường gần nhất. Null = không có lời mời.</summary>
    public Guid? GambleOfferUserId { get; set; }
    /// <summary>Hết hạn lời mời liều (chặn deal ván kế tới khi trả lời / hết hạn auto-từ-chối). Set khi tạo offer; null khi không có.</summary>
    public DateTime? GambleOfferDeadline { get; set; }
    /// <summary>UserId người đã ĐỒNG Ý liều → round KẾ TIẾP (TLMN thường) người này liều: ×2 +6 nếu thắng / ×2 nếu thua, mất quyền đi trước. Tiêu ở DealRound. Null = chưa ai.</summary>
    public Guid? GambleScheduledUserId { get; set; }
    /// <summary>True khi round HIỆN TẠI là round liều của GambleScheduledUserId (set ở DealRound khi tiêu cờ).</summary>
    public bool IsGambleRound { get; set; }

    // ---- Giải Lao Zui Zẻ (framework: Oẳn Tù Xì / Tính toán / Trí nhớ) ----
    /// <summary>
    /// Pool game giải lao CÒN LẠI trong trận. Bấm "Giải lao" → random rút 1 game khỏi pool (chơi rồi mất khỏi pool).
    /// Khởi tạo 4 game khác nhau (4 lượt = 4 player × 1 lần). Hết pool → reset đầy lại.
    /// </summary>
    public List<BreakGameType> BreakGamePool { get; set; } = new()
        { BreakGameType.Rps, BreakGameType.Math, BreakGameType.Memory, BreakGameType.Reflex };
    /// <summary>True khi đã đặt lịch giải lao → round KẾ TIẾP là 1 game giải lao. Chỉ 1 người/round. 1 lần/TRẬN.</summary>
    public bool BreakScheduled { get; set; }
    /// <summary>Loại game giải lao ĐÃ ĐẶT cho round kế (Rps/Math/Memory). None khi chưa đặt. Tiêu ở DealRound.</summary>
    public BreakGameType BreakScheduledType { get; set; } = BreakGameType.None;
    /// <summary>UserId người tổ chức giải lao (hiển thị + toast).</summary>
    public Guid? BreakOrganizerId { get; set; }
    /// <summary>True khi round HIỆN TẠI là round giải lao (bất kỳ game nào).</summary>
    public bool IsBreakRound { get; set; }
    /// <summary>Loại game giải lao của round HIỆN TẠI (Rps/Math). None khi không phải round giải lao.</summary>
    public BreakGameType BreakGame { get; set; } = BreakGameType.None;

    // -- Oẳn Tù Xì --
    /// <summary>State bracket Oẳn Tù Xì của round giải lao hiện tại (null khi không phải round Oẳn Tù Xì).</summary>
    public RpsTournament? Rps { get; set; }
    /// <summary>Hết hạn 20s chọn kéo/búa/bao của ván Oẳn Tù Xì hiện tại; hết → server tự random cho ai chưa chọn. Null trong pha hiện kết quả.</summary>
    public DateTime? RpsChoiceDeadline { get; set; }
    /// <summary>Pha HIỆN KẾT QUẢ ván Oẳn Tù Xì vừa chốt: giữ 2s cho mọi người xem rồi mới qua ván/giai đoạn kế (hoặc finalize). Null khi đang ở pha chọn.</summary>
    public DateTime? RpsRevealUntil { get; set; }

    // -- Tính toán --
    /// <summary>Số mỗi người chọn (0-9) trong pha BreakMathPick; key = UserId. Chưa chọn = không có key.</summary>
    public Dictionary<Guid, int> MathPicks { get; set; } = new();
    /// <summary>Hết hạn 10s pha chọn số; hết → server random cho ai chưa chọn rồi sinh câu hỏi.</summary>
    public DateTime? MathPickDeadline { get; set; }
    /// <summary>2 câu hỏi trắc nghiệm sinh từ 4 số đã chọn (null trước khi vào pha quiz).</summary>
    public List<MathQuestion>? MathQuestions { get; set; }
    /// <summary>Index câu hỏi hiện tại (0-based) trong pha BreakMathQuiz.</summary>
    public int MathCurrentQuestion { get; set; }
    /// <summary>Thời điểm mở câu hiện tại (để tính ElapsedMs khi player trả lời). Null giữa các câu.</summary>
    public DateTime? MathQuestionStart { get; set; }
    /// <summary>Hết hạn 5s trả lời câu hiện tại; hết → ai chưa trả lời tính sai (max time) rồi qua câu kế / finalize.</summary>
    public DateTime? MathAnswerDeadline { get; set; }
    /// <summary>Câu trả lời từng người cho từng câu: key = UserId → list (1 phần tử/câu đã mở). Dùng để xếp hạng.</summary>
    public Dictionary<Guid, List<MathAnswer>> MathAnswers { get; set; } = new();
    /// <summary>Pha HIỆN KẾT QUẢ câu vừa xong: giữ vài giây cho xem đáp án đúng + ai nhanh nhất rồi qua câu kế. Null khi đang trả lời.</summary>
    public DateTime? MathRevealUntil { get; set; }

    // -- Trí nhớ (Memory) -- (dùng chung MathAnswers cho lời giải + ranking)
    /// <summary>Lưới + câu hỏi Trí nhớ của round hiện tại (null khi không phải round Trí nhớ).</summary>
    public MemoryGameEngine.MemoryBoard? MemoryBoard { get; set; }
    /// <summary>Hết hạn 10s pha xem lưới ghi nhớ; hết → vào pha trả lời.</summary>
    public DateTime? MemoryViewDeadline { get; set; }
    /// <summary>Index câu hỏi hiện tại (0-based) trong pha BreakMemoryQuiz.</summary>
    public int MemoryCurrentQuestion { get; set; }
    /// <summary>Thời điểm mở câu hiện tại (tính ElapsedMs). Null giữa các câu.</summary>
    public DateTime? MemoryQuestionStart { get; set; }
    /// <summary>Hết hạn trả lời câu hiện tại.</summary>
    public DateTime? MemoryAnswerDeadline { get; set; }
    /// <summary>Câu trả lời từng người cho từng câu Trí nhớ (key = UserId).</summary>
    public Dictionary<Guid, List<MathAnswer>> MemoryAnswers { get; set; } = new();
    /// <summary>Pha hiện đáp án câu Trí nhớ vừa xong.</summary>
    public DateTime? MemoryRevealUntil { get; set; }

    // -- Phản xạ (Reflex) -- (dùng chung MathAnswer cho lời giải + ranking)
    /// <summary>3 lượt Phản xạ (mỗi lượt 1 lưới 3×3 + ô target). Null khi không phải round Phản xạ.</summary>
    public List<ReflexGameEngine.ReflexRound>? ReflexRounds { get; set; }
    /// <summary>Index lượt hiện tại (0-based).</summary>
    public int ReflexCurrentRound { get; set; }
    /// <summary>Hết hạn 3s cooldown chuẩn bị (đã hiện lưới, chưa cho click). Sang pha play khi hết.</summary>
    public DateTime? ReflexCooldownUntil { get; set; }
    /// <summary>Thời điểm mở pha click (tính ElapsedMs). Null ngoài pha play.</summary>
    public DateTime? ReflexRoundStart { get; set; }
    /// <summary>Hết hạn click lượt hiện tại.</summary>
    public DateTime? ReflexAnswerDeadline { get; set; }
    /// <summary>Câu trả lời từng người cho từng lượt Phản xạ (key = UserId; ChosenIndex = ô đã click).</summary>
    public Dictionary<Guid, List<MathAnswer>> ReflexAnswers { get; set; } = new();
    /// <summary>Pha hiện đáp án lượt vừa xong (tô ô đúng + ai nhanh nhất) trước khi qua lượt kế.</summary>
    public DateTime? ReflexRevealUntil { get; set; }

    /// <summary>UserId người đã tổ chức "Sát Phạt" → round KẾ TIẾP là Xì Dách, người này làm Nhà Cái. Chỉ 1 người/round. Null = chưa ai.</summary>
    public Guid? XiDachScheduledUserId { get; set; }
    /// <summary>True khi round HIỆN TẠI là round Sát Phạt (Xì Dách) thay vì Tiến Lên.</summary>
    public bool IsXiDachRound { get; set; }
    /// <summary>UserId Nhà Cái của round Xì Dách hiện tại (để hiển thị + so điểm).</summary>
    public Guid? XiDachDealerId { get; set; }
    /// <summary>UserId người đang tới lượt rút bài trong round Xì Dách (null khi không ở pha rút).</summary>
    public Guid? XiDachTurnUserId { get; set; }
    /// <summary>Hết hạn lượt rút bài Xì Dách (60s/lượt). Timer auto-rút/dừng khi qua hạn.</summary>
    public DateTime? XiDachTurnDeadline { get; set; }
    /// <summary>Hết hạn pha nặn bài: khi mọi người lật hết 3 lá → set now+5s để xem rồi mới resolve.</summary>
    public DateTime? FestivalRevealDeadline { get; set; }
    /// <summary>Hết hạn auto-lật: nếu sau 60s vẫn còn lá chưa lật → tự lật hết.</summary>
    public DateTime? FestivalAutoFlipDeadline { get; set; }
    public Guid? PendingTrickWinnerId { get; set; } // owner of trick that just won, awaiting possible 4-pair-run cut
    public List<Guid> TrickCutCandidates { get; init; } = new(); // users who hold 4-pair-run and can interrupt

    /// <summary>
    /// Lá thắng trick vừa rồi (combo cuối cùng khi mọi người pass → trick reset). Giữ lại để client
    /// hiển thị "ai thắng vòng bằng lá gì" trong khoảng người thắng mở nước mới (lúc CurrentTrick = null).
    /// Bị xoá ngay khi có nước đánh mới của trick kế tiếp.
    /// </summary>
    public List<Card>? LastWonTrickCards { get; set; }
    public Guid? LastWonTrickWinnerId { get; set; }

    /// <summary>
    /// Chop-pig chain for the current trick: sequence of (playerId, chopValue, kind) for each play in this trick.
    /// Cleared on trick reset. On settle: if chain.Count >= 2 AND last play is NOT a Single (single 2 chặt single 2
    /// is "same-kind đơn", không tính điểm theo rule), the second-to-last player pays the sum of chopValue of
    /// chain[0..^1] to the last player; intermediate players net zero.
    /// </summary>
    public List<(Guid PlayerId, int ChopValue, ComboKind Kind, string Label)> TrickChopChain { get; init; } = new();

    /// <summary>Accumulated chop-pig deltas per player across all tricks of the current round.</summary>
    public Dictionary<Guid, int> RoundChopExtra { get; init; } = new();

    /// <summary>
    /// Chi tiết chặt heo per player trong round (để hiển thị "chặt/bị chặt gì").
    /// IsCutter=true → người chặt cuối (ăn điểm), false → người bị chặt cuối (mất điểm).
    /// Labels = danh sách combo trong chain (các nước bị tính pot). Cộng dồn qua nhiều trick.
    /// </summary>
    public Dictionary<Guid, (bool IsCutter, List<string> Labels)> RoundChopDetails { get; init; } = new();

    /// <summary>True if this round was decided by "Phán xử" (judge) — winner finished while ≥1 other player had not played yet.</summary>
    public bool JudgeTriggered { get; set; }

    /// <summary>Snapshot of every round-end emitted in this match (in-memory only). Used so players can review past rounds at any time.</summary>
    public List<Dtos.RoundEndDto> RoundHistory { get; init; } = new();
}
