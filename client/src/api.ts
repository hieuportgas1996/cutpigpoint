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
  createGame: (playerIds: string[], type: GameTypeValue = GameType.TienLenMienNam) =>
    request<Game>('/games', { method: 'POST', body: JSON.stringify({ playerIds, type }) }),
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
    request<{ token: string; expiresAt: string; username: string }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password })
    }),
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
  me: () => request<{ username: string }>('/auth/me'),
  updateAccount: (currentPassword: string, newUsername: string | null, newPassword: string | null) =>
    request<{ username: string }>('/auth/account', {
      method: 'PUT',
      body: JSON.stringify({ currentPassword, newUsername, newPassword })
    })
};
