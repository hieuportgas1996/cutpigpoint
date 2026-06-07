import { useMemo } from 'react';
import type { MatchPlayerPublic, MathQuizState, MathToken } from '../api';
import { Avatar } from '../ui/Avatar';
import { CardSvg } from '../game/CardSvg';
import { cardFromDto } from '../game/cards';
import './math-break.css';

const DIGITS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

// Render biểu thức: số 1-9 hiện thành LÁ BÀI nhỏ, toán tử/ngoặc/"0" hiện text.
function ExprTokens({ tokens, fallback }: { tokens: MathToken[] | null; fallback: string }) {
  if (!tokens || tokens.length === 0) return <>{fallback}</>;
  return (
    <span className="math-expr-tokens">
      {tokens.map((t, i) =>
        t.isCard ? (
          <CardSvg key={i} card={cardFromDto({ rank: t.rank, suit: t.suit })} size="sm" />
        ) : (
          <span key={i} className={`math-expr-text ${t.text === '(' || t.text === ')' ? 'paren' : ''}`}>{t.text}</span>
        )
      )}
    </span>
  );
}

/**
 * Màn "Giải lao — Tính toán" full-screen overlay. 3 pha (math.phase):
 *  0 = chọn số: mỗi người bấm 1 chữ số 0-9 (nhìn realtime), 10s.
 *  1 = trả lời: hiện phép tính + 4 đáp án trắc nghiệm, 5s; bấm chọn (không lộ chọn của người khác).
 *  2 = hiện đáp án: tô xanh đáp án đúng, hiện ai đúng + thời gian; bảng điểm tích lũy.
 * Đồng bộ mọi client qua server deadline (mathPickDeadline / mathAnswerDeadline / mathRevealUntil).
 */
export function MathBreakScreen({
  math, players, myUserId, pickLeftSec, answerLeftSec, myPick, myChoiceIdx, onPickNumber, onAnswer,
}: {
  math: MathQuizState;
  players: MatchPlayerPublic[];
  myUserId: string;
  pickLeftSec: number;
  answerLeftSec: number;
  myPick: number | null;          // số mình đã chọn (pha 0) — client nhớ để khoá nút
  myChoiceIdx: number | null;     // đáp án mình đã chọn câu hiện tại (client nhớ; server ẩn lúc trả lời)
  onPickNumber: (n: number) => void;
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
  const pickOf: Record<string, number> = {};
  for (const p of math.picks) pickOf[p.userId] = p.number;
  const answered = new Set(math.answeredUserIds);
  const resultOf: Record<string, MathQuizState['results'][number]> = {};
  for (const r of math.results) resultOf[r.userId] = r;

  const iAlreadyPicked = myPick != null || pickOf[myUserId] != null;

  // ---- Pha 0: chọn số ----
  if (math.phase === 0) {
    return (
      <div className="math-overlay">
        <div className="math-card">
          <div className="math-title">🧮 Tính toán — Chọn số</div>
          <div className="math-sub">Mỗi người chọn 1 chữ số (0-9) · 4 số sẽ ghép thành phép tính · <b>{pickLeftSec}s</b></div>

          <div className="math-picks">
            {seats.map(p => {
              const picked = pickOf[p.userId];
              return (
                <div key={p.userId} className={`math-pick-seat ${picked != null ? 'done' : 'waiting'} ${p.userId === myUserId ? 'me' : ''}`}>
                  <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                  <div className="math-pick-name">{nameOf[p.userId] ?? '?'}</div>
                  <div className="math-pick-num">{picked != null ? picked : '?'}</div>
                </div>
              );
            })}
          </div>

          {iAlreadyPicked ? (
            <div className="math-status">✅ Đã chọn <b>{myPick ?? pickOf[myUserId]}</b> — chờ mọi người… <b>{pickLeftSec}s</b></div>
          ) : (
            <div className="math-digit-grid">
              {DIGITS.map(d => (
                <button key={d} className="math-digit-btn" onClick={() => onPickNumber(d)}>{d}</button>
              ))}
            </div>
          )}
        </div>
      </div>
    );
  }

  // ---- Pha 1 + 2: câu hỏi ----
  const reveal = math.phase === 2;
  const q = math.question;
  const correctIdx = reveal ? (q?.correctIndex ?? -1) : -1;
  const iAnswered = answered.has(myUserId) || myChoiceIdx != null;

  return (
    <div className="math-overlay">
      <div className="math-card">
        <div className="math-title">🧮 Tính toán</div>
        <div className="math-sub">
          Câu {math.currentQuestion + 1}/{math.totalQuestions}
          {!reveal && <> · <b className={answerLeftSec <= 2 ? 'low' : ''}>{answerLeftSec}s</b></>}
        </div>

        {/* Phép tính — số 1-9 hiện thành lá bài */}
        <div className="math-expr">
          <ExprTokens tokens={q?.exprTokens ?? null} fallback={q?.expression ?? ''} />
          <span className="math-eq">= ?</span>
        </div>

        {/* 4 đáp án */}
        <div className="math-options">
          {(q?.options ?? []).map((opt, i) => {
            const isCorrect = reveal && i === correctIdx;
            const isMine = myChoiceIdx === i;
            const isWrongMine = reveal && isMine && i !== correctIdx;
            return (
              <button
                key={i}
                className={`math-opt-btn ${isCorrect ? 'correct' : ''} ${isWrongMine ? 'wrong' : ''} ${isMine ? 'mine' : ''}`}
                disabled={reveal || iAnswered}
                onClick={() => onAnswer(i)}
              >
                {opt}
                {isMine && <span className="math-opt-tag">bạn</span>}
              </button>
            );
          })}
        </div>

        {/* Trạng thái / kết quả người chơi */}
        <div className="math-players">
          {seats.map(p => {
            const r = resultOf[p.userId];
            const hasAnswered = answered.has(p.userId);
            return (
              <div key={p.userId} className={`math-player ${p.userId === myUserId ? 'me' : ''}`}>
                <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                <div className="math-player-name">{nameOf[p.userId] ?? '?'}</div>
                {reveal ? (
                  <div className={`math-player-result ${r?.correct ? 'ok' : 'no'}`}>
                    {r?.correct ? `✅ ${(r.elapsedMs / 1000).toFixed(1)}s` : (r?.answered ? '❌' : '⏰')}
                  </div>
                ) : (
                  <div className={`math-player-status ${hasAnswered ? 'ans' : 'wait'}`}>
                    {hasAnswered ? '✔ đã trả lời' : '…'}
                  </div>
                )}
                <div className="math-player-score" title="Số câu đúng">🎯 {r?.correctCount ?? 0}</div>
              </div>
            );
          })}
        </div>

        {!reveal && iAnswered && (
          <div className="math-status">⏳ Đã chọn — chờ mọi người / hết giờ…</div>
        )}
        {reveal && (
          <div className="math-status">Đáp án đúng: <b>{q?.options[correctIdx]}</b> · câu kế ngay…</div>
        )}
      </div>
    </div>
  );
}
