import { useMemo } from 'react';
import type { MatchPlayerPublic, CaroState } from '../api';
import { Avatar } from '../ui/Avatar';
import './caro-break.css';

const SIZE = 10;

// Quân cờ vẽ bằng SVG nét bo tròn — sắc nét, cân giữa ô (thay ký tự ✕/◯ thô).
function Mark({ kind }: { kind: 'x' | 'o' }) {
  if (kind === 'x') {
    return (
      <svg className="car-mark mark-x" viewBox="0 0 24 24" aria-hidden>
        <line x1="6" y1="6" x2="18" y2="18" strokeLinecap="round" />
        <line x1="18" y1="6" x2="6" y2="18" strokeLinecap="round" />
      </svg>
    );
  }
  return (
    <svg className="car-mark mark-o" viewBox="0 0 24 24" aria-hidden>
      <circle cx="12" cy="12" r="7" fill="none" />
    </svg>
  );
}

/**
 * Màn "Giải lao — Caro đồng đội" full-screen overlay.
 * Luật: chia team → chia 2 CẶP ĐẤU 1v1 (X vs O) → chơi TUẦN TỰ 2 ván caro (mỗi cặp 1 bàn 10×10 riêng).
 * Team thắng nhiều cặp hơn → thắng chung cuộc (mỗi người +2; thua -2; hòa 0).
 *  phase 0 = quay chia team / hiện cặp đấu (giữa các ván); phase 1 = đang chơi ván của cặp hiện tại.
 */
