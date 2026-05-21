import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { CardSvg } from '../game/CardSvg';
import { MaiBranch } from '../game/effects/MaiBranch';
import { Confetti } from '../game/effects/Confetti';
import { Card, cardFromDto, cardToDto, compareCard, detectCombo, comboBeats } from '../game/cards';
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
    status, state: room, matchState, privateHand, matchEnd, error,
    playCards, passTurn,
  } = useRoomConnection(code);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [now, setNow] = useState(Date.now());

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  // Reset selection on turn change
  useEffect(() => {
    setSelected(new Set());
  }, [matchState?.currentTurnSeatIndex]);

  // Derive everything BEFORE early returns (hooks must be called in same order every render)
  const myUserId = state.status === 'authenticated' ? state.userId : '';
  const me = matchState?.players.find(p => p.userId === myUserId) ?? null;
  const isMyTurn = matchState?.players[matchState.currentTurnSeatIndex]?.userId === myUserId;
  const myHand: Card[] = (privateHand?.hand ?? []).map(cardFromDto).sort(compareCard);
  const trick: Card[] = (matchState?.currentTrick ?? []).map(cardFromDto);
  const trickCombo = trick.length > 0 ? detectCombo(trick) : null;

  const selectedCards = myHand.filter(c => selected.has(c.id));
  const selectedKey = selectedCards.map(c => c.id).join(',');
  const myCombo = useMemo(() => detectCombo(selectedCards), [selectedKey]);

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

  const canPlay = isMyTurn && myCombo !== null && (
    trickCombo === null || comboBeats(trickCombo, myCombo)
  );
  const canPass = isMyTurn && trickCombo !== null;

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

  return (
    <div className="tlmn-root room-play">
      <div className="tlmn-stage">
        <div className="play-header">
          <button className="tlmn-btn ghost" onClick={() => navigate('/rooms')}>← Thoát</button>
          <div className="lobby-code">
            <span className="muted small">Mã phòng</span>
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
            const isTurn = matchState.players[matchState.currentTurnSeatIndex]?.userId === player.userId;
            const isMe = player.userId === myUserId;
            return (
              <div key={player.userId} className={`tlmn-seat tlmn-seat-${position} ${isTurn ? 'is-turn' : ''}`}>
                <div className="tlmn-avatar">{player.displayName.charAt(0).toUpperCase()}</div>
                <div className="tlmn-seat-info">
                  <div className="tlmn-seat-name">{isMe ? 'Bạn' : player.displayName}</div>
                  <div className="tlmn-seat-meta">
                    <span>🂠 {player.cardsLeft}</span>
                    {player.finalRank && (
                      <span className="score-pill pos">{RANK_LABEL[player.finalRank] || `#${player.finalRank}`}</span>
                    )}
                  </div>
                </div>
              </div>
            );
          })}

          <div className="play-area-cards">
            {trick.length === 0 ? (
              <div className="play-empty muted">
                {trickCombo === null && matchState.currentTrickOwnerId === null
                  ? 'Mở nước mới'
                  : 'Chưa có bài'}
              </div>
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
            title={!isMyTurn ? 'Chưa đến lượt bạn' : myCombo === null ? 'Chọn bộ bài hợp lệ' : !canPlay ? 'Bộ này không chặn được nước trước' : ''}
          >
            ▶ Đánh ({selectedCards.length})
          </button>
          <button
            className="tlmn-btn ghost"
            disabled={!canPass}
            onClick={handlePass}
            title={!isMyTurn ? 'Chưa đến lượt bạn' : trickCombo === null ? 'Không thể bỏ qua khi đang mở nước' : ''}
          >
            ↷ Bỏ qua
          </button>
          {selectedCards.length > 0 && (
            <button className="tlmn-btn ghost" onClick={() => setSelected(new Set())}>Bỏ chọn</button>
          )}
        </div>

        {matchEnd && (
          <div className="match-end-overlay">
            <Confetti active={true} />
            <div className="match-end-card">
              <h2>🎉 Kết quả ván</h2>
              <div className="match-end-list">
                {matchEnd.results.map(r => (
                  <div key={r.userId} className="match-end-row">
                    <span className="rank-tag">{RANK_LABEL[r.finalRank] ?? `#${r.finalRank}`}</span>
                    <span className="match-end-name">{r.displayName}</span>
                    <span className={`score-pill ${r.score > 0 ? 'pos' : r.score < 0 ? 'neg' : ''}`}>
                      {r.score > 0 ? `+${r.score}` : r.score}
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
