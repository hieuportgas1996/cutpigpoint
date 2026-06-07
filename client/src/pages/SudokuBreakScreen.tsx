import { useEffect, useMemo, useState } from 'react';
import type { MatchPlayerPublic, SudokuGameState } from '../api';
import { Avatar } from '../ui/Avatar';
import { CardSvg } from '../game/CardSvg';
import { cardFromDto } from '../game/cards';
import './sudoku-break.css';

// Giá trị Sudoku 1-4 → rank lá bài: 1→A(14), 2→"2"(15), 3→3, 4→4 (giống quy ước Tính toán).
const valueToRank = (v: number) => (v === 1 ? 14 : v === 2 ? 15 : v);
const cardOf = (v: number, suit: number) => cardFromDto({ rank: valueToRank(v), suit });

/**
 * Màn "Giải lao — Trí tuệ" (Sudoku 4×4) full-screen overlay. CHUNG 1 đề cho cả 4 người, 60s.
 * Client tự giữ bài điền của MÌNH (không gửi lộ qua server). Chọn ô trống → bấm 1 trong 4 lá để điền.
 * Điền đủ + đúng → server tự chốt (solved). Public chỉ hiện tiến độ + ai đã xong + thời gian.
 */
export function SudokuBreakScreen({
  sudoku, players, myUserId, leftSec, onFill,
}: {
  sudoku: SudokuGameState;
  players: MatchPlayerPublic[];
  myUserId: string;
  leftSec: number;
  onFill: (cellIndex: number, value: number) => void;
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
  const progressOf: Record<string, SudokuGameState['progress'][number]> = {};
  for (const pr of sudoku.progress) progressOf[pr.userId] = pr;
  const meSolved = progressOf[myUserId]?.solved ?? false;

  // Bài điền của MÌNH: khởi tạo từ ô cho sẵn (given != 0). Ô trống = 0 cho tới khi tự điền.
  const [fills, setFills] = useState<number[]>(() => sudoku.given.slice());
  const [selected, setSelected] = useState<number | null>(null);

  // Đồng bộ ô cho sẵn khi đổi đề (round mới) — chỉ reset khi given thay đổi (so theo chuỗi).
  const givenKey = sudoku.given.join(',');
  useEffect(() => {
    setFills(sudoku.given.slice());
    setSelected(null);
  }, [givenKey]);

  // Chất random CỐ ĐỊNH cho mỗi giá trị 1-4 (bàn phím) + mỗi ô lưới — tránh nhấp nháy khi re-render.
  const padSuits = useMemo(() => [1, 2, 3, 4].map(() => Math.floor(Math.random() * 4)), [givenKey]);
  const cellSuits = useMemo(() => sudoku.given.map(() => Math.floor(Math.random() * 4)), [givenKey]);
  const numberCards = useMemo(
    () => [1, 2, 3, 4].map((v, i) => ({ v, card: cardOf(v, padSuits[i]) })),
    [givenKey, padSuits]
  );

  const isGiven = (i: number) => sudoku.given[i] !== 0;

  function pickCell(i: number) {
    if (meSolved || isGiven(i)) return;
    setSelected(prev => (prev === i ? null : i));
  }

  function fillValue(v: number) {
    if (meSolved || selected == null || isGiven(selected)) return;
    // Bấm lại số đang có → xoá ô (v=0). Khác → điền v.
    const next = fills[selected] === v ? 0 : v;
    const copy = fills.slice();
    copy[selected] = next;
    setFills(copy);
    onFill(selected, next);
  }

  return (
    <div className="sdk-overlay">
      <div className="sdk-card">
        <div className="sdk-title">🧩 Trí tuệ — Sudoku 4×4</div>
        <div className="sdk-sub">
          Mỗi hàng · cột · ô 2×2 đủ <b>1-2-3-4</b>
          {!meSolved && <> · <b className={leftSec <= 5 ? 'low' : ''}>{leftSec}s</b></>}
        </div>

        {meSolved ? (
          <div className="sdk-done">✅ Bạn đã giải xong! Chờ mọi người / hết giờ…</div>
        ) : (
          <>
            {/* Lưới 4×4 */}
            <div className="sdk-grid">
              {fills.map((v, i) => {
                const given = isGiven(i);
                const sel = selected === i;
                // viền ô 2×2: dày ở ranh giới box.
                const r = Math.floor(i / 4), c = i % 4;
                const cls = [
                  'sdk-cell',
                  given ? 'given' : 'blank',
                  sel ? 'sel' : '',
                  c === 2 ? 'box-left' : '',
                  r === 2 ? 'box-top' : '',
                ].join(' ');
                return (
                  <button key={i} className={cls} onClick={() => pickCell(i)} disabled={given}>
                    {v !== 0 ? <CardSvg card={cardOf(v, cellSuits[i])} size="sm" /> : <span className="sdk-empty" />}
                  </button>
                );
              })}
            </div>

            {/* Bàn phím lá bài 1-4 */}
            <div className="sdk-pad">
              {numberCards.map(({ v, card }) => (
                <button
                  key={v}
                  className={`sdk-pad-btn ${selected != null && fills[selected] === v ? 'active' : ''}`}
                  disabled={selected == null}
                  onClick={() => fillValue(v)}
                  title={`Điền ${v}`}
                >
                  <CardSvg card={card} size="sm" />
                </button>
              ))}
            </div>
            <div className="sdk-hint">
              {selected == null ? 'Chọn 1 ô trống rồi bấm lá bài để điền' : 'Bấm lá bài để điền · bấm lại để xoá'}
            </div>
          </>
        )}

        {/* Tiến độ người chơi */}
        <div className="sdk-players">
          {seats.map(p => {
            const pr = progressOf[p.userId];
            return (
              <div key={p.userId} className={`sdk-player ${p.userId === myUserId ? 'me' : ''}`}>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="sdk-player-name">{nameOf[p.userId] ?? '?'}</div>
                {pr?.solved ? (
                  <div className="sdk-player-result ok">✅ {(pr.elapsedMs / 1000).toFixed(1)}s</div>
                ) : (
                  <div className="sdk-player-status">📝 {pr?.filled ?? 0}/16</div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