export function CaroBreakScreen({
  caro, players, myUserId, isOrganizer, spinLeftSec, revealLeftSec, totalLeftSec, turnLeftSec,
  onSpin, onPlace, onDraw,
}: {
  caro: CaroState;
  players: MatchPlayerPublic[];
  myUserId: string;
  isOrganizer: boolean;
  spinLeftSec: number;
  revealLeftSec: number;     // 5s hiện team/cặp giữa các ván
  totalLeftSec: number;
  turnLeftSec: number;       // 10s đồng hồ lượt hiện tại
  onSpin: () => void;
  onPlace: (cellIndex: number) => void;
  onDraw: () => void;
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

  const playerOf: Record<string, CaroState['players'][number]> = {};
  for (const cp of caro.players) playerOf[cp.userId] = cp;

  const myTeam = playerOf[myUserId]?.team ?? 0;
  const isMyTurn = caro.phase === 1 && caro.turnUserId === myUserId;
  const turnTeam = caro.turnUserId ? (playerOf[caro.turnUserId]?.team ?? 0) : 0;
  const iVotedDraw = playerOf[myUserId]?.drawVote ?? false;
  const inCurrentPair = playerOf[myUserId]?.inCurrentPair ?? false;
  const winSet = useMemo(() => new Set(caro.winLine), [caro.winLine]);
  // 2 đầu mút đường gạch chuỗi thắng (toạ độ theo "ô" 0..10, tâm ô = col+0.5/row+0.5).
  const winLineEnds = useMemo(() => {
    if (!caro.winLine || caro.winLine.length < 2) return null;
    const sorted = [...caro.winLine].sort((a, b) => a - b);
    const a = sorted[0], b = sorted[sorted.length - 1];
    return {
      x1: (a % SIZE) + 0.5, y1: Math.floor(a / SIZE) + 0.5,
      x2: (b % SIZE) + 0.5, y2: Math.floor(b / SIZE) + 0.5,
    };
  }, [caro.winLine]);

  // Đếm số cặp mỗi team đã thắng (từ kết quả các cặp đã xong).
  const xPairWins = caro.pairs.filter(p => p.winner === 1).length;
  const oPairWins = caro.pairs.filter(p => p.winner === 2).length;

  function Stone({ team }: { team: number }) {
    return team === 1 ? <Mark kind="x" /> : <Mark kind="o" />;
  }

  // Bảng 2 cặp đấu + kết quả từng cặp (dùng ở cả 2 pha).
  function Matchups() {
    return (
      <div className="car-matchups">
        {caro.pairs.map((pr, i) => {
          const active = caro.phase === 1 && i === caro.pairIndex;
          const done = pr.winner !== 0 || i < caro.pairIndex;
          return (
            <div key={i} className={`car-matchup ${active ? 'active' : ''} ${done ? 'done' : ''}`}>
              <span className="car-matchup-label">Cặp {i + 1}</span>
              <div className="car-vs">
                <span className={`car-vs-p team1 ${pr.winner === 1 ? 'won' : pr.winner === 2 ? 'lost' : ''}`}>
                  <span className="car-vs-mark"><svg className="car-mark mark-x" viewBox="0 0 24 24"><line x1="6" y1="6" x2="18" y2="18" strokeLinecap="round"/><line x1="18" y1="6" x2="6" y2="18" strokeLinecap="round"/></svg></span>
                  {nameOf[pr.playerX] ?? '?'}
                </span>
                <span className="car-vs-x">vs</span>
                <span className={`car-vs-p team2 ${pr.winner === 2 ? 'won' : pr.winner === 1 ? 'lost' : ''}`}>
                  <span className="car-vs-mark"><svg className="car-mark mark-o" viewBox="0 0 24 24"><circle cx="12" cy="12" r="7" fill="none"/></svg></span>
                  {nameOf[pr.playerO] ?? '?'}
                </span>
              </div>
              <span className="car-matchup-status">
                {i < caro.pairIndex || pr.winner !== 0 || (caro.phase === 1 && i < caro.pairIndex)
                  ? (pr.winner === 1 ? '❌ thắng' : pr.winner === 2 ? '⭕ thắng' : i < caro.pairIndex ? 'Hòa' : '')
                  : active ? '⏳ đang đấu' : 'chờ'}
              </span>
            </div>
          );
        })}
        <div className="car-score">
          Tỉ số cặp: <b className="team1">❌ {xPairWins}</b> — <b className="team2">{oPairWins} ⭕</b>
        </div>
      </div>
    );
  }

  // ---- Pha 0: quay chia team / hiện cặp ----
  if (caro.phase === 0) {
    return (
      <div className="car-overlay">
        <div className="car-card car-card-spin">
          <div className="car-title">⭕ Caro đồng đội</div>
          {caro.spun ? (
            <>
              <div className="car-sub">
                {caro.matchWinnerTeam !== 0
                  ? <b className={caro.matchWinnerTeam === myTeam ? 'car-win' : 'car-lose'}>{caro.matchWinnerTeam === 1 ? '❌ Team X' : '⭕ Team O'} thắng chung cuộc!</b>
                  : <>🎲 Cặp đấu — {caro.pairIndex === 0 ? 'bắt đầu' : `vào cặp ${caro.pairIndex + 1}`} sau <b>{revealLeftSec}s</b></>}
              </div>
              <Matchups />
            </>
          ) : isOrganizer ? (
            <>
              <div className="car-sub">Bấm để quay chia team & cặp đấu ({spinLeftSec}s)</div>
              <button className="car-spin-btn" onClick={onSpin}>🎲 Quay chia team</button>
              <div className="car-roster">
                {[...players].sort((a, b) => a.seatIndex - b.seatIndex).map(p => (
                  <div key={p.userId} className={`car-roster-row ${p.userId === myUserId ? 'me' : ''}`}>
                    <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                    <span className="car-team-name">{nameOf[p.userId] ?? '?'}</span>
                  </div>
                ))}
              </div>
            </>
          ) : (
            <>
              <div className="car-sub">Chờ người tổ chức quay chia team… ({spinLeftSec}s)</div>
              <div className="car-roster">
                {[...players].sort((a, b) => a.seatIndex - b.seatIndex).map(p => (
                  <div key={p.userId} className={`car-roster-row ${p.userId === myUserId ? 'me' : ''}`}>
                    <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
                    <span className="car-team-name">{nameOf[p.userId] ?? '?'}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    );
  }

  // ---- Pha 1: chơi ván của cặp hiện tại ----
  const decided = caro.winnerTeam !== 0; // cặp hiện tại đã xong (hiển thị thoáng)
  const turnUser = caro.turnUserId;
  return (
    <div className="car-overlay">
      <div className="car-card car-card-play">
        <div className="car-title">⭕ Caro đồng đội · Cặp {caro.pairIndex + 1}/{caro.pairCount}</div>

        {/* THÔNG BÁO LƯỢT NỔI BẬT */}
        <div className={`car-turn-banner ${decided ? 'decided' : isMyTurn ? 'mine' : ''} ${turnTeam === 1 ? 'tb-x' : 'tb-o'}`}>
          {decided ? (
            <span>{caro.winnerTeam === 1 ? '❌ Team X' : '⭕ Team O'} thắng cặp này!</span>
          ) : isMyTurn ? (
            <span className="car-turn-big"><span className="car-turn-mark"><Stone team={myTeam} /></span> Tới lượt BẠN — đặt quân!</span>
          ) : (
            <span className="car-turn-big">
              <span className="car-turn-mark"><Stone team={turnTeam} /></span>
              Tới lượt: <b>{turnUser ? nameOf[turnUser] : ''}</b>
            </span>
          )}
          {!decided && <span className={`car-turn-timer ${turnLeftSec <= 3 ? 'low' : ''}`}>⏱ {turnLeftSec}s</span>}
        </div>

        <div className="car-body">
          <div className="car-side">
            <Matchups />
            <div className="car-total">⌛ {totalLeftSec}s</div>
          </div>

          {/* Bàn cờ 10×10 + đường gạch chuỗi thắng overlay */}
          <div className="car-board-wrap">
            <div className="car-board">
              {caro.board.map((v, i) => {
                const filled = v !== 0;
                const isLast = i === caro.lastMove;
                const isWin = winSet.has(i);
                const cls = [
                  'car-cell',
                  v === 1 ? 'x' : v === 2 ? 'o' : 'empty',
                  isLast ? 'last' : '',
                  isWin ? 'win' : '',
                ].join(' ');
                return (
                  <button
                    key={i}
                    className={cls}
                    disabled={!isMyTurn || filled || decided}
                    onClick={() => onPlace(i)}
                  >
                    {v === 1 ? <Mark kind="x" /> : v === 2 ? <Mark kind="o" /> : null}
                  </button>
                );
              })}
            </div>
            {winLineEnds && (
              <svg className="car-winline" viewBox={`0 0 ${SIZE} ${SIZE}`} preserveAspectRatio="none">
                <line x1={winLineEnds.x1} y1={winLineEnds.y1} x2={winLineEnds.x2} y2={winLineEnds.y2} />
              </svg>
            )}
          </div>
        </div>

        {!decided && inCurrentPair && (
          <button className={`car-draw-btn ${iVotedDraw ? 'voted' : ''}`} onClick={onDraw} disabled={iVotedDraw}>
            {iVotedDraw ? '🤝 Đã xin hòa — chờ đối thủ' : '🤝 Xin hòa cặp này'}
          </button>
        )}
      </div>
    </div>
  );
}
