import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { auth, HUB_BASE, RoomState } from '../api';

type Status = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error';

interface UseRoomConnectionResult {
  status: Status;
  state: RoomState | null;
  error: string | null;
  takeSeat: (seatIndex: number) => Promise<void>;
  leaveSeat: () => Promise<void>;
  startGame: () => Promise<void>;
  onGameStarted: (handler: (roomId: string) => void) => () => void;
}

export function useRoomConnection(code: string | undefined): UseRoomConnectionResult {
  const [status, setStatus] = useState<Status>('idle');
  const [state, setState] = useState<RoomState | null>(null);
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

  const onGameStarted = useCallback((handler: (roomId: string) => void) => {
    gameStartedHandlersRef.current.add(handler);
    return () => { gameStartedHandlersRef.current.delete(handler); };
  }, []);

  return { status, state, error, takeSeat, leaveSeat, startGame, onGameStarted };
}
