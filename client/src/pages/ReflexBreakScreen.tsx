import { useMemo } from 'react';
import type { MatchPlayerPublic, ReflexGameState } from '../api';
import { Avatar } from '../ui/Avatar';
import { ShapeSvg, shapeName, colorName, COLOR_HEX } from '../game/ShapeSvg';
import './reflex-break.css';

/**
 * Màn "Giải lao — Phản xạ" full-screen overlay. 3 pha (reflex.phase):
 *  0 = cooldown 3s: hiện lưới 3×3 hình (đếm ngược chuẩn bị), chưa cho click.
 *  1 = click: hiện đề "Tìm hình X màu Y" → bấm đúng ô, 10s.
 *  2 = hiện đáp án: tô ô đúng + ai đúng + thời gian; bảng điểm tích lũy.
 */
export function ReflexBreakScreen({
  reflex, players, myUserId, cooldownLeftSec, answerLeftSec, myCellIdx, onPick,
}: {
  reflex: ReflexGameState;
  players: MatchPlayerPublic[];
  myUserId: string;
  cooldownLeftSec: number;
  answerLeftSec: number;
  myCellIdx: number | null;
  onPick: (cellIndex: number) => void;
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
  const cooldown = reflex.phase === 0;
  const reveal = reflex.phase === 2;
  const answered = new Set(reflex.answeredUserIds);
  const resultOf: Record<string, ReflexGameState['results'][number]> = {};
  for (const r of reflex.results) resultOf[r.userId] = r;
  const iAnswered = answered.has(myUserId) || myCellIdx != null;
  const canClick = reflex.phase === 1 && !iAnswered;

  return (
    <div className="rfx-overlay">
      <div className="rfx-card">
        <div className="rfx-title">⚡ Phản xạ</div>
        <div className="rfx-sub">Lượt {reflex.currentRound + 1}/{reflex.totalRounds}</div>

        {/* Đề bài / cooldown */}
        {cooldown ? (
          <div className="rfx-prompt rfx-prompt-cooldown">
            Chuẩn bị… <b className="rfx-count">{cooldownLeftSec}</b>
          </div>
        ) : (
          <div className="rfx-prompt">
            Tìm nhanh: <b>{reflex.targetShape ? shapeName(reflex.targetShape) : ''}</b>
            {' '}màu <b style={reflex.targetColor ? { color: COLOR_HEX[reflex.targetColor] ?? undefined } : undefined}>{reflex.targetColor ? colorName(reflex.targetColor) : ''}</b>
            {!reveal && <> · <b className={answerLeftSec <= 3 ? 'low' : ''}>{answerLeftSec}s</b></>}
          </div>
        )}

        {/* Lưới 3×3 */}
        <div className="rfx-grid">
          {reflex.grid.map((cell, i) => {
            const isTarget = reveal && i === reflex.targetIndex;
            const isMine = myCellIdx === i;
            const isWrongMine = reveal && isMine && i !== reflex.targetIndex;
            return (
              <button
                key={i}
                className={`rfx-cell ${isTarget ? 'target' : ''} ${isWrongMine ? 'wrong' : ''} ${isMine ? 'mine' : ''}`}
                disabled={!canClick}
                onClick={() => canClick && onPick(i)}
                title={`${shapeName(cell.shape)} ${colorName(cell.color)}`}
              >
                <ShapeSvg shape={cell.shape} color={cell.color} size={56} />
              </button>
            );
          })}
        </div>

        {/* Người chơi */}
        <div className="rfx-players">
          {seats.map(p => {
            const r = resultOf[p.userId];
            const hasAnswered = answered.has(p.userId);
            return (
              <div key={p.userId} className={`rfx-player ${p.userId === myUserId ? 'me' : ''}`}>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="rfx-player-name">{nameOf[p.userId] ?? '?'}</div>
                {reveal ? (
                  <div className={`rfx-player-result ${r?.correct ? 'ok' : 'no'}`}>
                    {r?.correct ? `✅ ${(r.elapsedMs / 1000).toFixed(1)}s` : (r?.answered ? '❌' : '⏰')}
                  </div>
                ) : (
                  <div className={`rfx-player-status ${hasAnswered ? 'ans' : 'wait'}`}>
                    {cooldown ? '…' : (hasAnswered ? '✔' : '…')}
                  </div>
                )}
                <div className="rfx-player-score" title="Số lượt đúng">🎯 {r?.correctCount ?? 0}</div>
              </div>
            );
          })}
        </div>

        {reflex.phase === 1 && iAnswered && (
          <div className="rfx-status">⏳ Đã chọn — chờ mọi người / hết giờ…</div>
        )}
        {reveal && (
          <div className="rfx-status">Ô đúng đã sáng · lượt kế ngay…</div>
        )}
      </div>
    </div>
  );
}
