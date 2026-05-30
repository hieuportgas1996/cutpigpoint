import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { CardSvg } from '../game/CardSvg';
import { MaiBranch } from '../game/effects/MaiBranch';
import { Confetti } from '../game/effects/Confetti';
import { Card, cardFromDto, cardToDto, compareCard, detectCombo, comboBeats, isFourPairRun, isBigCutCombo, findFourPairRun } from '../game/cards';
import { api, MatchStatus, RoundEnd, RoundResultEntry } from '../api';
import { playSound, type SoundKey } from '../sounds';
import '../game/demo.css';
import './room-lobby.css';
import './room-play.css';

const SEAT_POSITIONS: Array<'bottom' | 'right' | 'top' | 'left'> = ['bottom', 'right', 'top', 'left'];
const RANK_LABEL: Record<number, string> = { 1: 'Nhất', 2: 'Nhì', 3: 'Ba', 4: 'Tư' };

const STICKER_PREFIX = '::sticker:';
const STICKERS: Array<{ code: string; emoji: string; label: string; hint: string }> = [
  { code: 'sos', emoji: '🆘', label: 'SOS', hint: 'Báo sắp xử' },
  { code: 'no-kill', emoji: '🙏', label: 'Không giết', hint: 'Không xử đâu' },
  { code: 'go-away', emoji: '👋', label: 'Bỏ đi nhỏ', hint: 'Pass đi nào' },
  { code: 'siuuu', emoji: '🐐', label: 'SIUUUU', hint: 'Ăn mừng' },
  { code: 'chop-it', emoji: '🪓', label: 'Chặt chết mẹ nó', hint: 'Khích chặt heo' },
  { code: 'sorry', emoji: '😢', label: 'Sorry', hint: 'Xin lỗi mất nết' },
  { code: 'beg', emoji: '🛐', label: 'Tao lạy mày', hint: 'Năn nỉ' },
  { code: 'chiuroi', emoji: '🤷', label: 'Thế thì chịu', hint: 'Thế thì chịu rồi' },
];
const STICKER_SOUND: Partial<Record<string, SoundKey>> = {
  'sos': 'sos',
  'siuuu': 'ronaldoSiuuuu',
  'sorry': 'sorry',
  'beg': 'begging',
  'chiuroi': 'chiuroi',
};
const STICKER_VOLUME = 0.45;
const STICKER_BY_CODE: Record<string, typeof STICKERS[number]> = STICKERS.reduce(
  (acc, s) => { acc[s.code] = s; return acc; },
  {} as Record<string, typeof STICKERS[number]>,
);
const STICKER_COOLDOWN_MS = 3000;

function parseSticker(text: string): typeof STICKERS[number] | null {
  if (!text.startsWith(STICKER_PREFIX)) return null;
  const code = text.slice(STICKER_PREFIX.length).trim();
  return STICKER_BY_CODE[code] ?? null;
}

function scoreBreakdownParts(r: RoundResultEntry): Array<{ label: string; value: number }> {
  const parts: Array<{ label: string; value: number }> = [];
  if (r.whiteWinDelta !== 0) parts.push({ label: '🌟 Về trắng', value: r.whiteWinDelta });
  if (r.baseRankScore !== 0) parts.push({ label: `Hạng ${RANK_LABEL[r.finalRank] ?? r.finalRank}`, value: r.baseRankScore });
  if (r.chopBonus !== 0) parts.push({ label: '🐷 Chặt heo', value: r.chopBonus });
  if (r.judgeDelta !== 0) {
    if (r.judgeIsWinner) parts.push({ label: '⚖️ Phán xử (ăn)', value: r.judgeDelta });
    else if (r.judgeIsVictim) parts.push({ label: `⚖️ Bị xử (4 + giữ ${r.judgeHeldValue})`, value: r.judgeDelta });
    else if (r.judgeIsPardoned) parts.push({ label: '⚖️ Tha (đã ra bài)', value: r.judgeDelta });
    else parts.push({ label: '⚖️ Phán xử', value: r.judgeDelta });
  }
  if (r.threeOfSpadesDelta !== 0) {
    const label = r.wonByThreeOfSpades ? '🏆 Thắng cuối 3♠'
      : r.lostByThreeOfSpades ? '💀 Đui 3♠'
      : '3♠';
    parts.push({ label, value: r.threeOfSpadesDelta });
  }
  if (r.heldPenaltyDelta !== 0) {
    const label = r.heldPenaltyDelta < 0 ? '🐷 Chót còn hàng' : '🐷 Ăn phạt Chót';
    parts.push({ label, value: r.heldPenaltyDelta });
  }
  return parts;
}

