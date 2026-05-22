import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { CardSvg } from '../game/CardSvg';
import { MaiBranch } from '../game/effects/MaiBranch';
import { Confetti } from '../game/effects/Confetti';
import { Card, cardFromDto, cardToDto, compareCard, detectCombo, comboBeats, isFourPairRun, findFourPairRun } from '../game/cards';
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
    status, state: room, matchState, privateHand, roundEnd, matchEnd, chatMessages, error,
    playCards, passTurn, endMatch, clearRoundEnd,
    respondWhiteWin, cutNewTrick, declineTrickCut, sendChat,
  } = useRoomConnection(code);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [now, setNow] = useState(Date.now());
  const [viewportW, setViewportW] = useState(() => typeof window !== 'undefined' ? window.innerWidth : 1024);
  const handAreaRef = useRef<HTMLDivElement | null>(null);
  const [handWidth, setHandWidth] = useState(0);
  const [chatOpen, setChatOpen] = useState(false);
  const [chatInput, setChatInput] = useState('');
  const [chatSeenCount, setChatSeenCount] = useState(0);
  const chatListRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    const handleResize = () => {
      setViewportW(window.innerWidth);
      if (handAreaRef.current) setHandWidth(handAreaRef.current.offsetWidth);
    };
    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    if (handAreaRef.current) setHandWidth(handAreaRef.current.offsetWidth);
  }, [handAreaRef.current]);

  // Auto-scroll chat to bottom when new messages arrive and panel open
  useEffect(() => {
    if (chatOpen && chatListRef.current) {
      chatListRef.current.scrollTop = chatListRef.current.scrollHeight;
    }
    if (chatOpen) setChatSeenCount(chatMessages.length);
  }, [chatMessages.length, chatOpen]);

  const unreadChat = Math.max(0, chatMessages.length - chatSeenCount);

  const isMobile = viewportW < 720;
  const cardSize: 'sm' | 'md' = isMobile ? 'sm' : 'md';
  const cardWidth = isMobile ? 44 : 64;

  useEffect(() => {
    setSelected(new Set());
  }, [matchState?.currentTurnSeatIndex, matchState?.roundNumber]);

  // Auto-clear roundEnd when next round begins (server auto-advances)
  useEffect(() => {
    if (!roundEnd) return;
    if (matchState?.status === MatchStatus.InProgress
      || matchState?.status === MatchStatus.WhiteWinChoice) {
      clearRoundEnd();
    }
  }, [matchState?.status, matchState?.roundNumber, roundEnd, clearRoundEnd]);

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

  const isWhiteWinChoicePhase = matchState?.status === MatchStatus.WhiteWinChoice;
  const myWhiteWinReason = me?.whiteWinReason ?? null;
  const myWhiteWinAccepted = me?.whiteWinAccepted ?? null;
  const whiteWinLeftSec = matchState?.whiteWinDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.whiteWinDeadline).getTime() - now) / 1000))
    : 0;

  const isPendingTrickCut = matchState?.status === MatchStatus.PendingTrickCut;
  const canCutTrick = isPendingTrickCut && (matchState?.trickCutCandidates ?? []).includes(myUserId);
  const trickCutLeftSec = matchState?.trickCutDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.trickCutDeadline).getTime() - now) / 1000))
    : 0;
  const trickWinnerName = matchState?.players.find(p => p.userId === matchState.pendingTrickWinnerId)?.displayName ?? '';

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
  const nextRoundLeftSec = matchState.nextRoundAt
    ? Math.max(0, Math.ceil((new Date(matchState.nextRoundAt).getTime() - now) / 1000))
    : 5;

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

  async function handleEndMatch() {
    if (!confirm('Kết thúc trận? Phòng sẽ đóng.')) return;
    try {
      await endMatch();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handleAcceptWhiteWin() {
    try { await respondWhiteWin(true); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleDeclineWhiteWin() {
    try { await respondWhiteWin(false); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleCutTrick() {
    const fourPair = findFourPairRun(myHand);
    if (!fourPair) {
      toast.push('error', 'Không tìm thấy 4 đôi thông trong tay.');
      return;
    }
    try { await cutNewTrick(fourPair.map(cardToDto)); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleDeclineCut() {
    try { await declineTrickCut(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleSendChat() {
    const text = chatInput.trim();
    if (!text) return;
    setChatInput('');
    try { await sendChat(text); }
    catch (e) { toast.push('error', (e as Error).message); }
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
          {matchState.status === MatchStatus.InProgress ? (
            <div className={`turn-timer ${turnLeftSec <= 5 ? 'low' : ''}`}>
              ⏱ {turnLeftSec}s
            </div>
          ) : (
            <div className="turn-timer" style={{ opacity: 0.4 }}>—</div>
          )}
          <button
            className="tlmn-btn ghost chat-toggle"
            onClick={() => setChatOpen(o => !o)}
            title="Chat trong phòng"
          >
            💬 {unreadChat > 0 && <span className="chat-unread">{unreadChat}</span>}
          </button>
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
                    <span>🂠 {isMe ? player.cardsLeft : ''}</span>
                    <span className={`score-pill ${player.totalScore > 0 ? 'pos' : player.totalScore < 0 ? 'neg' : ''}`}>
                      {player.totalScore > 0 ? `+${player.totalScore}` : player.totalScore}
                    </span>
                    {player.finalRank && (
                      <span className="rank-tag-mini">{RANK_LABEL[player.finalRank] || `#${player.finalRank}`}</span>
                    )}
                  </div>
                </div>
                {isTurn && (
                  <div className={`tlmn-seat-timer ${turnLeftSec <= 10 ? 'low' : ''}`}>
                    ⏱ {turnLeftSec}s
                  </div>
                )}
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
              trick.map(c => (
                <div key={c.id} className="play-card-slot">
                  <CardSvg card={c} size={cardSize} />
                </div>
              ))
            )}
          </div>
        </div>

        <div className="my-hand-area" ref={handAreaRef}>
          {myHand.length === 0 ? (
            <div className="muted">Bạn đã hết bài 🎉</div>
          ) : (
            <div className="my-hand-fan">
              {(() => {
                const padding = 24;
                const available = Math.max(handWidth - padding, cardWidth);
                const maxSpread = isMobile ? 24 : 38;
                const minSpread = isMobile ? 14 : 20;
                const naturalSpread = myHand.length > 1
                  ? (available - cardWidth) / (myHand.length - 1)
                  : 0;
                const spread = Math.max(minSpread, Math.min(maxSpread, naturalSpread));
                return myHand.map((c, idx) => {
                  const offset = (idx - (myHand.length - 1) / 2) * spread;
                  const isSelected = selected.has(c.id);
                  return (
                    <div
                      key={c.id}
                      className="my-hand-slot"
                      style={{
                        transform: `translateX(${offset}px) translateY(${isSelected ? -16 : 0}px)`,
                        marginLeft: -cardWidth / 2,
                      }}
                    >
                      <CardSvg
                        card={c}
                        size={cardSize}
                        selected={isSelected}
                        onClick={() => toggle(c.id)}
                      />
                    </div>
                  );
                });
              })()}
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

        {isWhiteWinChoicePhase && (
          <div className="match-end-overlay">
            <div className="match-end-card">
              <h2>🌟 Có bộ về trắng</h2>
              <div className="match-end-list">
                {matchState.players.filter(p => p.whiteWinReason).map(p => (
                  <div key={p.userId} className="match-end-row">
                    <span className="rank-tag">★</span>
                    <div className="match-end-name">
                      <div>{p.userId === myUserId ? 'Bạn' : p.displayName}</div>
                      <div className="white-win-reason">{p.whiteWinReason}</div>
                    </div>
                    <span className="muted small">
                      {p.whiteWinAccepted === true ? '✓ Về trắng'
                        : p.whiteWinAccepted === false ? '✗ Từ chối'
                        : '… đang chọn'}
                    </span>
                  </div>
                ))}
              </div>
              {myWhiteWinReason && myWhiteWinAccepted === null ? (
                <div className="match-end-actions">
                  <div className="next-round-countdown">
                    Bạn có <b>{myWhiteWinReason}</b> — về trắng để thắng ngay? ({whiteWinLeftSec}s)
                  </div>
                  <button className="tlmn-btn primary" onClick={handleAcceptWhiteWin}>✓ Về trắng</button>
                  <button className="tlmn-btn ghost" onClick={handleDeclineWhiteWin}>✗ Đánh tiếp</button>
                </div>
              ) : (
                <div className="match-end-actions">
                  <div className="next-round-countdown">
                    Đang chờ chọn… <b>{whiteWinLeftSec}s</b>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {isPendingTrickCut && (
          <div className="match-end-overlay" style={{ pointerEvents: canCutTrick ? 'auto' : 'none', background: 'rgba(0,0,0,0.35)' }}>
            <div className="match-end-card" style={{ pointerEvents: 'auto' }}>
              <h2>⚡ {trickWinnerName} sắp mở trick mới</h2>
              {canCutTrick ? (
                <>
                  <div className="next-round-countdown">
                    Bạn có 4 đôi thông — chặn để giành lượt? <b>{trickCutLeftSec}s</b>
                  </div>
                  <div className="match-end-actions">
                    <button className="tlmn-btn primary" onClick={handleCutTrick}>⚔ Chặn bằng 4 đôi thông</button>
                    <button className="tlmn-btn ghost" onClick={handleDeclineCut}>Không chặn</button>
                  </div>
                </>
              ) : (
                <div className="next-round-countdown">
                  Đang chờ người có 4 đôi thông quyết định… <b>{trickCutLeftSec}s</b>
                </div>
              )}
            </div>
          </div>
        )}

        {roundEnd && !matchEnd && !isWhiteWinChoicePhase && (
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
                <div className="next-round-countdown">
                  🎴 Ván tiếp sau <b>{nextRoundLeftSec}s</b>…
                </div>
                {isHost && (
                  <button className="tlmn-btn ghost" onClick={handleEndMatch}>Kết thúc trận</button>
                )}
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

        {chatOpen && (
          <div className="chat-panel">
            <div className="chat-panel-header">
              <span>💬 Chat phòng</span>
              <button className="tlmn-btn ghost sm" onClick={() => setChatOpen(false)}>✕</button>
            </div>
            <div className="chat-panel-list" ref={chatListRef}>
              {chatMessages.length === 0 ? (
                <div className="muted small">Chưa có tin nhắn nào.</div>
              ) : (
                chatMessages.map(m => (
                  <div key={m.id} className={`chat-msg ${m.userId === myUserId ? 'mine' : ''}`}>
                    <div className="chat-msg-meta">
                      <span className="chat-msg-name">{m.userId === myUserId ? 'Bạn' : m.displayName}</span>
                      <span className="chat-msg-time muted small">
                        {new Date(m.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                      </span>
                    </div>
                    <div className="chat-msg-text">{m.text}</div>
                  </div>
                ))
              )}
            </div>
            <form
              className="chat-panel-input"
              onSubmit={e => { e.preventDefault(); handleSendChat(); }}
            >
              <input
                type="text"
                placeholder="Nhập tin nhắn…"
                value={chatInput}
                onChange={e => setChatInput(e.target.value)}
                maxLength={300}
              />
              <button type="submit" className="tlmn-btn primary sm" disabled={!chatInput.trim()}>Gửi</button>
            </form>
          </div>
        )}
      </div>
    </div>
  );
}
