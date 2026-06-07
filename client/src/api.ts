export interface Player {
  id: string;
  name: string;
  nickname?: string | null;
  hasAvatar: boolean;
}

export interface GamePlayer {
  playerId: string;
  name: string;
  seat: number;
  totalScore: number;
  hasAvatar: boolean;
}

export interface BallConfig {
  ball: number;
  points: number;
}

export interface BallHit {
  ball: number;
  points: number;
  victimPlayerId: string;
}

export interface RoundResult {
  playerId: string;
  rank: number | null;
  blackPigsCut: number;
  redPigsCut: number;
  blackPigsLost: number;
  redPigsLost: number;
  threePairsStraight: boolean;
  threePairsVictimId: string | null;
  fourOfAKind: boolean;
  fourOfAKindVictimId: string | null;
  fourPairsStraight: boolean;
  fourPairsVictimId: string | null;
  whiteWin: boolean;
  judge: boolean;
  judgedVictim: boolean;
  blackPigsHeld: number;
  redPigsHeld: number;
  hasThreePairsHeld: boolean;
  hasFourOfAKindHeld: boolean;
  hasFourPairsHeld: boolean;
  wonByThreeOfSpades: boolean;
  lostByThreeOfSpades: boolean;
  breakAndCleared: boolean;
  ballHits: BallHit[] | null;
  score: number;
}

export interface Round {
  id: string;
  roundNumber: number;
  manualScoring: boolean;
  createdAt: string;
  results: RoundResult[];
}

export const GameType = {
  TienLenMienNam: 1,
  Bida9Ball: 2,
  BidaDen: 3,
  Manual: 4
} as const;
export type GameTypeValue = typeof GameType[keyof typeof GameType];

export interface Game {
  id: string;
  type: GameTypeValue;
  startedAt: string;
  finishedAt: string | null;
  ballConfig: BallConfig[] | null;
  players: GamePlayer[];
  rounds: Round[];
}

export interface PlayerRoundInput {
  playerId: string;
  rank: number | null;
  blackPigsCut: number;
  redPigsCut: number;
  blackPigsLost: number;
  redPigsLost: number;
  threePairsStraight: boolean;
  threePairsVictimId: string | null;
  fourOfAKind: boolean;
  fourOfAKindVictimId: string | null;
  fourPairsStraight: boolean;
  fourPairsVictimId: string | null;
  whiteWin: boolean;
  judge: boolean;
  judgedVictim: boolean;
  blackPigsHeld: number;
  redPigsHeld: number;
  hasThreePairsHeld: boolean;
  hasFourOfAKindHeld: boolean;
  hasFourPairsHeld: boolean;
  wonByThreeOfSpades: boolean;
  lostByThreeOfSpades: boolean;
  breakAndCleared: boolean;
  ballHits: BallHit[] | null;
  manualScore: number | null;
}

const RAW_BASE = (import.meta.env.VITE_API_BASE ?? '').replace(/\/$/, '');
const BASE = `${RAW_BASE}/api`;

const TOKEN_KEY = 'cutpig.auth.token';
type UnauthorizedHandler = () => void;
let unauthorizedHandler: UnauthorizedHandler | null = null;

