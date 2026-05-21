import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { CardSvg } from '../game/CardSvg';
import { MaiBranch } from '../game/effects/MaiBranch';
import { Confetti } from '../game/effects/Confetti';
import { Card, cardFromDto, cardToDto, compareCard, detectCombo, comboBeats, isFourPairRun } from '../game/cards';
import { MatchStatus } from '../api';
import '../game/demo.css';
import './room-lobby.css';
import './room-play.css';

const SEAT_POSITIONS: Array<'bottom' | 'right' | 'top' | 'left'> = ['bottom', 'right', 'top', 'left'];
const RANK_LABEL: Record<number, string> = { 1: 'Nhất', 2: 'Nhì', 3: 'Ba', 4: 'Tư' };

export default function RoomPlayPage() {
  const { id: code } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const {
    status, state: room, matchState, privateHand, roundEnd, matchEnd, error,
    playCards, passTurn, startNextRound, endMatch, clearRoundEnd,
  } = useRoomConnection(code);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    setSelected(new Set());
  }, [matchState?.currentTurnSeatIndex, matchState?.roundNumber]);

  const myUserId = state.status === 'authenticated' ? state.userId : '';
  const me = matchState?.players.find(p => p.userId === myUserId) ?? null;
  const isHost = matchState?.hostUserId === myUserId;
  const isMyTurn = matchState?.players[matchState.currentTurnSeatIndex]?.userId === myUserId
    && matchState?.status === MatchStatus.InProgress;
  const myHand: Card[] = (privateHand?.hand ?? []).map(cardFromDto).sort(compareCard);
  const trick: Card[] = (matchState?.currentTrick ?? []).map(cardFromDto);
  const trickCombo = trick.length > 0 ? detectCombo(trick) : null;

  const selectedCards = myHand.filter(c => selected.has(c.id));
  const selectedKey = selectedCards.map(c => c.id).join(',');
  const myCombo = useMemo(() => detectCombo(selectedCards), [selectedKey]);

  const myPassedThisTrick = me?.passedThisTrick ?? false;
  const myComboIsFourPair = myCombo !== null && isFourPairRun(myCombo);

  // canPlay logic:
  // - Must be my turn AND match in progress
  // - Combo must be valid
  // - If passed this trick: only 4-pair-run allowed (exception)
  // - If trick exists: must beat it (or 4-pair-run beats everything)
  const canPlay =
    isMyTurn &&
    myCombo !== null &&
    (!myPassedThisTrick || myComboIsFourPair) &&
    (trickCombo === null || comboBeats(trickCombo, myCombo));

  const canPass = isMyTurn && trickCombo !== null;

  if (state.status !== 'authenticated') return null;

  if (error) {
    return (
      <div className="card">
        <div style={{ color: 'var(--danger)' }}>{error}</div>
        <button className="ghost sm" onClick={() => navigate('/rooms')}>← Về danh sách phòng</button>
      </div>
    );
  }

  if (status !== 'connected' || !room || !matchState) {
    return (
      <div className="card">
        <div className="muted">Đang kết nối ván {code}…</div>
      </div>
    );
  }

  const turnLeftSec = Math.max(0, Math.ceil((new Date(matchState.turnDeadline).getTime() - now) / 1000));

  const myIdx = me ? me.seatIndex : 0;
  const seatLayout = matchState.players.map(p => ({
    player: p,
    position: SEAT_POSITIONS[(p.seatIndex - myIdx + matchState.players.length) % matchState.players.length],
  }));

  const toggle = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  async function handlePlay() {
    if (!myCombo) return;
    try {
      await playCards(myCombo.cards.map(cardToDto));
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handlePass() {
    try {
      await passTurn();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handleNextRound() {
    try {
      await startNextRound();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handleEndMatch() {
    if (!confirm('Kết thúc trận? Phòng sẽ đóng.')) return;
    try {
      await endMatch();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  const tooltipMsg = !isMyTurn
    ? 'Chưa đến lượt bạn'
    : myCombo === null
    ? 'Chọn bộ bài hợp lệ'
    : myPassedThisTrick && !myComboIsFourPair
    ? 'Đã bỏ lượt trick này (chỉ 4 đôi thông được đánh)'
    : !canPlay
    ? 'Bộ này không chặn được nước trước'
    : '';

  return (
    <div className="tlmn-root room-play">
      <div className="tlmn-stage">
        <div className="play-header">
          <button className="tlmn-btn ghost" onClick={() => navigate('/rooms')}>← Thoát</button>
          <div className="lobby-code">
            <span className="muted small">Ván {matchState.roundNumber} · Mã</span>
            <code>{code}</code>
          </div>
          <div className={`turn-timer ${turnLeftSec <= 5 ? 'low' : ''}`}>
            ⏱ {turnLeftSec}s
          </div>
        </div>

        <div className="tlmn-table">
          <MaiBranch corner="tl" />
          <MaiBranch corner="tr" />
          <MaiBranch corner="bl" />
          <MaiBranch corner="br" />

          {seatLayout.map(({ player, position }) => {
            const isTurn = matchState.players[matchState.currentTurnSeatIndex]?.userId === player.userId
              && matchState.status === MatchStatus.InProgress;
            const isMe = player.userId === myUserId;
            return (
              <div key={player.userId} className={`tlmn-seat tlmn-seat-${position} ${isTurn ? 'is-turn' : ''}`}>
                <div className="tlmn-avatar">{player.displayName.charAt(0).toUpperCase()}</div>
                <div className="tlmn-seat-info">
                  <div className="tlmn-seat-name">
                    {isMe ? 'Bạn' : player.displayName}
                    {player.userId === matchState.hostUserId && <span className="host-badge">CHỦ</span>}
                  </div>
                  <div className="tlmn-seat-meta">
                    <span>🂠 {player.cardsLeft}</span>
                    <span className={`score-pill ${player.totalScore > 0 ? 'pos' : player.totalScore < 0 ? 'neg' : ''}`}>
                      {player.totalScore > 0 ? `+${player.totalScore}` : player.totalScore}
                    </span>
                    {player.finalRank && (
                      <span className="rank-tag-mini">{RANK_LABEL[player.finalRank] || `#${player.finalRank}`}</span>
                    )}
                  </div>
                </div>
                {player.passedThisTrick && !player.finalRank && (
                  <div className="tlmn-seat-pass">BỎ LƯỢT</div>
                )}
              </div>
            );
          })}

          <div className="play-area-cards">
            {trick.length === 0 ? (
              <div className="play-empty muted">Mở nước mới</div>
            ) : (
              trick.map((c, i) => (
                <div key={c.id} className="play-card-slot" style={{ marginLeft: i === 0 ? 0 : -22 }}>
                  <CardSvg card={c} size="md" />
                </div>
              ))
            )}
          </div>
        </div>

        <div className="my-hand-area">
          {myHand.length === 0 ? (
            <div className="muted">Bạn đã hết bài 🎉</div>
          ) : (
            <div className="my-hand-fan">
              {myHand.map((c, idx) => {
                const offset = (idx - (myHand.length - 1) / 2) * Math.min(38, 480 / Math.max(myHand.length, 1));
                const isSelected = selected.has(c.id);
                return (
                  <div
                    key={c.id}
                    className="my-hand-slot"
                    style={{ transform: `translateX(${offset}px) translateY(${isSelected ? -16 : 0}px)` }}
                  >
                    <CardSvg
                      card={c}
                      size="md"
                      selected={isSelected}
                      onClick={() => toggle(c.id)}
                    />
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="tlmn-controls">
          <button
            className="tlmn-btn primary"
            disabled={!canPlay}
            onClick={handlePlay}
            title={tooltipMsg}
          >
            ▶ Đánh ({selectedCards.length})
          </button>
          <button
            className="tlmn-btn ghost"
            disabled={!canPass}
            onClick={handlePass}
            title={!isMyTurn ? 'Chưa đến lượt' : trickCombo === null ? 'Không thể bỏ qua khi đang mở nước' : ''}
          >
            ↷ Bỏ qua
          </button>
          {selectedCards.length > 0 && (
            <button className="tlmn-btn ghost" onClick={() => setSelected(new Set())}>Bỏ chọn</button>
          )}
        </div>

        {roundEnd && !matchEnd && (
          <div className="match-end-overlay">
            {roundEnd.wasWhiteWin && <Confetti active={true} />}
            <div className="match-end-card">
              <h2>
                {roundEnd.wasWhiteWin ? '🌟 Có người về trắng!' : `🎉 Kết quả ván ${roundEnd.roundNumber}`}
              </h2>
              <div className="match-end-list">
                {roundEnd.results.map(r => (
                  <div key={r.userId} className="match-end-row">
                    <span className="rank-tag">
                      {r.whiteWinReason ? '★' : RANK_LABEL[r.finalRank] ?? `#${r.finalRank}`}
                    </span>
                    <div className="match-end-name">
                      <div>{r.displayName}</div>
                      {r.whiteWinReason && <div className="white-win-reason">{r.whiteWinReason}</div>}
                    </div>
                    <span className={`score-pill ${r.roundScore > 0 ? 'pos' : r.roundScore < 0 ? 'neg' : ''}`}>
                      {r.roundScore > 0 ? `+${r.roundScore}` : r.roundScore}
                    </span>
                    <span className="total-score">Tổng: {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}</span>
                  </div>
                ))}
              </div>
              <div className="match-end-actions">
                {isHost ? (
                  <>
                    <button className="tlmn-btn primary" onClick={handleNextRound}>🎴 Ván tiếp</button>
                    <button className="tlmn-btn ghost" onClick={handleEndMatch}>Kết thúc trận</button>
                  </>
                ) : (
                  <div className="muted">Đang chờ chủ phòng…</div>
                )}
                <button className="tlmn-btn ghost" onClick={clearRoundEnd}>Đóng bảng</button>
              </div>
            </div>
          </div>
        )}

        {matchEnd && (
          <div className="match-end-overlay">
            <Confetti active={true} />
            <div className="match-end-card">
              <h2>🏆 Kết thúc trận</h2>
              <div className="match-end-list">
                {matchEnd.finalScores.map((r, idx) => (
                  <div key={r.userId} className="match-end-row">
                    <span className="rank-tag">#{idx + 1}</span>
                    <span className="match-end-name">{r.displayName}</span>
                    <span className={`score-pill ${r.totalScore > 0 ? 'pos' : r.totalScore < 0 ? 'neg' : ''}`}>
                      {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}
                    </span>
                  </div>
                ))}
              </div>
              <button className="tlmn-btn primary" onClick={() => navigate('/rooms')}>Về danh sách phòng</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
