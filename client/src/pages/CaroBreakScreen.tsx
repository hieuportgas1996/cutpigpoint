import { useMemo } from 'react';
import type { MatchPlayerPublic, CaroState } from '../api';
import { Avatar } from '../ui/Avatar';
import './caro-break.css';

const SIZE = 10;

/**
 * Màn "Giải lao — Caro đồng đội" full-screen overlay. 2 pha (caro.phase):
 *  0 = quay chia team: người tổ chức bấm "Quay" → hiện 2 team (X/O) + thứ tự đi.
 *  1 = chơi: theo lượt X→O→X→O. Người tới lượt click 1 ô trống → đặt quân team mình.
 *      Đủ 5 liên tiếp (ngang/dọc/chéo) → team đó thắng. Hết 10s → bỏ lượt.
 * Có thể bấm "Xin hòa"; cả 2 team đồng ý → hòa. Đồng bộ qua server.
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
  revealLeftSec: number;     // 5s hiện team + thứ tự sau khi quay
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
  const turnName = caro.turnUserId ? (caro.turnUserId === myUserId ? 'Bạn' : nameOf[caro.turnUserId] ?? '') : '';
  const turnTeam = caro.turnUserId ? (playerOf[caro.turnUserId]?.team ?? 0) : 0;
  const iVotedDraw = playerOf[myUserId]?.drawVote ?? false;
  const winSet = useMemo(() => new Set(caro.winLine), [caro.winLine]);

  // Roster mỗi team theo thứ tự lượt.
  const teamX = [...caro.players].filter(p => p.team === 1).sort((a, b) => a.turnOrder - b.turnOrder);
  const teamO = [...caro.players].filter(p => p.team === 2).sort((a, b) => a.turnOrder - b.turnOrder);

  function TeamCard({ team, list }: { team: number; list: CaroState['players'] }) {
    const isTurnTeam = caro.phase === 1 && turnTeam === team;
    return (
      <div className={`car-team team${team} ${isTurnTeam ? 'active' : ''} ${myTeam === team ? 'me' : ''}`}>
        <div className="car-team-head">{team === 1 ? '❌ Team X' : '⭕ Team O'}{myTeam === team ? ' (Bạn)' : ''}</div>
        {list.map(p => (
          <div key={p.userId} className={`car-team-row ${caro.turnUserId === p.userId ? 'turn' : ''}`}>
            <Avatar name={nameOf[p.userId] ?? '?'} hasAvatar={avatarOf[p.userId]} playerId={p.userId} size="sm" />
            <span className="car-team-name">{nameOf[p.userId] ?? '?'}</span>
            {p.drawVote && <span className="car-draw-tag">🤝</span>}
          </div>
        ))}
      </div>
    );
  }

  // ---- Pha 0: quay chia team ----
  if (caro.phase === 0) {
    return (
      <div className="car-overlay">
        <div className="car-card car-card-spin">
          <div className="car-title">⭕ Caro đồng đội</div>
          {caro.spun ? (
            <>
              <div className="car-sub">🎲 Chia team & thứ tự đi — vào game sau <b>{revealLeftSec}s</b></div>
              <div className="car-teams">
                <TeamCard team={1} list={teamX} />
                <TeamCard team={2} list={teamO} />
              </div>
              <div className="car-order-note">Thứ tự đi: ❌ → ⭕ → ❌ → ⭕ (team X đi trước)</div>
            </>
          ) : isOrganizer ? (
            <>
              <div className="car-sub">Bấm để quay chia team ({spinLeftSec}s)</div>
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

  // ---- Pha 1: chơi ----
  const decided = caro.winnerTeam !== 0; // có team thắng (hiển thị thoáng trước khi modal)
  return (
    <div className="car-overlay">
      <div className="car-card car-card-play">
        <div className="car-title">⭕ Caro đồng đội</div>
        <div className="car-sub">
          {decided ? (
            <b className={caro.winnerTeam === myTeam ? 'car-win' : 'car-lose'}>
              {caro.winnerTeam === 1 ? '❌ Team X' : '⭕ Team O'} thắng!
            </b>
          ) : isMyTurn ? (
            <b className="car-myturn">Lượt của bạn — đặt {myTeam === 1 ? '❌' : '⭕'}!</b>
          ) : (
            <>Lượt: <b>{turnName}</b> {turnTeam === 1 ? '❌' : '⭕'}</>
          )}
          {!decided && <> · ⏱ <b className={turnLeftSec <= 3 ? 'low' : ''}>{turnLeftSec}s</b></>}
          {' · '}<span className="car-total">⌛ {totalLeftSec}s</span>
        </div>

        <div className="car-body">
          <TeamCard team={1} list={teamX} />

          {/* Bàn cờ 10×10 */}
          <div className="car-board" style={{ gridTemplateColumns: `repeat(${SIZE}, 1fr)` }}>
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
                  {v === 1 ? '✕' : v === 2 ? '◯' : ''}
                </button>
              );
            })}
          </div>

          <TeamCard team={2} list={teamO} />
        </div>

        {!decided && myTeam !== 0 && (
          <button className={`car-draw-btn ${iVotedDraw ? 'voted' : ''}`} onClick={onDraw} disabled={iVotedDraw}>
            {iVotedDraw ? '🤝 Đã xin hòa — chờ team kia' : '🤝 Xin hòa'}
          </button>
        )}
      </div>
    </div>
  );
}
