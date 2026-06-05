import { useMemo } from 'react';
import type { MatchPlayerPublic, MemoryGameState } from '../api';
import { Avatar } from '../ui/Avatar';
import { CLUB_LOGO, clubName } from '../game/clubLogos';
import './memory-break.css';

/**
 * Màn "Giải lao — Trí nhớ" full-screen overlay. 3 pha (memory.phase):
 *  0 = xem lưới: hiện 3×3 logo CLB + đếm ngược 10s ghi nhớ.
 *  1 = trả lời: ẩn lưới, hỏi "Ô số X là đội nào?" + 4 logo đáp án, 20s/câu.
 *  2 = hiện đáp án: tô xanh logo đúng + ai đúng + thời gian; bảng điểm tích lũy.
 */
export function MemoryBreakScreen({
  memory, players, myUserId, viewLeftSec, answerLeftSec, myChoiceIdx, onAnswer,
}: {
  memory: MemoryGameState;
  players: MatchPlayerPublic[];
  myUserId: string;
  viewLeftSec: number;
  answerLeftSec: number;
  myChoiceIdx: number | null;
  onAnswer: (optionIndex: number) => void;
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
  const answered = new Set(memory.answeredUserIds);
  const resultOf: Record<string, MemoryGameState['results'][number]> = {};
  for (const r of memory.results) resultOf[r.userId] = r;

  function Logo({ slug, size = 'md' }: { slug: string; size?: 'sm' | 'md' | 'lg' }) {
    const url = CLUB_LOGO[slug];
    return url
      ? <img className={`mem-logo mem-logo-${size}`} src={url} alt={clubName(slug)} title={clubName(slug)} />
      : <span className="mem-logo-fallback">{clubName(slug)}</span>;
  }

  // ---- Pha 0: xem lưới ----
  if (memory.phase === 0) {
    const grid = memory.grid ?? [];
    return (
      <div className="mem-overlay">
        <div className="mem-card">
          <div className="mem-title">🧠 Trí nhớ — Ghi nhớ vị trí logo</div>
          <div className="mem-sub">Nhớ kỹ logo ở từng ô! Sắp có câu hỏi… <b className={viewLeftSec <= 3 ? 'low' : ''}>{viewLeftSec}s</b></div>
          <div className="mem-grid">
            {grid.map((slug, i) => (
              <div key={i} className="mem-cell filled">
                <span className="mem-cell-no">{i + 1}</span>
                <Logo slug={slug} />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  // ---- Pha 1 + 2: câu hỏi ----
  const reveal = memory.phase === 2;
  const q = memory.question;
  const correctIdx = reveal ? (q?.correctIndex ?? -1) : -1;
  const iAnswered = answered.has(myUserId) || myChoiceIdx != null;

  return (
    <div className="mem-overlay">
      <div className="mem-card">
        <div className="mem-title">🧠 Trí nhớ</div>
        <div className="mem-sub">
          Câu {memory.currentQuestion + 1}/{memory.totalQuestions}
          {!reveal && <> · <b className={answerLeftSec <= 3 ? 'low' : ''}>{answerLeftSec}s</b></>}
        </div>

        {/* Lưới mini đánh dấu ô đang hỏi */}
        <div className="mem-grid mem-grid-mini">
          {Array.from({ length: 9 }).map((_, i) => (
            <div key={i} className={`mem-cell ${q && i === q.cellIndex ? 'asked' : 'dim'}`}>
              <span className="mem-cell-no">{i + 1}</span>
              {q && i === q.cellIndex && <span className="mem-q-mark">?</span>}
            </div>
          ))}
        </div>
        <div className="mem-question">Ô số <b>{(q?.cellIndex ?? 0) + 1}</b> là logo đội nào?</div>

        {/* 4 logo đáp án */}
        <div className="mem-options">
          {(q?.options ?? []).map((slug, i) => {
            const isCorrect = reveal && i === correctIdx;
            const isMine = myChoiceIdx === i;
            const isWrongMine = reveal && isMine && i !== correctIdx;
            return (
              <button
                key={i}
                className={`mem-opt-btn ${isCorrect ? 'correct' : ''} ${isWrongMine ? 'wrong' : ''} ${isMine ? 'mine' : ''}`}
                disabled={reveal || iAnswered}
                onClick={() => onAnswer(i)}
                title={clubName(slug)}
              >
                <Logo slug={slug} size="lg" />
                {isMine && <span className="mem-opt-tag">bạn</span>}
              </button>
            );
          })}
        </div>

        {/* Trạng thái / kết quả người chơi */}
        <div className="mem-players">
          {seats.map(p => {
            const r = resultOf[p.userId];
            const hasAnswered = answered.has(p.userId);
            return (
              <div key={p.userId} className={`mem-player ${p.userId === myUserId ? 'me' : ''}`}>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="mem-player-name">{nameOf[p.userId] ?? '?'}</div>
                {reveal ? (
                  <div className={`mem-player-result ${r?.correct ? 'ok' : 'no'}`}>
                    {r?.correct ? `✅ ${(r.elapsedMs / 1000).toFixed(1)}s` : (r?.answered ? '❌' : '⏰')}
                  </div>
                ) : (
                  <div className={`mem-player-status ${hasAnswered ? 'ans' : 'wait'}`}>
                    {hasAnswered ? '✔ đã trả lời' : '…'}
                  </div>
                )}
                <div className="mem-player-score" title="Số câu đúng">🎯 {r?.correctCount ?? 0}</div>
              </div>
            );
          })}
        </div>

        {!reveal && iAnswered && (
          <div className="mem-status">⏳ Đã chọn — chờ mọi người / hết giờ…</div>
        )}
        {reveal && memory.answerSlug && (
          <div className="mem-status">Đáp án: <b>{clubName(memory.answerSlug)}</b> · câu kế ngay…</div>
        )}
      </div>
    </div>
  );
}
