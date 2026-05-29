import { useCallback, useEffect, useRef } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { auth, HUB_BASE, RoomHistory } from '../api';

export interface WheelSpinStartedPayload {
  pool: number[];
  resultIndex: number;
  min: number;
  max: number;
  double: boolean;
  spinnerUserId: string;
}

interface UseHistorySocketArgs {
  code: string | undefined;
  onHistoryUpdated?: (h: RoomHistory) => void;
  onWheelSpinStarted?: (payload: WheelSpinStartedPayload) => void;
}

interface UseHistorySocketResult {
  startLuckyWheelSpin: (min: number, max: number, doubled: boolean) => Promise<void>;
}

/**
 * SignalR connection scoped to the room history page. Joins the room group via JoinRoom so the same
 * RoomHub broadcasts (HistoryUpdated, WheelSpinStarted) reach every viewer in lock-step.
 */
export function useHistorySocket({ code, onHistoryUpdated, onWheelSpinStarted }: UseHistorySocketArgs): UseHistorySocketResult {
  const connectionRef = useRef<HubConnection | null>(null);
  // Stash callbacks in refs so the connection effect doesn't tear down when the parent re-renders
  // with fresh closures.
  const onHistoryUpdatedRef = useRef(onHistoryUpdated);
  const onWheelSpinStartedRef = useRef(onWheelSpinStarted);
  useEffect(() => { onHistoryUpdatedRef.current = onHistoryUpdated; }, [onHistoryUpdated]);
  useEffect(() => { onWheelSpinStartedRef.current = onWheelSpinStarted; }, [onWheelSpinStarted]);

  useEffect(() => {
    if (!code) return;
    const token = auth.getToken();
    if (!token) return;

    const url = `${HUB_BASE}/hubs/room?access_token=${encodeURIComponent(token)}`;
    const conn = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = conn;

    conn.on('HistoryUpdated', (h: RoomHistory) => {
      onHistoryUpdatedRef.current?.(h);
    });
    conn.on('WheelSpinStarted', (p: WheelSpinStartedPayload) => {
      onWheelSpinStartedRef.current?.(p);
    });

    (async () => {
      try {
        await conn.start();
        await conn.invoke('JoinRoom', code);
      } catch {
        /* swallow — history page still works without realtime */
      }
    })();

    return () => {
      if (conn.state !== HubConnectionState.Disconnected) {
        conn.stop().catch(() => undefined);
      }
      connectionRef.current = null;
    };
  }, [code]);

  const startLuckyWheelSpin = useCallback(async (min: number, max: number, doubled: boolean) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối realtime.');
    await conn.invoke('StartLuckyWheelSpin', code, min, max, doubled);
  }, [code]);

  return { startLuckyWheelSpin };
}
