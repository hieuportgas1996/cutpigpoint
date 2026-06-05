import { useMemo } from 'react';
import type { MatchPlayerPublic, ReflexGameState } from '../api';
import { Avatar } from '../ui/Avatar';
import { CardSvg } from '../game/CardSvg';
import { cardFromDto } from '../game/cards';
import './reflex-break.css';

/**
 * Màn "Giải lao — Phản xạ" full-screen overlay — bài 52 lá, lưới 4×4, tìm 3 lá. 3 pha (reflex.phase):
 *  0 = cooldown 3s: lưới 16 ô ẩn ("?"), đếm ngược chuẩn bị.
 *  1 = click: hiện lưới + đề "Tìm 3 lá: A B C" → bấm đủ 3 lá (chọn lá thứ 3 = chốt), 15s.
 *  2 = hiện đáp án: tô 3 ô đúng + ai đúng + thời gian; bảng điểm tích lũy.
 */
export function ReflexBreakScreen({
  reflex, players, myUserId, cooldownLeftSec, answerLeftSec, mySelected, onPick,
}: {
  reflex: ReflexGameState;
  players: MatchPlayerPublic[];
  myUserId: string;
  cooldownLeftSec: number;
  answerLeftSec: number;
  mySelected: number[];     // các ô MÌNH đã chọn lượt này (client nhớ)
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
  const targetSet = new Set(reflex.targetIndexes ?? []);
  const mySel = new Set(mySelected);
  const iDone = answered.has(myUserId) || mySelected.length >= 3;
  const canClick = reflex.phase === 1 && !iDone;

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
            <div>Tìm nhanh 3 lá {!reveal && <b className={answerLeftSec <= 3 ? 'low' : ''}>· {answerLeftSec}s</b>}{' '}
              {!reveal && <span className="rfx-progress">({mySelected.length}/3)</span>}
            </div>
            <div className="rfx-target-cards">
              {(reflex.targetCards ?? []).map((c, i) => (
                <CardSvg key={i} card={cardFromDto(c)} size="sm" />
              ))}
            </div>
          </div>
        )}

        {/* Lưới 4×4. Cooldown: lưới rỗng → 16 ô "?". */}
        <div className="rfx-grid">
          {cooldown
            ? Array.from({ length: 16 }).map((_, i) => (
                <div key={i} className="rfx-cell rfx-cell-hidden" aria-hidden="true">
                  <span className="rfx-qmark">?</span>
                </div>
              ))
            : reflex.grid.map((c, i) => {
                const isTarget = reveal && targetSet.has(i);
                const isMine = mySel.has(i);
                const isWrongMine = reveal && isMine && !targetSet.has(i);
                return (
                  <button
                    key={i}
                    className={`rfx-cell rfx-cell-card ${isTarget ? 'target' : ''} ${isWrongMine ? 'wrong' : ''} ${isMine ? 'mine' : ''}`}
                    disabled={!canClick || isMine}
                    onClick={() => canClick && !isMine && onPick(i)}
                  >
                    <CardSvg card={cardFromDto(c)} size="sm" />
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

        {reflex.phase === 1 && iDone && (
          <div className="rfx-status">⏳ Đã chọn 3 lá — chờ mọi người / hết giờ…</div>
        )}
        {reveal && (
          <div className="rfx-status">3 lá đúng đã sáng · lượt kế ngay…</div>
        )}
      </div>
    </div>
  );
}