function RoundResultRows({ round, myUserId }: { round: RoundEnd; myUserId: string }) {
  return (
    <div className="match-end-list">
      {round.results.map(r => {
        const parts = scoreBreakdownParts(r);
        const held = round.wasWhiteWin ? [] : (r.heldDetails ?? []);
        return (
          <div key={r.userId} className="match-end-row">
            <span className="rank-tag">
              {r.whiteWinReason ? '★' : RANK_LABEL[r.finalRank] ?? `#${r.finalRank}`}
            </span>
            <div className="match-end-name">
              <div>{r.userId === myUserId ? `${r.displayName} (Bạn)` : r.displayName}</div>
              {r.whiteWinReason && <div className="white-win-reason">{r.whiteWinReason}</div>}
              {held.length > 0 && (
                <div className="held-items">
                  <div className="held-items-label">Còn giữ:</div>
                  {held.map((h, i) => {
                    const icon = h.label.startsWith('Heo') ? '🐷'
                      : h.label.startsWith('Tứ quý') ? '🃏'
                      : h.label.includes('đôi thông') ? '🔗'
                      : '•';
                    const showPenalty = r.heldPenaltyDelta < 0 || r.judgeIsVictim;
                    return (
                      <div key={i} className="held-row">
                        <span className="held-chip">{icon} {h.label}</span>
                        {showPenalty ? (
                          <span className="held-value">−{h.value}đ</span>
                        ) : (
                          <span className="held-value held-value-info">{h.value}đ</span>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
              {parts.length > 0 && (
                <div className="score-breakdown">
                  {parts.map((p, i) => (
                    <div key={i} className="score-breakdown-row">
                      <span className="score-breakdown-label">{p.label}</span>
                      <span className={`score-breakdown-value ${p.value > 0 ? 'pos' : p.value < 0 ? 'neg' : ''}`}>
                        {p.value > 0 ? `+${p.value}` : p.value}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>
            <span className={`score-pill ${r.roundScore > 0 ? 'pos' : r.roundScore < 0 ? 'neg' : ''}`}>
              {r.roundScore > 0 ? `+${r.roundScore}` : r.roundScore}
            </span>
            <span className="total-score">Tổng: {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}</span>
          </div>
        );
      })}
    </div>
  );
}

export default function RoomPlayPage() {
  const { id: code } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const {
    status, state: room, matchState, privateHand, roundEnd, roundHistory, matchEnd, chatMessages, error,
    playCards, passTurn, endMatch, clearRoundEnd,
    respondWhiteWin, cutNewTrick, declineTrickCut, sendChat, startNextRound,
  } = useRoomConnection(code);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [historyOpen, setHistoryOpen] = useState(false);
  const [now, setNow] = useState(Date.now());
  const [viewportW, setViewportW] = useState(() => typeof window !== 'undefined' ? window.innerWidth : 1024);
  const handAreaRef = useRef<HTMLDivElement | null>(null);
  const [handWidth, setHandWidth] = useState(0);
  const [chatOpen, setChatOpen] = useState(false);
  const [chatInput, setChatInput] = useState('');
  const [chatSeenCount, setChatSeenCount] = useState(0);
  const chatListRef = useRef<HTMLDivElement | null>(null);
  const [seatBubbles, setSeatBubbles] = useState<Record<string, { id: string; text: string }>>({});
  const lastBubbledChatId = useRef<string | null>(null);
  const [cutPigBanner, setCutPigBanner] = useState<{ id: number; cutter: string; comboLabel: string } | null>(null);
  const lastCutSignature = useRef<string | null>(null);
  const [stickerOverlay, setStickerOverlay] = useState<{ id: string; code: string; emoji: string; label: string; sender: string; senderUserId: string } | null>(null);
  // ID người Nhất đang được hiển thị pháo hoa (chỉ khi roundEnd vừa bùng ra)
  const [winnerCelebration, setWinnerCelebration] = useState<string | null>(null);
  const lastCelebratedRoundRef = useRef<string | null>(null);
  const lastStickerSentAt = useRef<number>(0);
  const [stickerCooldownLeft, setStickerCooldownLeft] = useState(0);

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

  // Show seat bubble (chat) or center sticker overlay for newest chat message
  useEffect(() => {
    if (chatMessages.length === 0) return;
    const latest = chatMessages[chatMessages.length - 1];
    if (lastBubbledChatId.current === latest.id) return;
    lastBubbledChatId.current = latest.id;
    const sticker = parseSticker(latest.text);
    if (sticker) {
      setStickerOverlay({ id: latest.id, code: sticker.code, emoji: sticker.emoji, label: sticker.label, sender: latest.displayName, senderUserId: latest.userId });
      const sndKey = STICKER_SOUND[sticker.code];
      if (sndKey) playSound(sndKey, STICKER_VOLUME);
      const t = setTimeout(() => {
        setStickerOverlay(prev => (prev?.id === latest.id ? null : prev));
      }, 3000);
      return () => clearTimeout(t);
    }
    setSeatBubbles(prev => ({ ...prev, [latest.userId]: { id: latest.id, text: latest.text } }));
    const t = setTimeout(() => {
      setSeatBubbles(prev => {
        if (prev[latest.userId]?.id !== latest.id) return prev;
        const { [latest.userId]: _drop, ...rest } = prev;
        return rest;
      });
    }, 5000);
    return () => clearTimeout(t);
  }, [chatMessages]);

  // Tick cooldown countdown so buttons re-enable visually
  useEffect(() => {
    if (stickerCooldownLeft <= 0) return;
    const t = setInterval(() => {
      const left = Math.max(0, STICKER_COOLDOWN_MS - (Date.now() - lastStickerSentAt.current));
      setStickerCooldownLeft(left);
      if (left <= 0) clearInterval(t);
    }, 100);
    return () => clearInterval(t);
  }, [stickerCooldownLeft]);

  const unreadChat = Math.max(0, chatMessages.length - chatSeenCount);

  const isMobile = viewportW < 720;
  const cardSize: 'sm' | 'md' = isMobile ? 'sm' : 'md';
  const cardWidth = isMobile ? 44 : 64;

  // Clear selection chỉ khi sang ván mới — giữ nguyên lá đã tick khi đối phương đánh / pass / khi
  // lượt chuyển qua chuyển lại, để người chơi không phải tick lại từ đầu mỗi turn.
  useEffect(() => {
    setSelected(new Set());
  }, [matchState?.roundNumber]);

  // Countdown beep khi còn 3s lượt của mình — play 1 lần (per turn) khi secLeft chạm 3.
  const lastCountdownTurnRef = useRef<string | null>(null);
  useEffect(() => {
    if (!matchState || matchState.status !== MatchStatus.InProgress) return;
    const myUid = state.status === 'authenticated' ? state.userId : '';
    const isMine = matchState.players[matchState.currentTurnSeatIndex]?.userId === myUid;
    if (!isMine) return;
    const secLeft = Math.max(0, Math.ceil((new Date(matchState.turnDeadline).getTime() - now) / 1000));
    if (secLeft !== 3) return;
    const turnKey = `${matchState.roundNumber}|${matchState.turnDeadline}`;
    if (lastCountdownTurnRef.current === turnKey) return;
    lastCountdownTurnRef.current = turnKey;
    playSound('countdown', 0.6);
  }, [now, matchState?.turnDeadline, matchState?.currentTurnSeatIndex, matchState?.status, matchState?.roundNumber, state]);

  // Auto-direct mọi player sang trang lịch sử phòng sau khi MatchEnd (10s, có nút "Đi ngay").
  const [matchEndAt, setMatchEndAt] = useState<number | null>(null);
  useEffect(() => {
    if (!matchEnd) { setMatchEndAt(null); return; }
    setMatchEndAt(Date.now());
    // Sound pháo hoa khi modal tổng kết trận hiện ra.
    playSound('fireworkNew', 0.7);
  }, [matchEnd]);
  useEffect(() => {
    if (!matchEnd || !code) return;
    const t = setTimeout(() => navigate(`/rooms/${code.toUpperCase()}/history`), 10000);
    return () => clearTimeout(t);
  }, [matchEnd, code, navigate]);
  const matchEndLeftSec = matchEndAt
    ? Math.max(0, 10 - Math.floor((now - matchEndAt) / 1000))
    : 10;

  // Notify turn sound: play khi lượt vừa chuyển đến mình (transition false → true).
  const wasMyTurnRef = useRef(false);
  useEffect(() => {
    const myUid = state.status === 'authenticated' ? state.userId : '';
    const isMine = matchState?.status === MatchStatus.InProgress
      && matchState?.players[matchState.currentTurnSeatIndex]?.userId === myUid;
    if (isMine && !wasMyTurnRef.current) {
      playSound('notifyTurn', 0.7);
    }
    wasMyTurnRef.current = !!isMine;
  }, [matchState?.currentTurnSeatIndex, matchState?.status, state]);

  // Auto-clear roundEnd when next round begins (server auto-advances)
  useEffect(() => {
    if (!roundEnd) return;
    if (matchState?.status === MatchStatus.InProgress
      || matchState?.status === MatchStatus.WhiteWinChoice) {
      clearRoundEnd();
    }
  }, [matchState?.status, matchState?.roundNumber, roundEnd, clearRoundEnd]);

  // Trì hoãn hiển thị modal kết quả ván 2s sau khi server gửi RoundEnd — cho pháo hoa của người Nhất
  // bùng lên trước, modal không che màn hình ngay. White-win hiển thị ngay (không có pháo hoa Nhất).
  const [delayedRoundEnd, setDelayedRoundEnd] = useState<RoundEnd | null>(null);
  useEffect(() => {
    if (!roundEnd) { setDelayedRoundEnd(null); return; }
    if (roundEnd.wasWhiteWin) { setDelayedRoundEnd(roundEnd); return; }
    const t = setTimeout(() => setDelayedRoundEnd(roundEnd), 2000);
    return () => clearTimeout(t);
  }, [roundEnd]);

  // Sound vỗ tay khi modal tổng kết ván vừa hiện ra. Dedup theo round.
  const lastVictoryRoundRef = useRef<string | null>(null);
  useEffect(() => {
    if (!delayedRoundEnd) return;
    const key = `${delayedRoundEnd.matchId}|${delayedRoundEnd.roundNumber}`;
    if (lastVictoryRoundRef.current === key) return;
    lastVictoryRoundRef.current = key;
    playSound('clapHand', 0.7);
  }, [delayedRoundEnd]);

  // Pháo hoa + victory NGAY khi có người về Nhất (finalRank=1 xuất hiện trong matchState),
  // không cần chờ roundEnd của cả ván. Dùng key (roundNumber + winnerId) để chỉ chạy 1 lần / ván.
  const winnerUserId = matchState?.players.find(p => p.finalRank === 1)?.userId ?? null;
  useEffect(() => {
    if (!matchState || !winnerUserId) return;
    const key = `${matchState.roundNumber}|${winnerUserId}`;
    if (lastCelebratedRoundRef.current === key) return;
    lastCelebratedRoundRef.current = key;
    setWinnerCelebration(winnerUserId);
    const t = setTimeout(() => {
      setWinnerCelebration(prev => prev === winnerUserId ? null : prev);
    }, 3000);
    return () => clearTimeout(t);
  }, [matchState?.roundNumber, winnerUserId]);

  // Track previous trick combo signature/kind to detect "3-đôi-thông / tứ quý vừa bị thay bằng combo
  // lớn hơn" → play ahh.mp3 (mọi user nghe).
  const prevTrickComboKindRef = useRef<{ kind: string; length: number } | null>(null);

  // Detect chặt heo (big cut combo): tứ quý / 3-đôi-thông / 4-đôi-thông vừa được đánh ra.
  useEffect(() => {
    const trickCards = matchState?.currentTrick;
    if (!trickCards || trickCards.length === 0) {
      lastCutSignature.current = null;
      prevTrickComboKindRef.current = null;
      return;
    }
    const cards = trickCards.map(cardFromDto);
    const combo = detectCombo(cards);
    if (!combo) return;

    // Detect: previous combo trên bàn là 3-đôi-thông hoặc tứ quý, vừa bị thay bằng combo lớn hơn
    // (tứ quý ăn 3-đôi-thông, hoặc 4-đôi-thông ăn 3-đôi-thông/tứ quý). Play `ahh` cho mọi user.
    const prev = prevTrickComboKindRef.current;
    const prevWasBigDefender = prev && (
      (prev.kind === 'runOfPairs' && prev.length === 6) || // 3 đôi thông
      (prev.kind === 'four')                                // tứ quý
    );
    const nowIsBigCut = isBigCutCombo(combo);
    if (prevWasBigDefender && nowIsBigCut && (prev!.kind !== combo.kind || prev!.length !== combo.cards.length)) {
      playSound('ahh', 0.8);
    }
    prevTrickComboKindRef.current = { kind: combo.kind, length: combo.cards.length };

    if (!nowIsBigCut) return;
    const signature = `${matchState!.roundNumber}|${cards.map(c => c.id).sort().join(',')}`;
    if (lastCutSignature.current === signature) return;
    lastCutSignature.current = signature;
    // Sound chặt heo (mọi user nghe).
    playSound('uhhhh', 0.8);
    const cutterId = matchState!.currentTrickOwnerId;
    const cutter = matchState!.players.find(p => p.userId === cutterId)?.displayName ?? 'Ai đó';
    const comboLabel = combo.kind === 'four' ? 'Tứ quý'
      : combo.cards.length === 8 ? '4 đôi thông'
      : '3 đôi thông';
    const id = Date.now();
    setCutPigBanner({ id, cutter, comboLabel });
    const t = setTimeout(() => {
      setCutPigBanner(prev => (prev?.id === id ? null : prev));
    }, 3200);
    return () => clearTimeout(t);
  }, [matchState?.currentTrick, matchState?.roundNumber, matchState?.currentTrickOwnerId]);

  const myUserId = state.status === 'authenticated' ? state.userId : '';
  const me = matchState?.players.find(p => p.userId === myUserId) ?? null;
  const isHost = matchState?.hostUserId === myUserId;
  const isMyTurn = matchState?.players[matchState.currentTurnSeatIndex]?.userId === myUserId
    && matchState?.status === MatchStatus.InProgress;
  const myHand: Card[] = (privateHand?.hand ?? []).map(cardFromDto).sort(compareCard);
  const trick: Card[] = (matchState?.currentTrick ?? []).map(cardFromDto);
  const trickCombo = trick.length > 0 ? detectCombo(trick) : null;
  // Lá vừa thắng vòng trước (hiển thị mờ khi chưa ai mở nước mới).
  const lastWonTrick: Card[] = (matchState?.lastWonTrick ?? []).map(cardFromDto);
  const lastWonTrickWinnerName = matchState?.lastWonTrickWinnerId
    ? matchState.players.find(p => p.userId === matchState.lastWonTrickWinnerId)?.displayName ?? null
    : null;

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

  // Direction để animate lá bài bay từ avatar của trick-owner vào giữa bàn.
  const trickOwnerSeat = matchState.currentTrickOwnerId
    ? seatLayout.find(s => s.player.userId === matchState.currentTrickOwnerId)
    : null;
  const flyDirection: 'bottom' | 'top' | 'left' | 'right' = trickOwnerSeat?.position ?? 'bottom';
  // Key = owner + cards → khi combo đổi, key đổi → React unmount+remount → animation chạy lại.
  const flyKey = `${matchState.currentTrickOwnerId ?? ''}|${trick.map(c => c.id).join(',')}`;

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
      setSelected(new Set());
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

  async function handleStartNextRoundNow() {
    try {
      await startNextRound();
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

  async function handleSendSticker(code: string) {
    const left = STICKER_COOLDOWN_MS - (Date.now() - lastStickerSentAt.current);
    if (left > 0) return;
    lastStickerSentAt.current = Date.now();
    setStickerCooldownLeft(STICKER_COOLDOWN_MS);
    try { await sendChat(`${STICKER_PREFIX}${code}`); }
    catch (e) {
      lastStickerSentAt.current = 0;
      setStickerCooldownLeft(0);
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
          {matchState.status === MatchStatus.InProgress ? (
            <div className={`turn-timer ${turnLeftSec <= 5 ? 'low' : ''}`}>
              ⏱ {turnLeftSec}s
            </div>
          ) : (
            <div className="turn-timer" style={{ opacity: 0.4 }}>—</div>
          )}
          <button
            type="button"
            className="tlmn-btn ghost sm history-toggle"
            onClick={() => setHistoryOpen(true)}
            title="Lịch sử các ván trong trận"
            disabled={roundHistory.length === 0}
          >
            📜 Lịch sử {roundHistory.length > 0 && `(${roundHistory.length})`}
          </button>
        </div>

        <button
          className="chat-fab"
          onClick={() => setChatOpen(o => !o)}
          title="Chat trong phòng"
          aria-label="Mở chat"
        >
          💬
          {unreadChat > 0 && <span className="chat-fab-badge">{unreadChat}</span>}
        </button>

        <div className="tlmn-table">
          <MaiBranch corner="tl" />
          <MaiBranch corner="tr" />
          <MaiBranch corner="bl" />
          <MaiBranch corner="br" />

          {seatLayout.map(({ player, position }) => {
            const isTurn = matchState.players[matchState.currentTurnSeatIndex]?.userId === player.userId
              && matchState.status === MatchStatus.InProgress;
            const isMe = player.userId === myUserId;
            const bubble = seatBubbles[player.userId];
            return (
              <div key={player.userId} className={`tlmn-seat tlmn-seat-${position} ${isTurn ? 'is-turn' : ''}`}>
                {bubble && <div key={bubble.id} className="seat-chat-bubble">{bubble.text}</div>}
                <div className="tlmn-avatar">
                  {player.hasAvatar
                    ? <img src={api.userAvatarUrl(player.userId)} alt={player.displayName} />
                    : player.displayName.charAt(0).toUpperCase()}
                </div>
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
                {isTurn && (
                  <div className={`tlmn-seat-timer ${turnLeftSec <= 10 ? 'low' : ''}`}>
                    ⏱ {turnLeftSec}s
                  </div>
                )}
                {player.passedThisTrick && !player.finalRank && (
                  <div className="tlmn-seat-pass">BỎ LƯỢT</div>
                )}
                {isMe && cutPigBanner && (
                  <div className="seat-cut-pig" key={cutPigBanner.id}>
                    <div className="seat-cut-pig-pigs">🐷💥🐷</div>
                    <div className="seat-cut-pig-text">Chặt heo!</div>
                    <div className="seat-cut-pig-sub">{cutPigBanner.cutter} · {cutPigBanner.comboLabel}</div>
                  </div>
                )}
                {stickerOverlay?.senderUserId === player.userId && (
                  <div className={`seat-sticker sticker-${stickerOverlay.code}`} key={stickerOverlay.id}>
                    <div className="seat-sticker-emoji">{stickerOverlay.emoji}</div>
                    <div className="seat-sticker-label">{stickerOverlay.label}</div>
                    <div className="seat-sticker-sender">— {stickerOverlay.sender}</div>
                  </div>
                )}
                {winnerCelebration === player.userId && (
                  <div className="seat-fireworks" aria-hidden="true">
                    <span className="fw fw-1">🎆</span>
                    <span className="fw fw-2">🎇</span>
                    <span className="fw fw-3">✨</span>
                    <span className="fw fw-4">🎉</span>
                    <span className="fw fw-5">🏆</span>
                  </div>
                )}
              </div>
            );
          })}

          <div className="play-area-cards">
            {trick.length === 0 ? (
              lastWonTrick.length > 0 ? (
                <div className="play-won-trick">
                  <div className="play-card-row play-card-row-faded">
                    {lastWonTrick.map(c => (
                      <div key={c.id} className="play-card-slot">
                        <CardSvg card={c} size={cardSize} />
                      </div>
                    ))}
                  </div>
                  <div className="play-won-trick-label muted">
                    {lastWonTrickWinnerName ? `${lastWonTrickWinnerName} thắng vòng` : 'Thắng vòng'} · mở nước mới
                  </div>
                </div>
              ) : (
                <div className="play-empty muted">Mở nước mới</div>
              )
            ) : (
              <div key={flyKey} className={`play-card-row fly-from-${flyDirection}`}>
                {trick.map(c => (
                  <div key={c.id} className="play-card-slot">
                    <CardSvg card={c} size={cardSize} />
                  </div>
                ))}
              </div>
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
                        transform: `translate3d(${offset}px, 0, 0)`,
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

        {isWhiteWinChoicePhase && myWhiteWinReason && (
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
              {myWhiteWinAccepted === null ? (
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
                    {whiteWinLeftSec > 0
                      ? <>Đang chờ người khác chọn… <b>{whiteWinLeftSec}s</b></>
                      : <>Đang xử lý…</>}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {isPendingTrickCut && canCutTrick && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.35)' }}>
            <div className="match-end-card">
              <h2>⚡ {trickWinnerName} sắp mở trick mới</h2>
              <div className="next-round-countdown">
                Bạn có 4 đôi thông — chặn để giành lượt? <b>{trickCutLeftSec}s</b>
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn primary" onClick={handleCutTrick}>⚔ Chặn bằng 4 đôi thông</button>
                <button className="tlmn-btn ghost" onClick={handleDeclineCut}>Không chặn</button>
              </div>
            </div>
          </div>
        )}

        {delayedRoundEnd && !matchEnd && !isWhiteWinChoicePhase && (
          <div className="match-end-overlay">
            {delayedRoundEnd.wasWhiteWin && <Confetti active={true} />}
            <div className="match-end-card">
              <h2>
                {delayedRoundEnd.wasWhiteWin
                  ? '🌟 Có người về trắng!'
                  : delayedRoundEnd.wasJudge
                  ? `⚖️ Phán xử — Ván ${delayedRoundEnd.roundNumber}`
                  : `🎉 Kết quả ván ${delayedRoundEnd.roundNumber}`}
              </h2>
              <RoundResultRows round={delayedRoundEnd} myUserId={myUserId} />
              <div className="match-end-actions">
                <div className="next-round-countdown">
                  🎴 Ván tiếp sau <b>{nextRoundLeftSec}s</b>…
                </div>
                {isHost && (
                  <>
                    <button className="tlmn-btn primary" onClick={handleStartNextRoundNow}>▶ Bắt đầu ngay</button>
                    <button className="tlmn-btn ghost" onClick={handleEndMatch}>Kết thúc trận</button>
                  </>
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
              <div className="next-round-countdown">
                📜 Tự chuyển sang bảng điểm sau <b>{matchEndLeftSec}s</b>…
              </div>
              <button className="tlmn-btn primary" onClick={() => navigate(`/rooms/${(code ?? '').toUpperCase()}/history`)}>
                ▶ Đi ngay
              </button>
            </div>
          </div>
        )}

        {historyOpen && (
          <div className="match-end-overlay" onClick={() => setHistoryOpen(false)}>
            <div className="match-end-card history-card" onClick={e => e.stopPropagation()}>
              <div className="history-header">
                <h2>📜 Lịch sử ván trong trận</h2>
                <button className="tlmn-btn ghost sm" onClick={() => setHistoryOpen(false)} aria-label="Đóng">✕</button>
              </div>
              {roundHistory.length === 0 ? (
                <div className="muted">Chưa có ván nào kết thúc.</div>
              ) : (
                <div className="history-list">
                  {[...roundHistory].reverse().map(r => {
                    const winner = r.results.find(x => x.finalRank === 1);
                    const title = r.wasWhiteWin
                      ? `Ván ${r.roundNumber} · 🌟 Về trắng`
                      : r.wasJudge
                      ? `Ván ${r.roundNumber} · ⚖️ Phán xử`
                      : `Ván ${r.roundNumber}${winner ? ` · ${winner.displayName} Nhất` : ''}`;
                    return (
                      <details key={`${r.matchId}-${r.roundNumber}`} className="history-item" open={r === roundHistory[roundHistory.length - 1]}>
                        <summary className="history-item-summary">
                          <span>{title}</span>
                          <span className="history-item-scores">
                            {r.results.map(x => (
                              <span
                                key={x.userId}
                                className={`score-chip ${x.roundScore > 0 ? 'pos' : x.roundScore < 0 ? 'neg' : ''}`}
                                title={x.displayName}
                              >
                                {x.displayName.split(' ').pop()}: {x.roundScore > 0 ? `+${x.roundScore}` : x.roundScore}
                              </span>
                            ))}
                          </span>
                        </summary>
                        <RoundResultRows round={r} myUserId={myUserId} />
                      </details>
                    );
                  })}
                </div>
              )}
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
                chatMessages.map(m => {
                  const sticker = parseSticker(m.text);
                  return (
                    <div key={m.id} className={`chat-msg ${m.userId === myUserId ? 'mine' : ''}`}>
                      <div className="chat-msg-meta">
                        <span className="chat-msg-name">{m.userId === myUserId ? 'Bạn' : m.displayName}</span>
                        <span className="chat-msg-time muted small">
                          {new Date(m.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                        </span>
                      </div>
                      {sticker ? (
                        <div className="chat-msg-sticker">
                          <span className="chat-msg-sticker-emoji">{sticker.emoji}</span>
                          <span>{sticker.label}</span>
                        </div>
                      ) : (
                        <div className="chat-msg-text">{m.text}</div>
                      )}
                    </div>
                  );
                })
              )}
            </div>
            <div className="sticker-bar">
              {STICKERS.map(s => (
                <button
                  key={s.code}
                  type="button"
                  className="sticker-chip"
                  title={s.hint}
                  disabled={stickerCooldownLeft > 0}
                  onClick={() => handleSendSticker(s.code)}
                >
                  <span className="sticker-chip-emoji">{s.emoji}</span>
                  <span className="sticker-chip-label">{s.label}</span>
                </button>
              ))}
              {stickerCooldownLeft > 0 && (
                <span className="sticker-cooldown muted small">{Math.ceil(stickerCooldownLeft / 1000)}s</span>
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
