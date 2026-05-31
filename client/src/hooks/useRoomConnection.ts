import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { auth, CardDto, ChatHistory, ChatMessage, HUB_BASE, MatchEnd, MatchPublicState, PrivateHand, RoomState, RoundEnd, RoundHistory } from '../api';

type Status = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error';

interface UseRoomConnectionResult {
  status: Status;
  state: RoomState | null;
  matchState: MatchPublicState | null;
  privateHand: PrivateHand | null;
  roundEnd: RoundEnd | null;
  roundHistory: RoundEnd[];
  matchEnd: MatchEnd | null;
  chatMessages: ChatMessage[];
  error: string | null;
  takeSeat: (seatIndex: number) => Promise<void>;
  leaveSeat: () => Promise<void>;
  startGame: () => Promise<void>;
  setShowOpponentCardCount: (show: boolean) => Promise<void>;
  startNextRound: () => Promise<void>;
  endMatch: () => Promise<void>;
  playCards: (cards: CardDto[]) => Promise<void>;
  passTurn: () => Promise<void>;
  respondWhiteWin: (accept: boolean) => Promise<void>;
  cutNewTrick: (cards: CardDto[]) => Promise<void>;
  declineTrickCut: () => Promise<void>;
  sendChat: (text: string) => Promise<void>;
  requestMatchState: () => Promise<void>;
  clearRoundEnd: () => void;
  onGameStarted: (handler: (roomId: string) => void) => () => void;
}

export function useRoomConnection(code: string | undefined): UseRoomConnectionResult {
  const [status, setStatus] = useState<Status>('idle');
  const [state, setState] = useState<RoomState | null>(null);
  const [matchState, setMatchState] = useState<MatchPublicState | null>(null);
  const [privateHand, setPrivateHand] = useState<PrivateHand | null>(null);
  const [roundEnd, setRoundEnd] = useState<RoundEnd | null>(null);
  const [roundHistory, setRoundHistory] = useState<RoundEnd[]>([]);
  const [matchEnd, setMatchEnd] = useState<MatchEnd | null>(null);
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([]);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const gameStartedHandlersRef = useRef<Set<(roomId: string) => void>>(new Set());

  useEffect(() => {
    if (!code) return;
    const token = auth.getToken();
    if (!token) {
      setError('Chưa đăng nhập.');
      setStatus('error');
      return;
    }

    setStatus('connecting');
    const url = `${HUB_BASE}/hubs/room?access_token=${encodeURIComponent(token)}`;
    const conn = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = conn;

    conn.on('RoomState', (newState: RoomState) => {
      setState(newState);
    });

    conn.on('GameStarted', (roomId: string) => {
      gameStartedHandlersRef.current.forEach(h => h(roomId));
    });

    conn.on('MatchState', (m: MatchPublicState) => {
      setMatchState(m);
    });

    conn.on('PrivateHand', (h: PrivateHand) => {
      setPrivateHand(h);
    });

    conn.on('RoundEnd', (e: RoundEnd) => {
      setRoundEnd(e);
      setRoundHistory(prev => {
        if (prev.some(r => r.matchId === e.matchId && r.roundNumber === e.roundNumber)) return prev;
        return [...prev, e];
      });
    });

    conn.on('RoundHistory', (h: RoundHistory) => {
      setRoundHistory(h.rounds ?? []);
    });

    conn.on('MatchEnd', (e: MatchEnd) => {
      setMatchEnd(e);
    });

    conn.on('GameStarted', () => {
      // New match starts → reset history of previous match.
      setRoundHistory([]);
    });

    conn.on('ChatHistory', (h: ChatHistory) => {
      setChatMessages(h.messages ?? []);
    });

    conn.on('ChatMessage', (m: ChatMessage) => {
      setChatMessages(prev => {
        if (prev.some(x => x.id === m.id)) return prev;
        const next = [...prev, m];
        return next.length > 200 ? next.slice(next.length - 200) : next;
      });
    });

    conn.onreconnecting(() => setStatus('reconnecting'));
    conn.onreconnected(async () => {
      setStatus('connected');
      try {
        const fresh = await conn.invoke<RoomState>('JoinRoom', code);
        if (fresh) setState(fresh);
      } catch (e) {
        setError((e as Error).message);
      }
    });
    conn.onclose((err) => {
      if (err) {
        setError(err.message);
        setStatus('error');
      } else {
        setStatus('disconnected');
      }
    });

    (async () => {
      try {
        await conn.start();
        setStatus('connected');
        const initial = await conn.invoke<RoomState>('JoinRoom', code);
        if (initial) setState(initial);
      } catch (e) {
        setError((e as Error).message);
        setStatus('error');
      }
    })();

    return () => {
      if (conn.state !== HubConnectionState.Disconnected) {
        conn.stop().catch(() => undefined);
      }
      connectionRef.current = null;
    };
  }, [code]);

  const takeSeat = useCallback(async (seatIndex: number) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('TakeSeat', seatIndex);
  }, []);

  const leaveSeat = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('LeaveSeat');
  }, []);

  const startGame = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('StartGame');
  }, []);

  const setShowOpponentCardCount = useCallback(async (show: boolean) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('SetShowOpponentCardCount', show);
  }, []);

  const startNextRound = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    setRoundEnd(null);
    await conn.invoke('StartNextRound');
  }, []);

  const endMatch = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('EndMatch');
  }, []);

  const clearRoundEnd = useCallback(() => setRoundEnd(null), []);

  const playCards = useCallback(async (cards: CardDto[]) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('PlayCards', cards);
  }, []);

  const passTurn = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('PassTurn');
  }, []);

  const respondWhiteWin = useCallback(async (accept: boolean) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('RespondWhiteWin', accept);
  }, []);

  const cutNewTrick = useCallback(async (cards: CardDto[]) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('CutNewTrick', cards);
  }, []);

  const declineTrickCut = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('DeclineTrickCut');
  }, []);

  const sendChat = useCallback(async (text: string) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('SendChat', text);
  }, []);

  const requestMatchState = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối phòng.');
    await conn.invoke('RequestMatchState');
  }, []);

  const onGameStarted = useCallback((handler: (roomId: string) => void) => {
    gameStartedHandlersRef.current.add(handler);
    return () => { gameStartedHandlersRef.current.delete(handler); };
  }, []);

  return {
    status, state, matchState, privateHand, roundEnd, roundHistory, matchEnd, chatMessages, error,
    takeSeat, leaveSeat, startGame, startNextRound, endMatch,
    playCards, passTurn, respondWhiteWin, cutNewTrick, declineTrickCut,
    sendChat, requestMatchState, clearRoundEnd, onGameStarted, setShowOpponentCardCount
  };
}
