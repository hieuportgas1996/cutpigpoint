import { useMemo } from 'react';
import type { MatchPlayerPublic, MatchPairsState } from '../api';
import { Avatar } from '../ui/Avatar';
import { CardSvg } from '../game/CardSvg';
import { cardFromDto } from '../game/cards';
import './matchpairs-break.css';

const ORDER_MEDAL = ['1️⃣', '2️⃣', '3️⃣', '4️⃣'];

/**
 * Màn "Giải lao — Cơ hội" (Match Pairs) full-screen overlay. 2 pha (matchPairs.phase):
 *  0 = quay thứ tự: người tổ chức bấm "Quay" → hiện thứ tự 1-2-3-4.
 *  1 = chơi: theo lượt, người tới lượt lật 2 lá. Trúng cặp → đi tiếp; trật → úp lại (1.5s) + qua lượt.
 * Ô còn úp = mặt sau; ô đã match / đang ngửa = hiện lá. Đồng bộ qua server.
 */
export function MatchPairsBreakScreen({
  mp, players, myUserId, isOrganizer, spinLeftSec, totalLeftSec, turnLeftSec, mismatchActive, onSpin, onFlip,
}: {
  mp: MatchPairsState;
  players: MatchPlayerPublic[];
  myUserId: string;
  isOrganizer: boolean;
  spinLeftSec: number;
  totalLeftSec: number;
  turnLeftSec: number;       // 10s đồng hồ lượt hiện tại
  mismatchActive: boolean;   // đang chờ úp 2 lá trật (khoá click)
  onSpin: () => void;
  onFlip: (cellIndex: number) => void;
}) {
  const nameOf = useMemo(() => {
    const m: Record<string, string> = {};
    for (const p of players) m[p.userId] = p.userId === myUserId ? 'Bạn' : p.displayName;
    return m;
  }, [players, myUserId]);
  const avatarOf = useMemo(() => {
    const m: Record<string, boolean> = {};
    for (const p of players) m[p.userId] = p.hasAvatar;
    return m;
  }, [players]);

  const seats = [...players].sort((a, b) => a.seatIndex - b.seatIndex);
  const playerOf: Record<string, MatchPairsState['players'][number]> = {};
  for (const pp of mp.players) playerOf[pp.userId] = pp;

  const isMyTurn = mp.phase === 1 && mp.turnUserId === myUserId;
  const turnName = mp.turnUserId ? (mp.turnUserId === myUserId ? 'Bạn' : nameOf[mp.turnUserId] ?? '') : '';
  const canFlip = isMyTurn && !mismatchActive && mp.flipped.length < 2;

  // ---- Pha 0: quay thứ tự ----
  if (mp.phase === 0) {
    return (
      <div className="mpr-overlay">
        <div className="mpr-card">
          <div className="mpr-title">🎴 Cơ hội — Tìm cặp giống nhau</div>
          {isOrganizer ? (
            <>
              <div className="mpr-sub">Bấm để quay thứ tự đi ({spinLeftSec}s)</div>
              <button className="mpr-spin-btn" onClick={onSpin}>🎲 Quay thứ tự</button>
            </>
          ) : (
            <div className="mpr-sub">Chờ người tổ chức quay thứ tự… ({spinLeftSec}s)</div>
          )}
          <div className="mpr-players">
            {seats.map(p => (
              <div key={p.userId} className={`mpr-player ${p.userId === myUserId ? 'me' : ''}`}>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="mpr-player-name">{nameOf[p.userId] ?? '?'}</div>
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  // ---- Pha 1: chơi ----
  return (
    <div className="mpr-overlay">
      <div className="mpr-card">
        <div className="mpr-title">🎴 Cơ hội</div>
        <div className="mpr-sub">
          {isMyTurn ? <b className="mpr-myturn">Lượt của bạn — lật 2 lá!</b> : <>Lượt: <b>{turnName}</b></>}
          {!mismatchActive && <> · ⏱ <b className={turnLeftSec <= 3 ? 'low' : ''}>{turnLeftSec}s</b></>}
          {' · '}<span className="mpr-total">⌛ {totalLeftSec}s</span>
        </div>

        {/* Lưới 4×4 */}
        <div className="mpr-grid">
          {mp.cells.map((c, i) => {
            const matched = mp.matched[i];
            const faceUp = c != null; // đã match hoặc đang ngửa
            const cls = ['mpr-cell', faceUp ? 'up' : 'down', matched ? 'matched' : ''].join(' ');
            return (
              <button
                key={i}
                className={cls}
                disabled={!canFlip || faceUp}
                onClick={() => onFlip(i)}
              >
                {faceUp
                  ? <CardSvg card={cardFromDto(c!)} size="sm" />
                  : <div className="mpr-back" />}
              </button>
            );
          })}
        </div>

        {/* Bảng thứ tự + số cặp */}
        <div className="mpr-players">
          {seats.map(p => {
            const pp = playerOf[p.userId];
            const isTurn = mp.turnUserId === p.userId;
            return (
              <div key={p.userId} className={`mpr-player ${p.userId === myUserId ? 'me' : ''} ${isTurn ? 'turn' : ''}`}>
                <span className="mpr-order">{ORDER_MEDAL[(pp?.turnOrder ?? 1) - 1] ?? ''}</span>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="mpr-player-name">{nameOf[p.userId] ?? '?'}</div>
                <div className="mpr-player-pairs">🎴 {pp?.pairs ?? 0}</div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