export const auth = {
  getToken: (): string | null => {
    try { return localStorage.getItem(TOKEN_KEY); } catch { return null; }
  },
  setToken: (token: string | null) => {
    try {
      if (token) localStorage.setItem(TOKEN_KEY, token);
      else localStorage.removeItem(TOKEN_KEY);
    } catch { /* ignore */ }
  },
  onUnauthorized: (handler: UnauthorizedHandler | null) => {
    unauthorizedHandler = handler;
  }
};

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...((options.headers as Record<string, string>) ?? {})
  };
  const token = auth.getToken();
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(`${BASE}${path}`, { ...options, headers });
  if (res.status === 401) {
    auth.setToken(null);
    unauthorizedHandler?.();
    throw new Error('Phiên đăng nhập hết hạn, vui lòng đăng nhập lại.');
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed: ${res.status}`);
  }
  if (res.status === 204) return undefined as unknown as T;
  return res.json() as Promise<T>;
}

export const api = {
  listPlayers: () => request<Player[]>('/players'),
  createPlayer: (name: string, nickname?: string) =>
    request<Player>('/players', { method: 'POST', body: JSON.stringify({ name, nickname }) }),
  updatePlayer: (id: string, name: string, nickname?: string) =>
    request<Player>(`/players/${id}`, { method: 'PUT', body: JSON.stringify({ name, nickname }) }),
  deletePlayer: (id: string) => request<void>(`/players/${id}`, { method: 'DELETE' }),
  setAvatar: (id: string, dataUrl: string) =>
    request<void>(`/players/${id}/avatar`, { method: 'PUT', body: JSON.stringify({ dataUrl }) }),
  deleteAvatar: (id: string) => request<void>(`/players/${id}/avatar`, { method: 'DELETE' }),
  avatarUrl: (id: string) => `${BASE}/players/${id}/avatar`,

  listGames: () => request<Array<{ id: string; type: GameTypeValue; startedAt: string; finishedAt: string | null; players: { playerId: string; name: string; seat: number; hasAvatar: boolean }[] }>>('/games'),
  getGame: (id: string) => request<Game>(`/games/${id}`),
  createGame: (playerIds: string[], type: GameTypeValue = GameType.TienLenMienNam, ballConfig?: BallConfig[]) =>
    request<Game>('/games', { method: 'POST', body: JSON.stringify({ playerIds, type, ballConfig: ballConfig ?? null }) }),
  finishGame: (id: string) => request<Game>(`/games/${id}/finish`, { method: 'POST' }),
  addRound: (id: string, manualScoring: boolean, players: PlayerRoundInput[]) =>
    request<Round>(`/games/${id}/rounds`, {
      method: 'POST',
      body: JSON.stringify({ manualScoring, players })
    }),
  deleteRound: (gameId: string, roundId: string) =>
    request<void>(`/games/${gameId}/rounds/${roundId}`, { method: 'DELETE' }),
  deleteGame: (id: string) => request<void>(`/games/${id}`, { method: 'DELETE' }),

  login: (username: string, password: string) =>
    request<{ token: string; expiresAt: string; userId: string; username: string; displayName: string; isAdmin: boolean; hasAvatar: boolean }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password })
    }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  me: () => request<{ userId: string; username: string; displayName: string; isAdmin: boolean; hasAvatar: boolean }>('/auth/me'),
  listOnlineUsers: () => request<OnlineUser[]>('/auth/online-users'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword })
    }),
  changeDisplayName: (displayName: string) =>
    request<{ userId: string; username: string; displayName: string; isAdmin: boolean; hasAvatar: boolean }>('/auth/change-display-name', {
      method: 'POST',
      body: JSON.stringify({ displayName })
    }),
  setMyAvatar: (dataUrl: string) =>
    request<void>('/auth/avatar', { method: 'PUT', body: JSON.stringify({ dataUrl }) }),
  deleteMyAvatar: () => request<void>('/auth/avatar', { method: 'DELETE' }),
  userAvatarUrl: (userId: string) => `${BASE}/users/${userId}/avatar`,

  listAdminUsers: () => request<AdminUser[]>('/admin/users'),
  createAdminUser: (req: { username: string; password: string; displayName?: string; isAdmin: boolean }) =>
    request<AdminUser>('/admin/users', { method: 'POST', body: JSON.stringify(req) }),
  updateAdminUser: (id: string, req: { displayName?: string; password?: string; isAdmin?: boolean }) =>
    request<AdminUser>(`/admin/users/${id}`, { method: 'PATCH', body: JSON.stringify(req) }),
  deleteAdminUser: (id: string) => request<void>(`/admin/users/${id}`, { method: 'DELETE' }),

  listRooms: () => request<RoomSummary[]>('/rooms'),
  listRoomHistory: () => request<RoomHistory[]>('/rooms/history'),
  getRoomHistory: (code: string) => request<RoomHistory>(`/rooms/history/${code.toUpperCase()}`),
  deleteRoomHistory: (id: string) => request<void>(`/rooms/history/${id}`, { method: 'DELETE' }),
  saveSponsorPlan: (code: string, plan: RoomSponsorEntry[]) =>
    request<RoomHistory>(`/rooms/history/${code.toUpperCase()}/sponsor`, { method: 'PUT', body: JSON.stringify({ plan }) }),
  skipSponsor: (code: string) =>
    request<RoomHistory>(`/rooms/history/${code.toUpperCase()}/sponsor/skip`, { method: 'POST' }),
  saveLuckyWheel: (code: string, body: { min: number; max: number; double: boolean; result: number }) =>
    request<RoomHistory>(`/rooms/history/${code.toUpperCase()}/wheel`, { method: 'PUT', body: JSON.stringify(body) }),
  createRoom: (gameType: number, maxSeats: number, name?: string) =>
    request<RoomSummary>('/rooms', { method: 'POST', body: JSON.stringify({ gameType, maxSeats, name }) }),
  getRoom: (code: string) => request<RoomState>(`/rooms/${code.toUpperCase()}`),
  deleteRoom: (id: string) => request<void>(`/rooms/${id}`, { method: 'DELETE' })
};

export interface AdminUser {
  id: string;
  username: string;
  displayName: string;
  isAdmin: boolean;
  createdAt: string;
}

export interface OnlineUser {
  userId: string;
  username: string;
  displayName: string;
  hasAvatar: boolean;
}

export const RoomStatus = { Waiting: 0, Playing: 1, Finished: 2 } as const;
export type RoomStatusValue = typeof RoomStatus[keyof typeof RoomStatus];

export interface RoomSummary {
  id: string;
  code: string;
  name: string | null;
  gameType: number;
  maxSeats: number;
  status: RoomStatusValue;
  occupiedSeats: number;
  hostDisplayName: string;
  createdAt: string;
  finishedAt: string | null;
}

export interface RoomFinalScoreEntry {
  userId: string;
  displayName: string;
  totalScore: number;
  hasAvatar: boolean;
}

export interface RoomSponsorEntry {
  fromUserId: string;
  toUserId: string;
  amount: number;
}

export interface LuckyWheelResult {
  min: number;
  max: number;
  double: boolean;
  result: number;
  spinnerUserId: string;
}

export interface LuckyWheelPreview {
  pool: number[];
  min: number;
  max: number;
  double: boolean;
  spinnerUserId: string;
}

export interface RoomHistory {
  id: string;
  code: string;
  name: string | null;
  maxSeats: number;
  hostDisplayName: string;
  createdAt: string;
  finishedAt: string | null;
  finalScores: RoomFinalScoreEntry[];
  sponsorPlan: RoomSponsorEntry[] | null;
  luckyWheel: LuckyWheelResult | null;
  sponsorDecidedDonors: string[] | null;
  luckyWheelPreview: LuckyWheelPreview | null;
}

export interface RoomSeat {
  seatIndex: number;
  userId: string;
  username: string;
  displayName: string;
  isHost: boolean;
  isOnline: boolean;
  hasAvatar: boolean;
}

export interface RoomState {
  id: string;
  code: string;
  name: string | null;
  gameType: number;
  maxSeats: number;
  status: RoomStatusValue;
  hostUserId: string;
  createdAt: string;
  startedAt: string | null;
  seats: RoomSeat[];
  showOpponentCardCount: boolean;
}

export const HUB_BASE = RAW_BASE;

export interface CardDto {
  rank: number;
  suit: number;
}

export const MatchStatus = {
  InProgress: 0,
  Finished: 1,
  WaitingNextRound: 2,
  WhiteWinChoice: 3,
  PendingTrickCut: 4,
  VoteReset: 5,
  FestivalReveal: 6,
  XiDachPlaying: 7,
  XiDachCompare: 8,
  BreakRps: 9,
  BreakMathPick: 10,
  BreakMathQuiz: 11,
  BreakMemoryView: 12,
  BreakMemoryQuiz: 13,
  BreakReflexCooldown: 14,
  BreakReflexPlay: 15,
  BreakSelect: 16,
  BreakIntro: 17,
} as const;

export const RpsChoice = { None: 0, Rock: 1, Paper: 2, Scissors: 3 } as const;
export const RpsStage = { Round1A: 0, Round1B: 1, ThirdPlace: 2, Final: 3, Done: 4 } as const;
export const BreakGameType = { None: 0, Rps: 1, Math: 2, Memory: 3, Reflex: 4 } as const;

export interface RpsMatchup {
  playerAId: string;
  playerBId: string;
  winTarget: number;
  winsA: number;
  winsB: number;
  winnerId: string | null;
  loserId: string | null;
  aChosen: boolean;
  bChosen: boolean;
  lastChoiceA: number;
  lastChoiceB: number;
  lastOutcome: number; // 0 draw, 1 A, 2 B
  hasLast: boolean;
}
export interface RpsState {
  stage: number;
  round1A: RpsMatchup;
  round1B: RpsMatchup;
  thirdPlace: RpsMatchup;
  final: RpsMatchup;
  finalRanking: string[];
}

// ---- Giải Lao (Tính toán) ----
export interface MathPick { userId: string; number: number; }
export interface MathPlayerResult {
  userId: string;
  chosenIndex: number;      // -1 nếu chưa/không lộ (chỉ lộ ở pha reveal)
  answered: boolean;
  correct: boolean;
  elapsedMs: number;
  correctCount: number;
  totalCorrectMs: number;
}
export interface MathQuestion {
  expression: string;
  options: number[];
  correctIndex: number;     // -1 khi đang trả lời, ≥0 ở pha reveal
}
export interface MathQuizState {
  phase: number;            // 0 chọn số, 1 trả lời, 2 hiện đáp án
  picks: MathPick[];
  totalQuestions: number;
  currentQuestion: number;
  question: MathQuestion | null;
  answeredUserIds: string[];
  results: MathPlayerResult[];
  finalRanking: string[];
}

// ---- Giải Lao (Trí nhớ) ----
export interface MemoryQuestion {
  cellIndex: number;        // ô 0-8 đang hỏi
  options: string[];        // 4 slug CLB đáp án
  correctIndex: number;     // -1 khi đang trả lời, ≥0 ở pha reveal
}
export interface MemoryGameState {
  phase: number;            // 0 xem lưới, 1 trả lời, 2 hiện đáp án
  grid: string[] | null;    // 9 slug CLB (chỉ pha view)
  totalQuestions: number;
  currentQuestion: number;
  question: MemoryQuestion | null;
  answerSlug: string | null;     // slug đúng (chỉ pha reveal)
  answeredUserIds: string[];
  results: MathPlayerResult[];
}

// ---- Giải Lao (Phản xạ) — bài 52 lá, lưới 4×4, tìm 3 lá ----
export interface ReflexGameState {
  phase: number;            // 0 cooldown (ẩn lưới), 1 click, 2 hiện đáp án
  grid: CardDto[];          // 16 lá (rỗng ở pha cooldown)
  totalRounds: number;
  currentRound: number;
  targetCards: CardDto[] | null;  // đề: 3 lá cần tìm (pha click + reveal)
  targetIndexes: number[] | null; // 3 ô đúng (chỉ ở reveal)
  answeredUserIds: string[];
  results: MathPlayerResult[];
}

export interface MatchPlayerPublic {
  userId: string;
  displayName: string;
  seatIndex: number;
  cardsLeft: number;
  finalRank: number | null;
  passedThisTrick: boolean;
  totalScore: number;
  whiteWinReason: string | null;
  whiteWinAccepted: boolean | null;
  hasAvatar: boolean;
  surrendered: boolean;
  voteResetChoice: boolean | null;
  hasUsedVoteReset: boolean;
  hasUsedFestival: boolean;
  festivalWinner: boolean;
  festivalRevealed: number;
  festivalCardSlots: (CardDto | null)[] | null;
  hasUsedStarOfHope: boolean;
  isStarOfHope: boolean;
  hasUsedXiDach: boolean;
  isXiDachDealer: boolean;
  xiDachStood: boolean;
  xiDachSettled: boolean;
  xiDachRevealed: boolean;
  xiDachVisibleTotal: number;
  xiDachVisibleCards: CardDto[] | null;
  winStreak: number;
  isGambling: boolean;
  hasUsedBreak: boolean;
}

export interface MatchPublicState {
  matchId: string;
  roomId: string;
  status: number;
  roundNumber: number;
  currentTurnSeatIndex: number;
  currentTrickOwnerId: string | null;
  currentTrick: CardDto[] | null;
  turnDeadline: string;
  nextRoundAt: string | null;
  hostUserId: string;
  players: MatchPlayerPublic[];
  whiteWinDeadline: string | null;
  trickCutDeadline: string | null;
  pendingTrickWinnerId: string | null;
  trickCutCandidates: string[] | null;
  lastWonTrick: CardDto[] | null;
  lastWonTrickWinnerId: string | null;
  showOpponentCardCount: boolean;
  voteResetDeadline: string | null;
  voteResetInitiatorId: string | null;
  pastFirstTrick: boolean;
  festivalScheduled: boolean;
  isFestivalRound: boolean;
  festivalOrganizerId: string | null;
  festivalRevealDeadline: string | null;
  festivalAutoFlipDeadline: string | null;
  starOfHopeScheduledUserId: string | null;
  xiDachScheduledUserId: string | null;
  isXiDachRound: boolean;
  xiDachDealerId: string | null;
  xiDachTurnUserId: string | null;
  xiDachTurnDeadline: string | null;
  gambleOfferUserId: string | null;
  gambleScheduledUserId: string | null;
  isGambleRound: boolean;
  gambleOfferDeadline: string | null;
  breakScheduled: boolean;
  breakOrganizerId: string | null;
  isBreakRound: boolean;
  breakSelectDeadline: string | null;   // pha chọn game (30s → random)
  breakIntroDeadline: string | null;    // pha hiện luật (30s → tự bắt đầu)
  rps: RpsState | null;
  rpsChoiceDeadline: string | null;
  rpsRevealUntil: string | null;
  breakGame: number;        // BreakGameType: 0 none, 1 Rps, 2 Math, 3 Memory, 4 Reflex
  math: MathQuizState | null;
  mathPickDeadline: string | null;
  mathAnswerDeadline: string | null;
  mathRevealUntil: string | null;
  memory: MemoryGameState | null;
  memoryViewDeadline: string | null;
  memoryAnswerDeadline: string | null;
  memoryRevealUntil: string | null;
  reflex: ReflexGameState | null;
  reflexCooldownUntil: string | null;
  reflexAnswerDeadline: string | null;
  reflexRevealUntil: string | null;
}

export interface PrivateHand {
  matchId: string;
  hand: CardDto[];
}

export interface RoundResultEntry {
  userId: string;
  displayName: string;
  finalRank: number;
  roundScore: number;
  totalScore: number;
  whiteWinReason: string | null;
  chopBonus: number;
  wonByThreeOfSpades: boolean;
  lostByThreeOfSpades: boolean;
  judgeIsWinner: boolean;
  judgeIsVictim: boolean;
  judgeIsPardoned: boolean;
  judgeHeldValue: number;
  baseRankScore: number;
  threeOfSpadesDelta: number;
  judgeDelta: number;
  whiteWinDelta: number;
  heldPenaltyDelta: number;
  held: HeldItems;
  heldDetails: HeldDetail[];
  festivalDelta: number;
  festivalWinner: boolean;
  festivalCards: CardDto[] | null;
  festivalLabel: string | null;
  starDelta: number;
  isStar: boolean;
  chopLabels: string[] | null;
  chopIsCutter: boolean;
  xiDachCards: CardDto[] | null;
  xiDachLabel: string | null;
  xiDachIsDealer: boolean;
  xiDachTotal: number;
  gambleDelta: number;
  isGamble: boolean;
  mathCorrectCount: number;
  mathTotalCorrectMs: number;
  mathResults: MathQuestionResult[] | null;
}

export interface MathQuestionResult {
  correct: boolean;
  answered: boolean;
  elapsedMs: number;
}

export interface HeldItems {
  blackPigs: number;
  redPigs: number;
  hasFourOfAKind: boolean;
  hasThreePairRun: boolean;
  hasFourPairRun: boolean;
}

export interface HeldDetail {
  label: string;
  value: number;
}

export interface RoundEnd {
  matchId: string;
  roundNumber: number;
  wasWhiteWin: boolean;
  wasJudge: boolean;
  results: RoundResultEntry[];
  wasFestival: boolean;
  wasXiDach: boolean;
  wasBreak: boolean;
  breakGame: number;        // BreakGameType: 1 Rps, 2 Math (chỉ ý nghĩa khi wasBreak)
}

export interface MatchEnd {
  matchId: string;
  finalScores: RoundResultEntry[];
}

export interface RoundHistory {
  matchId: string;
  rounds: RoundEnd[];
}

export interface ChatMessage {
  id: string;
  userId: string;
  displayName: string;
  text: string;
  createdAt: string;
}

export interface ChatHistory {
  messages: ChatMessage[];
}
