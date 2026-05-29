import { useCallback, useEffect, useRef } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { auth, HUB_BASE, LuckyWheelPreview, RoomHistory } from '../api';

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
  onWheelPreview?: (payload: LuckyWheelPreview) => void;
}

interface UseHistorySocketResult {
  createLuckyWheelPreview: (min: number, max: number, doubled: boolean) => Promise<void>;
  startLuckyWheelSpin: () => Promise<void>;
}

/**
 * SignalR connection scoped to the room history page. Joins the room group via JoinRoom so the same
 * RoomHub broadcasts (HistoryUpdated, WheelSpinStarted) reach every viewer in lock-step.
 */
export function useHistorySocket({ code, onHistoryUpdated, onWheelSpinStarted, onWheelPreview }: UseHistorySocketArgs): UseHistorySocketResult {
  const connectionRef = useRef<HubConnection | null>(null);
  const onHistoryUpdatedRef = useRef(onHistoryUpdated);
  const onWheelSpinStartedRef = useRef(onWheelSpinStarted);
  const onWheelPreviewRef = useRef(onWheelPreview);
  useEffect(() => { onHistoryUpdatedRef.current = onHistoryUpdated; }, [onHistoryUpdated]);
  useEffect(() => { onWheelSpinStartedRef.current = onWheelSpinStarted; }, [onWheelSpinStarted]);
  useEffect(() => { onWheelPreviewRef.current = onWheelPreview; }, [onWheelPreview]);

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
    conn.on('WheelPreview', (p: LuckyWheelPreview) => {
      onWheelPreviewRef.current?.(p);
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

  const createLuckyWheelPreview = useCallback(async (min: number, max: number, doubled: boolean) => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối realtime.');
    await conn.invoke('CreateLuckyWheelPreview', code, min, max, doubled);
  }, [code]);

  const startLuckyWheelSpin = useCallback(async () => {
    const conn = connectionRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) throw new Error('Chưa kết nối realtime.');
    await conn.invoke('StartLuckyWheelSpin', code);
  }, [code]);

  return { createLuckyWheelPreview, startLuckyWheelSpin };
}
