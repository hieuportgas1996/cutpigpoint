import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { CardSvg } from '../game/CardSvg';
import { MaiBranch } from '../game/effects/MaiBranch';
import { Confetti } from '../game/effects/Confetti';
import { ChampionTrophy } from '../game/effects/ChampionTrophy';
import { RpsBreakScreen } from './RpsBreakScreen';
import { MathBreakScreen } from './MathBreakScreen';
import { MemoryBreakScreen } from './MemoryBreakScreen';
import { XiDachMobilePanel } from './XiDachMobilePanel';
import { ReflexBreakScreen } from './ReflexBreakScreen';
import { Card, cardFromDto, cardToDto, compareCard, detectCombo, comboBeats, isFourPairRun, isBigCutCombo, findFourPairRun } from '../game/cards';
import { api, MatchStatus, RoundEnd, RoundResultEntry, MatchPlayerPublic } from '../api';
import { playSound, stopSound, type SoundKey } from '../sounds';
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
  { code: 'so-qua', emoji: '😱', label: 'Sợ quá sợ quá', hint: 'Sợ quá sợ quá' },
  { code: 'sao-ma-do', emoji: '🛡️', label: 'Sao mà đỡ được', hint: 'Sao mà đỡ được' },
  { code: 'khong-sao-ma', emoji: '😎', label: 'Không sao mà', hint: 'Không sao mà' },
  { code: 'mu-vo-dich', emoji: '🔴', label: 'MU vô địch', hint: 'MU vô địch' },
  { code: 'dcmm', emoji: '🤬', label: 'DCMM !!', hint: 'DCMM !!' },
  { code: 'suiiii', emoji: '⚽', label: 'Suiiii', hint: 'Suiiii' },
  { code: 'dan-do', emoji: '😤', label: 'Loz dằn dơ', hint: 'Loz dằn dơ' },
];
const STICKER_SOUND: Partial<Record<string, SoundKey>> = {
  'sos': 'sos',
  'siuuu': 'ronaldoSiuuuu',
  'sorry': 'sorry',
  'beg': 'begging',
  'chiuroi': 'chiuroi',
  'so-qua': 'soQua',
  'sao-ma-do': 'saoMaDo',
  'khong-sao-ma': 'quenChaNa',
  'chop-it': 'chatChetMe',
  'no-kill': 'khongGiet',
  'go-away': 'boDiNho',
  'mu-vo-dich': 'muVoDich',
  'dcmm': 'dcmm',
  'suiiii': 'siuiii',
  'dan-do': 'danDo',
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

// Âm thanh "troll" phát khi bấm vào avatar của người chơi có tên CHỨA từ khoá (không dấu, substring).
// Thứ tự = thứ tự ưu tiên: 'thien' kiểm tra trước 'duy' (vd "Thiêns2Duyên" → Thiện chứ không phải Duy).
const AVATAR_CLICK_SOUNDS: Array<{ match: string; sound: SoundKey }> = [
  { match: 'thien', sound: 'lozThien' },
  { match: 'duy', sound: 'lozDuy' },
  { match: 'bao', sound: 'lozBao' },
  { match: 'hieu', sound: 'lozHieu' },
];

// Bỏ dấu tiếng Việt + thường hoá để so chuỗi: "Thiêns2Duyên" → "thiens2duyen".
function stripAccents(name: string): string {
  return name
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/đ/gi, 'd')
    .toLowerCase();
}

function avatarClickSound(displayName: string): SoundKey | null {
  const norm = stripAccents(displayName);
  for (const entry of AVATAR_CLICK_SOUNDS) {
    if (norm.includes(entry.match)) return entry.sound;
  }
  return null;
}

// Tổng điểm tay Xì Dách (mirror server XiDachEngine): 3..10 = mặt; J/Q/K = 10; "2"(15) = 2.
// A(14): tay 2-3 lá → linh hoạt 1/10/11 (chọn tổng cao nhất ≤21, fallback nhỏ nhất); tay 4-5 lá → 1.
function xiDachHandTotal(hand: Card[]): number {
  const n = hand.length;
  const aces = hand.filter(c => c.rank === 14).length;
  const baseSum = hand.filter(c => c.rank !== 14).reduce((s, c) => {
    if (c.rank === 15) return s + 2;
    if (c.rank >= 11) return s + 10;
    return s + c.rank;
  }, 0);
  if (aces === 0) return baseSum;
  if (n >= 4) return baseSum + aces; // A = 1
  // 2-3 lá: thử tổ hợp A ∈ {1,10,11}
  const opts = [11, 10, 1];
  let best = Infinity, bestValid = -1;
  const rec = (i: number, acc: number) => {
    if (i === aces) {
      const total = baseSum + acc;
      if (total < best) best = total;
      if (total <= 21 && total > bestValid) bestValid = total;
      return;
    }
    for (const v of opts) rec(i + 1, acc + v);
  };
  rec(0, 0);
  return bestValid >= 0 ? bestValid : best;
}

// Nhãn tay Xì Dách (mirror XiDachEngine.Label): đặc biệt hiện tên, còn lại hiện "N điểm".
function xiDachHandLabel(hand: Card[]): string {
  const n = hand.length;
  const total = xiDachHandTotal(hand);
  const aces = hand.filter(c => c.rank === 14).length;
  if (n === 2 && aces === 2) return 'Xì Vàng';
  if (n === 2 && aces === 1 && hand.some(c => c.rank >= 10 && c.rank <= 13)) return 'Xì Dách';
  if (n === 5 && total <= 21) return `Ngũ Linh (${total})`;
  if (total > 21) return `Quắc (${total})`;
  return `${total} điểm`;
}

function scoreBreakdownParts(r: RoundResultEntry): Array<{ label: string; value: number }> {
  const parts: Array<{ label: string; value: number }> = [];
  if (r.whiteWinDelta !== 0) parts.push({ label: '🌟 Về trắng', value: r.whiteWinDelta });
  // Phán xử thay toàn bộ scoring → khi có judge thì không hiện hạng thường (dùng dòng phán xử bên dưới).
  const isJudge = r.judgeIsWinner || r.judgeIsVictim || r.judgeIsPardoned;
  if (!isJudge && r.whiteWinDelta === 0 && r.festivalDelta === 0) {
    // Luôn hiện hạng (kể cả 0) cho ván thường.
    parts.push({ label: `Hạng ${RANK_LABEL[r.finalRank] ?? r.finalRank}`, value: r.baseRankScore });
  }
  if (r.chopBonus !== 0) parts.push({ label: r.chopBonus > 0 ? '🐷 Chặt heo' : '🐷 Bị chặt heo', value: r.chopBonus });
  if (isJudge) {
    if (r.judgeIsWinner) parts.push({ label: '⚖️ Phán xử ăn', value: r.judgeDelta });
    else if (r.judgeIsVictim) {
      // Victim bị xử = −4 cố định + phạt giữ bài (held). Tách 2 dòng cho dễ hiểu, không gộp.
      const fine = -4;
      const heldPart = r.judgeDelta - fine; // = −held (judgeDelta = −(4+held))
      parts.push({ label: '⚖️ Bị xử', value: fine });
      if (heldPart !== 0) parts.push({ label: '🐷 Phạt giữ bài', value: heldPart });
    }
    else if (r.judgeIsPardoned) {
      // Pardoned (Case C): tách "Đã ra bài 0đ" (được tha) + dòng hạng sub-round (Nhì/Ba +/-).
      parts.push({ label: '⚖️ Đã ra bài', value: 0 });
      if (r.judgeDelta !== 0) parts.push({ label: `Hạng ${RANK_LABEL[r.finalRank] ?? r.finalRank}`, value: r.judgeDelta });
    }
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
  if (r.starDelta !== 0) {
    parts.push({ label: '⭐ Ngôi sao ×2', value: r.starDelta });
  }
  if (r.gambleDelta !== 0) {
    const label = r.isGamble ? '🔥 Liều ×3' : '🔥 Bù người liều';
    parts.push({ label, value: r.gambleDelta });
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
              <div>{r.isStar && '⭐ '}{r.isGamble && '🔥 '}{r.userId === myUserId ? `${r.displayName} (Bạn)` : r.displayName}</div>
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
              {r.chopLabels && r.chopLabels.length > 0 && (
                <div className="held-items">
                  <div className="held-items-label">{r.chopIsCutter ? 'Chặt:' : 'Bị chặt:'}</div>
                  {r.chopLabels.map((lbl, i) => (
                    <div key={i} className="held-row">
                      <span className="held-chip">🐷 {lbl}</span>
                    </div>
                  ))}
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

function FestivalResultRows({ round, myUserId }: { round: RoundEnd; myUserId: string }) {
  // Winner-first, rồi theo điểm/displayName.
  const rows = [...round.results].sort((a, b) =>
    (b.festivalWinner ? 1 : 0) - (a.festivalWinner ? 1 : 0));
  return (
    <div className="match-end-list festival-list">
      {rows.map(r => (
        <div key={r.userId} className={`match-end-row festival-row ${r.festivalWinner ? 'festival-winner' : ''}`}>
          <span className="rank-tag">{r.festivalWinner ? '🏆' : ''}</span>
          <div className="match-end-name">
            <div>{r.userId === myUserId ? `${r.displayName} (Bạn)` : r.displayName}</div>
            <div className="festival-cards">
              {(r.festivalCards ?? []).map(c => (
                <CardSvg key={`${c.rank}-${c.suit}`} card={cardFromDto(c)} size="sm" />
              ))}
            </div>
            <div className="festival-label">{r.festivalLabel}</div>
          </div>
          <span className={`score-pill ${r.roundScore > 0 ? 'pos' : r.roundScore < 0 ? 'neg' : ''}`}>
            {r.roundScore > 0 ? `+${r.roundScore}` : r.roundScore}
          </span>
          <span className="total-score">Tổng: {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}</span>
        </div>
      ))}
    </div>
  );
}

function XiDachResultRows({ round, myUserId }: { round: RoundEnd; myUserId: string }) {
  // Nhà cái lên đầu, rồi players.
  const rows = [...round.results].sort((a, b) => (b.xiDachIsDealer ? 1 : 0) - (a.xiDachIsDealer ? 1 : 0));
  return (
    <div className="match-end-list festival-list">
      {rows.map(r => (
        <div key={r.userId} className={`match-end-row festival-row ${r.xiDachIsDealer ? 'festival-winner' : ''}`}>
          <span className="rank-tag">{r.xiDachIsDealer ? '🏦' : '🎴'}</span>
          <div className="match-end-name">
            <div>
              {r.xiDachIsDealer && <b>Nhà Cái · </b>}
              {r.userId === myUserId ? `${r.displayName} (Bạn)` : r.displayName}
            </div>
            <div className="festival-cards">
              {(r.xiDachCards ?? []).map((c, i) => (
                <CardSvg key={i} card={cardFromDto(c)} size="sm" />
              ))}
            </div>
            <div className="festival-label">{r.xiDachLabel}</div>
          </div>
          <span className={`score-pill ${r.roundScore > 0 ? 'pos' : r.roundScore < 0 ? 'neg' : ''}`}>
            {r.roundScore > 0 ? `+${r.roundScore}` : r.roundScore}
          </span>
          <span className="total-score">Tổng: {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}</span>
        </div>
      ))}
    </div>
  );
}

const MATH_MEDALS = ['🥇', '🥈', '🥉', '4️⃣'];
function MathResultRows({ round, myUserId }: { round: RoundEnd; myUserId: string }) {
  // Theo hạng (FinalRank 1..4).
  const rows = [...round.results].sort((a, b) => (a.finalRank || 99) - (b.finalRank || 99));
  return (
    <div className="match-end-list festival-list">
      {rows.map(r => (
        <div key={r.userId} className={`match-end-row festival-row ${r.finalRank === 1 ? 'festival-winner' : ''}`}>
          <span className="rank-tag">{MATH_MEDALS[(r.finalRank || 1) - 1] ?? `#${r.finalRank}`}</span>
          <div className="match-end-name">
            <div>{r.userId === myUserId ? `${r.displayName} (Bạn)` : r.displayName}</div>
            <div className="math-result-detail">
              <span className="math-result-correct">🎯 {r.mathCorrectCount}/{r.mathResults?.length ?? 0} đúng</span>
              {/* Thời gian từng câu — câu đúng hiện giây, sai hiện ❌, không trả lời hiện ⏰ */}
              {(r.mathResults ?? []).map((q, i) => (
                <span key={i} className={`math-result-q ${q.correct ? 'ok' : 'no'}`}>
                  C{i + 1}: {q.correct ? `${(q.elapsedMs / 1000).toFixed(1)}s` : (q.answered ? '❌' : '⏰')}
                </span>
              ))}
              {r.mathCorrectCount > 0 && (
                <span className="math-result-total">Σ {(r.mathTotalCorrectMs / 1000).toFixed(1)}s</span>
              )}
            </div>
          </div>
          <span className={`score-pill ${r.roundScore > 0 ? 'pos' : r.roundScore < 0 ? 'neg' : ''}`}>
            {r.roundScore > 0 ? `+${r.roundScore}` : r.roundScore}
          </span>
          <span className="total-score">Tổng: {r.totalScore > 0 ? `+${r.totalScore}` : r.totalScore}</span>
        </div>
      ))}
    </div>
  );
}

// Hàng bài đánh ra giữa bàn. Khi sảnh quá dài (vượt maxWidth khả dụng) thì các lá tự
// ĐÈ LÊN NHAU (negative margin) thay vì tràn ra che mất ghế hai bên. Vẫn xếp 1 hàng,
// không xuống dòng — giữ animation bay vào + đọc được mặt bài (chỉ phần trái mỗi lá bị che).
function TrickCardRow({
  cards, cardSize, cardWidth, maxWidth, className = '',
}: {
  cards: Card[]; cardSize: 'sm' | 'md'; cardWidth: number; maxWidth: number; className?: string;
}) {
  const n = cards.length;
  const gap = 6;
  const naturalWidth = n * cardWidth + (n - 1) * gap;
  // Nếu vượt khung: tính overlap âm để n lá vừa khít maxWidth. Tối đa đè 62% bề rộng lá.
  let overlap = 0;
  if (n > 1 && naturalWidth > maxWidth) {
    overlap = Math.min(cardWidth * 0.62, (naturalWidth - maxWidth) / (n - 1));
  }
  return (
    <div className={`play-card-row ${className}`} style={{ maxWidth }}>
      {cards.map((c, i) => (
        <div
          key={c.id}
          className="play-card-slot"
          style={i > 0 && overlap > 0 ? { marginLeft: -overlap } : undefined}
        >
          <CardSvg card={c} size={cardSize} />
        </div>
      ))}
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
    surrender, startVoteReset, respondVoteReset, scheduleFestival, flipFestivalCard, activateStarOfHope,
    activateXiDach, respondGamble, scheduleBreak, submitRpsChoice, submitMathNumber, submitMathAnswer, submitMemoryAnswer, submitReflexCell, drawXiDachCard, standXiDach, compareXiDach, compareXiDachAll,
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
  // "Qua lượt tự động": khi bật, đến lượt mình mà ĐANG CÓ TRICK thì tự bỏ qua. Mở nước thì dừng chờ mình.
  const [autoPass, setAutoPass] = useState(false);
  const autoPassFiredRef = useRef<string | null>(null);
  const [surrenderConfirmOpen, setSurrenderConfirmOpen] = useState(false);
  const [whiteWinConfirmOpen, setWhiteWinConfirmOpen] = useState(false);
  const [optionsMenuOpen, setOptionsMenuOpen] = useState(false);
  const optionsMenuRef = useRef<HTMLDivElement | null>(null);
  const lastFestivalAnnouncedRef = useRef<string | null>(null);
  const lastStarAnnouncedRef = useRef<string | null>(null);
  const [starConfirmOpen, setStarConfirmOpen] = useState(false);
  const lastXiDachAnnouncedRef = useRef<string | null>(null);
  const [xiDachConfirmOpen, setXiDachConfirmOpen] = useState(false);
  const lastGambleAnnouncedRef = useRef<string | null>(null);
  const lastBreakAnnouncedRef = useRef<string | null>(null);
  // Giải lao Tính toán: client nhớ số mình chọn (pha chọn) + đáp án mình chọn câu hiện tại (server ẩn lúc trả lời).
  const [mathMyPick, setMathMyPick] = useState<number | null>(null);
  const [mathMyChoice, setMathMyChoice] = useState<number | null>(null);
  const mathChoiceQuestionRef = useRef<number>(-1);
  // Giải lao Trí nhớ: client nhớ đáp án mình chọn câu hiện tại (server ẩn lúc trả lời).
  const [memMyChoice, setMemMyChoice] = useState<number | null>(null);
  const memChoiceQuestionRef = useRef<number>(-1);
  // Giải lao Phản xạ: client nhớ ô mình click lượt hiện tại (server ẩn lúc đang chơi).
  const [reflexMyCell, setReflexMyCell] = useState<number | null>(null);
  const reflexRoundRef = useRef<number>(-1);
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
  // Bề rộng tối đa cho hàng bài giữa bàn — chừa chỗ cho ghế trái/phải. Bàn rộng tối đa 1200px
  // (desktop) / full viewport (mobile, ghế dồn lên góc); trừ ~2×130px ghế + đệm.
  const tableW = Math.min(viewportW, isMobile ? viewportW : 1200);
  const trickMaxWidth = isMobile ? tableW - 24 : Math.max(280, tableW - 320);

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
    // Tắt sound pháo hoa khi rời trang (chuyển sang bảng điểm) — tránh phát tiếp sau khi đã sang.
    return () => {
      clearTimeout(t);
      stopSound('fireworkNew');
    };
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

  // Auto-clear roundEnd when next round begins (server auto-advances).
  // FestivalReveal cũng phải clear: round lễ hội mới bắt đầu (pha nặn bài) → modal kết quả ván trước
  // phải biến mất để overlay nặn bài hiện ra (bug: player khác kẹt ở modal phán xử ván trước).
  useEffect(() => {
    if (!roundEnd) return;
    // Ván mới đã bắt đầu (bất kỳ status nào KHÁC WaitingNextRound/Finished) → đóng modal kết quả ván cũ.
    // Bao gồm BreakRps (Giải lao) — bug cũ: thiếu BreakRps → modal kết quả ván trước treo khi host qua ván.
    const s = matchState?.status;
    if (s != null && s !== MatchStatus.WaitingNextRound && s !== MatchStatus.Finished) {
      clearRoundEnd();
    }
  }, [matchState?.status, matchState?.roundNumber, roundEnd, clearRoundEnd]);

  // Trì hoãn hiển thị modal kết quả ván 2s sau khi server gửi RoundEnd — cho pháo hoa của người Nhất
  // bùng lên trước, modal không che màn hình ngay. White-win hiển thị ngay (không có pháo hoa Nhất).
  const [delayedRoundEnd, setDelayedRoundEnd] = useState<RoundEnd | null>(null);
  useEffect(() => {
    if (!roundEnd) { setDelayedRoundEnd(null); return; }
    if (roundEnd.wasWhiteWin || roundEnd.wasFestival || roundEnd.wasXiDach || roundEnd.wasBreak) { setDelayedRoundEnd(roundEnd); return; }
    const t = setTimeout(() => setDelayedRoundEnd(roundEnd), 2000);
    return () => clearTimeout(t);
  }, [roundEnd]);

  // Sound vỗ tay khi modal tổng kết ván vừa hiện ra. Dedup theo round.
  // Về trắng: KHÔNG phát sound (theo yêu cầu). Round thường: clap.
  const lastVictoryRoundRef = useRef<string | null>(null);
  useEffect(() => {
    if (!delayedRoundEnd) return;
    const key = `${delayedRoundEnd.matchId}|${delayedRoundEnd.roundNumber}`;
    if (lastVictoryRoundRef.current === key) return;
    lastVictoryRoundRef.current = key;
    if (!delayedRoundEnd.wasWhiteWin && !delayedRoundEnd.wasBreak) playSound('clapHand', 0.7);
  }, [delayedRoundEnd]);

  // Pháo hoa + victory NGAY khi có người về Nhất (finalRank=1 xuất hiện trong matchState),
  // không cần chờ roundEnd của cả ván. Dùng key (roundNumber + winnerId) để chỉ chạy 1 lần / ván.
  // KHÔNG ăn mừng/cúp C1 trong round lễ hội (Cào Rùa có winner riêng) hoặc khi về trắng
  // (white-win gán finalRank=1 cho NHIỀU người + có confetti/firework riêng → tránh nice-sound lặp).
  const anyWhiteWin = matchState?.players.some(p => p.whiteWinReason != null) ?? false;
  // KHÔNG ăn mừng C1/nice-sound trong round biến tấu (lễ hội / giải lao RPS): chúng gán finalRank=1
  // cho người thắng biến tấu (không phải "về Nhất TLMN") + có màn kết quả riêng → tránh sound lặp.
  const winnerUserId = (matchState && !matchState.isFestivalRound && !matchState.isBreakRound && !anyWhiteWin)
    ? (matchState.players.find(p => p.finalRank === 1)?.userId ?? null)
    : null;
  useEffect(() => {
    if (!matchState || !winnerUserId) return;
    const key = `${matchState.roundNumber}|${winnerUserId}`;
    if (lastCelebratedRoundRef.current === key) return;
    lastCelebratedRoundRef.current = key;
    setWinnerCelebration(winnerUserId);
    playSound('niceSound', 0.7);
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

  const myWhiteWinReason = me?.whiteWinReason ?? null;
  const myWhiteWinAccepted = me?.whiteWinAccepted ?? null;
  const whiteWinLeftSec = matchState?.whiteWinDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.whiteWinDeadline).getTime() - now) / 1000))
    : 0;
  // Rule mới: nút "Về trắng" hiện cho candidate trong trick 1 (chưa qua trick 2), chưa từ chối, còn giờ.
  const canGoWhiteWin = matchState?.status === MatchStatus.InProgress
    && !matchState.pastFirstTrick
    && myWhiteWinReason != null
    && myWhiteWinAccepted !== false
    && whiteWinLeftSec > 0;

  const isPendingTrickCut = matchState?.status === MatchStatus.PendingTrickCut;
  const canCutTrick = isPendingTrickCut && (matchState?.trickCutCandidates ?? []).includes(myUserId);
  const trickCutLeftSec = matchState?.trickCutDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.trickCutDeadline).getTime() - now) / 1000))
    : 0;
  const trickWinnerName = matchState?.players.find(p => p.userId === matchState.pendingTrickWinnerId)?.displayName ?? '';

  // Vote chia bài lại
  const isVoteResetPhase = matchState?.status === MatchStatus.VoteReset;
  const myVoteResetChoice = me?.voteResetChoice ?? null;
  const myHasUsedVoteReset = me?.hasUsedVoteReset ?? false;
  const voteResetYes = matchState?.players.filter(p => p.voteResetChoice === true).length ?? 0;
  const voteResetInitiatorName = matchState?.players.find(p => p.userId === matchState.voteResetInitiatorId)?.displayName ?? '';
  const voteResetLeftSec = matchState?.voteResetDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.voteResetDeadline).getTime() - now) / 1000))
    : 0;
  // Có thể mở vote: đang InProgress, chưa qua trick 1, chưa ai về, chưa dùng quyền.
  const canStartVoteReset = matchState?.status === MatchStatus.InProgress
    && !matchState.pastFirstTrick
    && !matchState.players.some(p => p.finalRank !== null)
    && !myHasUsedVoteReset
    && (me?.finalRank == null);
  // Có thể đầu hàng: đang chơi, chưa có thứ hạng.
  const canSurrender = matchState?.status === MatchStatus.InProgress && me != null && me.finalRank == null;

  // Lễ hội (Cào Rùa): có thể tổ chức nếu đang chơi, chưa ai đặt lịch, chưa phải round lễ hội, chưa dùng quyền.
  const myHasUsedFestival = me?.hasUsedFestival ?? false;
  // 3 chế độ đặc biệt loại trừ lẫn nhau: round sau chỉ 1 cái. Ai đặt trước, người khác mất cả 3 option.
  const noSpecialScheduled = matchState != null
    && !matchState.festivalScheduled
    && !matchState.xiDachScheduledUserId
    && !matchState.starOfHopeScheduledUserId
    && !matchState.gambleScheduledUserId
    && !matchState.isFestivalRound
    && !matchState.isXiDachRound;
  const canScheduleFestival = matchState?.status === MatchStatus.InProgress
    && noSpecialScheduled
    && !myHasUsedFestival;
  const festivalScheduled = matchState?.festivalScheduled ?? false;
  const festivalOrganizerName = matchState?.festivalOrganizerId
    ? matchState.players.find(p => p.userId === matchState.festivalOrganizerId)?.displayName ?? ''
    : '';

  // Ngôi Sao Hi Vọng: kích được nếu đang chơi, chưa ai kích round này, chưa phải round lễ hội, chưa dùng quyền.
  const myHasUsedStar = me?.hasUsedStarOfHope ?? false;
  const starScheduledUserId = matchState?.starOfHopeScheduledUserId ?? null;
  const canActivateStar = matchState?.status === MatchStatus.InProgress
    && noSpecialScheduled
    && !myHasUsedStar;
  const starScheduledName = starScheduledUserId
    ? matchState?.players.find(p => p.userId === starScheduledUserId)?.displayName ?? ''
    : '';

  // Sát Phạt (Xì Dách): kích được nếu đang chơi, chưa ai kích, chưa round đặc biệt, chưa dùng quyền, round sau chưa là lễ hội.
  const myHasUsedXiDach = me?.hasUsedXiDach ?? false;
  const xiDachScheduledUserId = matchState?.xiDachScheduledUserId ?? null;
  const canActivateXiDach = matchState?.status === MatchStatus.InProgress
    && noSpecialScheduled
    && !myHasUsedXiDach;
  const xiDachScheduledName = xiDachScheduledUserId
    ? matchState?.players.find(p => p.userId === xiDachScheduledUserId)?.displayName ?? ''
    : '';

  // Liều Ăn Nhiều: lời mời tự hiện cho NGƯỜI ĐƯỢC MỜI (đủ 5 ván về Nhất liên tiếp). Đồng ý/Từ chối.
  // CHỈ hiện khi ván n+1 ĐÃ deal & đang chơi (InProgress) — không hiện ở màn round-end ván n (WaitingNextRound),
  // tức chỉ sau khi host bấm "Bắt đầu ngay" hoặc timer tự qua ván n+1.
  const gambleOfferUserId = matchState?.gambleOfferUserId ?? null;
  const iAmOfferedGamble = gambleOfferUserId != null && gambleOfferUserId === myUserId
    && matchState?.status === MatchStatus.InProgress;
  const gambleOfferLeftSec = matchState?.gambleOfferDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.gambleOfferDeadline).getTime() - now) / 1000))
    : 0;
  const gambleScheduledUserId = matchState?.gambleScheduledUserId ?? null;
  const gambleScheduledName = gambleScheduledUserId
    ? (gambleScheduledUserId === myUserId ? 'Bạn' : matchState?.players.find(p => p.userId === gambleScheduledUserId)?.displayName ?? '')
    : '';

  // Giải Lao (Oẳn Tù Xì): đặt lịch được nếu đang chơi, chưa biến tấu, chưa dùng quyền, đủ 4 người.
  const myHasUsedBreak = me?.hasUsedBreak ?? false;
  const canScheduleBreak = matchState?.status === MatchStatus.InProgress
    && noSpecialScheduled
    && !myHasUsedBreak
    && (matchState?.players.length === 4);
  const breakScheduled = matchState?.breakScheduled ?? false;
  const breakOrganizerName = matchState?.breakOrganizerId
    ? matchState.players.find(p => p.userId === matchState.breakOrganizerId)?.displayName ?? ''
    : '';
  const isBreakRound = matchState?.status === MatchStatus.BreakRps;
  const rps = matchState?.rps ?? null;
  const rpsLeftSec = matchState?.rpsChoiceDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.rpsChoiceDeadline).getTime() - now) / 1000))
    : 0;
  const rpsRevealActive = matchState?.rpsRevealUntil
    ? new Date(matchState.rpsRevealUntil).getTime() > now
    : false;

  // Giải Lao (Tính toán): pha chọn số / trả lời quiz.
  const isMathRound = matchState?.status === MatchStatus.BreakMathPick || matchState?.status === MatchStatus.BreakMathQuiz;
  const math = matchState?.math ?? null;
  const mathPickLeftSec = matchState?.mathPickDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.mathPickDeadline).getTime() - now) / 1000))
    : 0;
  const mathAnswerLeftSec = matchState?.mathAnswerDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.mathAnswerDeadline).getTime() - now) / 1000))
    : 0;

  // Giải Lao (Trí nhớ): pha xem lưới / trả lời quiz.
  const isMemoryRound = matchState?.status === MatchStatus.BreakMemoryView || matchState?.status === MatchStatus.BreakMemoryQuiz;
  const memory = matchState?.memory ?? null;
  const memViewLeftSec = matchState?.memoryViewDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.memoryViewDeadline).getTime() - now) / 1000))
    : 0;
  const memAnswerLeftSec = matchState?.memoryAnswerDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.memoryAnswerDeadline).getTime() - now) / 1000))
    : 0;

  // Giải Lao (Phản xạ): pha cooldown / click.
  const isReflexRound = matchState?.status === MatchStatus.BreakReflexCooldown || matchState?.status === MatchStatus.BreakReflexPlay;
  const reflex = matchState?.reflex ?? null;
  const reflexCooldownLeftSec = matchState?.reflexCooldownUntil
    ? Math.max(0, Math.ceil((new Date(matchState.reflexCooldownUntil).getTime() - now) / 1000))
    : 0;
  const reflexAnswerLeftSec = matchState?.reflexAnswerDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.reflexAnswerDeadline).getTime() - now) / 1000))
    : 0;

  // Round Sát Phạt đang diễn ra (rút bài hoặc so điểm).
  const isXiDachRound = matchState?.isXiDachRound ?? false;
  const isXiDachPlaying = matchState?.status === MatchStatus.XiDachPlaying;
  const isXiDachCompare = matchState?.status === MatchStatus.XiDachCompare;
  const xiDachDealerId = matchState?.xiDachDealerId ?? null;
  const xiDachDealerName = xiDachDealerId === myUserId
    ? 'Bạn'
    : matchState?.players.find(p => p.userId === xiDachDealerId)?.displayName ?? '';
  const iAmDealer = xiDachDealerId === myUserId;
  const xiDachTurnUserId = matchState?.xiDachTurnUserId ?? null;
  const isMyXiDachTurn = isXiDachPlaying && xiDachTurnUserId === myUserId;
  const xiDachTurnName = xiDachTurnUserId
    ? (xiDachTurnUserId === myUserId ? 'Bạn' : matchState?.players.find(p => p.userId === xiDachTurnUserId)?.displayName ?? '')
    : '';
  const xiDachTurnLeftSec = matchState?.xiDachTurnDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.xiDachTurnDeadline).getTime() - now) / 1000))
    : 0;
  // Tổng điểm tay MÌNH (từ private hand) — để biết được rút/dừng.
  const myXiDachTotal = isXiDachRound ? xiDachHandTotal(myHand) : 0;
  const myXiDachCount = myHand.length;
  // Được dừng: đạt ngưỡng (nhà cái 15, player 16) và chưa quắc; HOẶC đã đủ 5 lá / đã quắc (không rút được nữa).
  const myCanStand = isMyXiDachTurn
    && ((myXiDachTotal <= 21 && myXiDachTotal >= (iAmDealer ? 15 : 16))
        || myXiDachCount >= 5
        || myXiDachTotal > 21);
  const myMustDraw = isMyXiDachTurn && myXiDachTotal < (iAmDealer ? 15 : 16) && myXiDachCount < 5;
  const myCanDraw = isMyXiDachTurn && myXiDachCount < 5 && myXiDachTotal <= 21;
  // Nhà cái được "Xét bài" (sớm hoặc pha so) khi đã đạt ≥15 điểm (hoặc đã quắc/đang pha so).
  const dealerCanCompare = iAmDealer && isXiDachRound
    && (isXiDachCompare || myXiDachTotal >= 15 || myXiDachTotal > 21);
  // Player đã "xong" (dừng/đặc biệt/đền/quắc) → nhà cái xét sớm được.
  const playerXiDachDone = (p: MatchPlayerPublic): boolean => {
    if (isXiDachCompare) return true;
    if (p.xiDachStood || p.xiDachSettled) return true;
    if (p.xiDachRevealed) return true;
    return false;
  };
  // Còn ai chưa xét (để hiện nút "Xét hết").
  const anyUnsettledXiDach = isXiDachRound && (matchState?.players.some(p => !p.isXiDachDealer && !p.xiDachSettled) ?? false);

  // Pha nặn bài lễ hội (FestivalReveal) — hiện bài Cào Rùa NGAY TẠI SEAT mỗi người (không modal).
  const isFestivalReveal = matchState?.status === MatchStatus.FestivalReveal;
  const myFestivalRevealed = me?.festivalRevealed ?? 0;
  const myAllRevealed = me != null && myFestivalRevealed >= 3;
  const festivalRevealLeftSec = matchState?.festivalRevealDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.festivalRevealDeadline).getTime() - now) / 1000))
    : 0;
  // Cooldown 60s buộc phải lật bài (auto-lật khi hết giờ).
  const festivalAutoFlipLeftSec = matchState?.festivalAutoFlipDeadline
    ? Math.max(0, Math.ceil((new Date(matchState.festivalAutoFlipDeadline).getTime() - now) / 1000))
    : 0;
  // map userId → 3 slot (Card đã lật | null chưa lật) cho hiển thị tại seat.
  const festivalSeatCards: Record<string, Array<Card | null>> = {};
  if (isFestivalReveal && matchState) {
    for (const p of matchState.players) {
      const slots = p.festivalCardSlots ?? [];
      festivalSeatCards[p.userId] = [0, 1, 2].map(i => {
        const c = slots[i];
        return c ? cardFromDto(c) : null;
      });
    }
  }

  // Auto-pass: đến lượt mình + đang có trick (không phải mở nước) + bật autoPass → tự bỏ qua một lần.
  useEffect(() => {
    if (!autoPass || !isMyTurn || trickCombo === null) {
      // Reset cờ khi rời lượt để lần tới vào lượt lại bắn được.
      if (!isMyTurn) autoPassFiredRef.current = null;
      return;
    }
    const fireKey = `${matchState?.roundNumber}|${matchState?.currentTurnSeatIndex}|${trick.map(c => c.id).join(',')}`;
    if (autoPassFiredRef.current === fireKey) return;
    autoPassFiredRef.current = fireKey;
    passTurn().catch(() => undefined);
  }, [autoPass, isMyTurn, trickCombo, matchState?.roundNumber, matchState?.currentTurnSeatIndex]);

  // Giải lao Tính toán: reset lựa chọn đáp án của mình khi sang CÂU MỚI (currentQuestion đổi) hoặc rời round.
  useEffect(() => {
    const q = matchState?.math?.currentQuestion ?? -1;
    if (mathChoiceQuestionRef.current !== q) {
      mathChoiceQuestionRef.current = q;
      setMathMyChoice(null);
    }
  }, [matchState?.math?.currentQuestion, matchState?.status]);

  // Reset số đã chọn khi KHÔNG còn ở round Tính toán (sang round khác / ván mới).
  useEffect(() => {
    if (matchState?.status !== MatchStatus.BreakMathPick && matchState?.status !== MatchStatus.BreakMathQuiz) {
      setMathMyPick(null);
    }
  }, [matchState?.status]);

  // Giải lao Trí nhớ: reset lựa chọn của mình khi sang CÂU MỚI (currentQuestion đổi).
  useEffect(() => {
    const q = matchState?.memory?.currentQuestion ?? -1;
    if (memChoiceQuestionRef.current !== q) {
      memChoiceQuestionRef.current = q;
      setMemMyChoice(null);
    }
  }, [matchState?.memory?.currentQuestion, matchState?.status]);

  // Giải lao Phản xạ: reset ô đã click khi sang LƯỢT MỚI (currentRound đổi).
  useEffect(() => {
    const r = matchState?.reflex?.currentRound ?? -1;
    if (reflexRoundRef.current !== r) {
      reflexRoundRef.current = r;
      setReflexMyCell(null);
    }
  }, [matchState?.reflex?.currentRound, matchState?.status]);

  // Thông báo (mọi người) khi có người tổ chức lễ hội — 1 lần / lượt đặt lịch.
  useEffect(() => {
    if (!festivalScheduled || matchState?.isFestivalRound) return;
    const key = `${matchState?.roundNumber}|${matchState?.festivalOrganizerId ?? ''}`;
    if (lastFestivalAnnouncedRef.current === key) return;
    lastFestivalAnnouncedRef.current = key;
    const who = matchState?.festivalOrganizerId === myUserId ? 'Bạn' : festivalOrganizerName;
    toast.push('info', `🎉 ${who} đã tổ chức lễ hội — round sau chơi Cào Rùa!`);
  }, [festivalScheduled, matchState?.festivalOrganizerId, matchState?.roundNumber, matchState?.isFestivalRound]);

  // Thông báo (mọi người) khi có người kích Ngôi Sao Hi Vọng cho round sau — 1 lần / lượt kích.
  useEffect(() => {
    if (!starScheduledUserId) return;
    const key = `${matchState?.roundNumber}|${starScheduledUserId}`;
    if (lastStarAnnouncedRef.current === key) return;
    lastStarAnnouncedRef.current = key;
    const who = starScheduledUserId === myUserId ? 'Bạn' : starScheduledName;
    toast.push('info', `⭐ ${who} đã kích Ngôi Sao Hi Vọng — round sau điểm giao dịch với ${starScheduledUserId === myUserId ? 'bạn' : 'họ'} sẽ ×2!`);
  }, [starScheduledUserId, matchState?.roundNumber]);

  // Thông báo (mọi người) khi có người tổ chức Sát Phạt — round sau là Xì Dách, người đó làm Nhà Cái.
  useEffect(() => {
    if (!xiDachScheduledUserId) return;
    const key = `${matchState?.roundNumber}|${xiDachScheduledUserId}`;
    if (lastXiDachAnnouncedRef.current === key) return;
    lastXiDachAnnouncedRef.current = key;
    const who = xiDachScheduledUserId === myUserId ? 'Bạn' : xiDachScheduledName;
    toast.push('info', `🃏 ${who} đã tổ chức Sát Phạt — round sau chơi Xì Dách, ${xiDachScheduledUserId === myUserId ? 'bạn' : 'họ'} làm Nhà Cái!`);
  }, [xiDachScheduledUserId, matchState?.roundNumber]);

  // Thông báo (mọi người) khi có người ĐỒNG Ý liều ăn nhiều — round sau người đó liều (×2 +6 / ×2).
  useEffect(() => {
    if (!gambleScheduledUserId) return;
    const key = `${matchState?.roundNumber}|${gambleScheduledUserId}`;
    if (lastGambleAnnouncedRef.current === key) return;
    lastGambleAnnouncedRef.current = key;
    const who = gambleScheduledUserId === myUserId ? 'Bạn' : gambleScheduledName;
    toast.push('info', `🔥 ${who} quyết định LIỀU ĂN NHIỀU — round sau điểm thắng/thua của ${gambleScheduledUserId === myUserId ? 'bạn' : 'họ'} ×3!`);
  }, [gambleScheduledUserId, matchState?.roundNumber]);

  // Thông báo (mọi người) khi có người tổ chức Giải lao — round sau là Oẳn Tù Xì.
  useEffect(() => {
    if (!breakScheduled) return;
    const key = `${matchState?.roundNumber}|${matchState?.breakOrganizerId ?? ''}`;
    if (lastBreakAnnouncedRef.current === key) return;
    lastBreakAnnouncedRef.current = key;
    const who = matchState?.breakOrganizerId === myUserId ? 'Bạn' : breakOrganizerName;
    const gameLabel = matchState?.breakScheduledType === 2 ? 'Tính toán'
      : matchState?.breakScheduledType === 3 ? 'Trí nhớ'
      : matchState?.breakScheduledType === 4 ? 'Phản xạ'
      : 'Oẳn Tù Xì';
    toast.push('info', `🎮 ${who} đã tổ chức Giải lao zui zẻ — round sau chơi ${gameLabel}!`);
  }, [breakScheduled, matchState?.breakOrganizerId, matchState?.roundNumber, matchState?.breakScheduledType]);

  // Đóng menu "Tùy chọn" khi bấm ra ngoài.
  useEffect(() => {
    if (!optionsMenuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (optionsMenuRef.current && !optionsMenuRef.current.contains(e.target as Node)) {
        setOptionsMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [optionsMenuOpen]);

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
    setWhiteWinConfirmOpen(false);
    try { await respondWhiteWin(true); }
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

  async function handleSurrender() {
    setSurrenderConfirmOpen(false);
    try { await surrender(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleStartVoteReset() {
    try { await startVoteReset(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleVoteReset(accept: boolean) {
    try { await respondVoteReset(accept); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleScheduleFestival() {
    // Toast thông báo do effect festivalScheduled lo (cho cả mọi người), tránh double-toast ở đây.
    try { await scheduleFestival(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleActivateStar() {
    setStarConfirmOpen(false);
    // Toast thông báo do effect starScheduledUserId lo (cho cả mọi người), tránh double-toast ở đây.
    try { await activateStarOfHope(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleActivateXiDach() {
    setXiDachConfirmOpen(false);
    try { await activateXiDach(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleRespondGamble(accept: boolean) {
    // Toast (cho mọi người) khi accept do effect gambleScheduledUserId lo; ở đây chỉ gọi.
    try { await respondGamble(accept); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleScheduleBreak() {
    // Game do server random chọn từ pool (không truyền gameType nữa).
    try { await scheduleBreak(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleRps(choice: number) {
    try { await submitRpsChoice(choice); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleMathPick(n: number) {
    setMathMyPick(n);
    try { await submitMathNumber(n); }
    catch (e) { setMathMyPick(null); toast.push('error', (e as Error).message); }
  }

  async function handleMathAnswer(optionIndex: number) {
    setMathMyChoice(optionIndex);
    mathChoiceQuestionRef.current = matchState?.math?.currentQuestion ?? -1;
    try { await submitMathAnswer(optionIndex); }
    catch (e) { setMathMyChoice(null); toast.push('error', (e as Error).message); }
  }

  async function handleMemoryAnswer(optionIndex: number) {
    setMemMyChoice(optionIndex);
    memChoiceQuestionRef.current = matchState?.memory?.currentQuestion ?? -1;
    try { await submitMemoryAnswer(optionIndex); }
    catch (e) { setMemMyChoice(null); toast.push('error', (e as Error).message); }
  }

  async function handleReflexPick(cellIndex: number) {
    setReflexMyCell(cellIndex);
    reflexRoundRef.current = matchState?.reflex?.currentRound ?? -1;
    try { await submitReflexCell(cellIndex); }
    catch (e) { setReflexMyCell(null); toast.push('error', (e as Error).message); }
  }

  async function handleDrawXiDach() {
    try { await drawXiDachCard(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleStandXiDach() {
    try { await standXiDach(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleCompareXiDach(targetUserId: string) {
    try { await compareXiDach(targetUserId); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleCompareXiDachAll() {
    try { await compareXiDachAll(); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

  async function handleFlipFestival(flipAll: boolean, cardIndex: number) {
    try { await flipFestivalCard(flipAll, cardIndex); }
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
            const isTurn = (matchState.players[matchState.currentTurnSeatIndex]?.userId === player.userId
                && matchState.status === MatchStatus.InProgress)
              || (isXiDachPlaying && xiDachTurnUserId === player.userId);
            const isMe = player.userId === myUserId;
            const bubble = seatBubbles[player.userId];
            const isStar = player.isStarOfHope;
            const isGambling = player.isGambling;
            const streak = player.winStreak ?? 0;
            return (
              <div key={player.userId} className={`tlmn-seat tlmn-seat-${position} ${isTurn ? 'is-turn' : ''} ${isStar ? 'is-star' : ''} ${isGambling ? 'is-gambling' : ''}`}>
                {bubble && <div key={bubble.id} className="seat-chat-bubble">{bubble.text}</div>}
                {streak > 0 && (
                  <div className="seat-streak-badge" title={`Thắng ${streak} ván liên tiếp`}>
                    {streak <= 5 ? '🏆'.repeat(streak) : <>{streak} × 🏆</>}
                  </div>
                )}
                {isStar && <div className="seat-star-badge" title="Ngôi Sao Hi Vọng — điểm giao dịch ×2">⭐</div>}
                {isGambling && <div className="seat-gamble-badge" title="Liều Ăn Nhiều — điểm thắng/thua ×3, mất quyền đi đầu">🔥</div>}
                <div
                  className="tlmn-avatar"
                  onClick={() => {
                    const snd = avatarClickSound(player.displayName);
                    if (snd) playSound(snd, STICKER_VOLUME);
                  }}
                  style={avatarClickSound(player.displayName) ? { cursor: 'pointer' } : undefined}
                >
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
                    <span>🂠 {(isMe || matchState.showOpponentCardCount) ? player.cardsLeft : ''}</span>
                    <span className={`score-pill ${player.totalScore > 0 ? 'pos' : player.totalScore < 0 ? 'neg' : ''}`}>
                      {player.totalScore > 0 ? `+${player.totalScore}` : player.totalScore}
                    </span>
                    {player.finalRank && (
                      <span className="rank-tag-mini">{RANK_LABEL[player.finalRank] || `#${player.finalRank}`}</span>
                    )}
                  </div>
                </div>
                {isTurn && !isXiDachRound && (
                  <div className={`tlmn-seat-timer ${turnLeftSec <= 10 ? 'low' : ''}`}>
                    ⏱ {turnLeftSec}s
                  </div>
                )}
                {player.passedThisTrick && !player.finalRank && (
                  <div className="tlmn-seat-pass">BỎ LƯỢT</div>
                )}
                {player.surrendered && (
                  <div className="tlmn-seat-pass surrendered">🏳️ ĐẦU HÀNG</div>
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
                  <>
                    <div className="seat-fireworks" aria-hidden="true">
                      <span className="fw fw-1">🎆</span>
                      <span className="fw fw-2">🎇</span>
                      <span className="fw fw-3">✨</span>
                      <span className="fw fw-4">🎉</span>
                    </div>
                    <div className="seat-champion" aria-hidden="true">
                      <ChampionTrophy size={84} />
                      <div className="seat-champion-caption">Vô địch!</div>
                    </div>
                  </>
                )}
                {festivalSeatCards[player.userId] && (
                  <div className={`seat-festival-cards ${isMe ? 'is-me' : ''}`}>
                    {festivalSeatCards[player.userId].map((slot, i) => (
                      <div
                        key={i}
                        className={`festival-card-slot ${slot ? 'flipped' : ''} ${isMe && !slot ? 'flippable' : ''}`}
                        onClick={isMe && !slot ? () => handleFlipFestival(false, i) : undefined}
                        title={isMe && !slot ? 'Nhấn để lật lá này' : undefined}
                      >
                        <CardSvg card={slot ?? undefined} faceDown={!slot} size="sm" />
                      </div>
                    ))}
                    {isMe && !myAllRevealed && (
                      <button className="festival-flip-all-btn" onClick={() => handleFlipFestival(true, -1)}>
                        Lật hết
                      </button>
                    )}
                  </div>
                )}
                {isXiDachRound && (
                  <div className="seat-xidach">
                    {player.isXiDachDealer && <div className="seat-xidach-dealer">🏦 NHÀ CÁI</div>}
                    <div className="seat-xidach-cards">
                      {(isMe ? myHand : (player.xiDachVisibleCards ? player.xiDachVisibleCards.map(cardFromDto) : [])).map((c, i) => (
                        <CardSvg key={i} card={c} size="sm" />
                      ))}
                      {/* Đối thủ chưa lật → hiện lưng bài theo số lá */}
                      {!isMe && !player.xiDachRevealed && Array.from({ length: player.cardsLeft }).map((_, i) => (
                        <CardSvg key={`b${i}`} faceDown size="sm" />
                      ))}
                    </div>
                    <div className="seat-xidach-meta">
                      {isMe
                        ? <span className="seat-xidach-total">{xiDachHandLabel(myHand)} · {myXiDachCount} lá</span>
                        : player.xiDachRevealed && player.xiDachVisibleCards
                          ? <span className="seat-xidach-total">{xiDachHandLabel(player.xiDachVisibleCards.map(cardFromDto))}</span>
                          : <span className="muted">{player.cardsLeft} lá</span>}
                      {player.xiDachStood && !player.xiDachSettled && <span className="seat-xidach-stood">DỪNG</span>}
                      {player.xiDachSettled && <span className="seat-xidach-settled">✓ đã xét</span>}
                    </div>
                    {/* Nhà cái xét bài từng player: pha so, hoặc sớm khi player đã xong + nhà cái ≥15. */}
                    {dealerCanCompare && !player.isXiDachDealer && !player.xiDachSettled && playerXiDachDone(player) && (
                      <button className="tlmn-btn primary seat-xidach-compare" onClick={() => handleCompareXiDach(player.userId)}>
                        Xét bài
                      </button>
                    )}
                  </div>
                )}
              </div>
            );
          })}

          <div className="play-area-cards">
            {isXiDachRound ? (
              <div className="festival-reveal-center">
                <div className="festival-reveal-title">🃏 Sát Phạt của {xiDachDealerName || '?'}</div>
                {/* Bộ bài giữa bàn: click để rút (chỉ khi tới lượt mình + được rút). */}
                <button
                  className={`xidach-deck ${myCanDraw ? 'drawable' : ''}`}
                  onClick={myCanDraw ? handleDrawXiDach : undefined}
                  disabled={!myCanDraw}
                  title={myCanDraw ? 'Nhấn để rút 1 lá' : ''}
                >
                  <CardSvg faceDown size={cardSize} />
                  {myCanDraw && <span className="xidach-deck-hint">Rút bài</span>}
                </button>
                <div className="festival-reveal-status">
                  {isXiDachCompare
                    ? (iAmDealer ? <>Xét bài từng người (hoặc “Xét hết”)</> : <>Nhà cái đang xét bài…</>)
                    : isMyXiDachTurn
                      ? <>Lượt của <b>bạn</b> ({xiDachTurnLeftSec}s)</>
                      : <>Đang chờ <b>{xiDachTurnName || '...'}</b>… ({xiDachTurnLeftSec}s)</>}
                </div>
                {dealerCanCompare && anyUnsettledXiDach && (
                  <button className="tlmn-btn primary xidach-compare-all" onClick={handleCompareXiDachAll}>
                    ⚖️ Xét hết
                  </button>
                )}
              </div>
            ) : isFestivalReveal ? (
              <div className="festival-reveal-center" aria-hidden="true">
                <div className="festival-reveal-title">🎉 Lễ hội của {festivalOrganizerName || '?'}</div>
                <div className="festival-reveal-status">
                  {myAllRevealed
                    ? (festivalRevealLeftSec > 0
                        ? <>Mọi người đang lật… kết quả sau <b>{festivalRevealLeftSec}s</b></>
                        : <>Chờ mọi người lật hết…</>)
                    : <>Nhấn từng lá bài của bạn để nặn ({myFestivalRevealed}/3)<br/>tự lật sau <b>{festivalAutoFlipLeftSec}s</b></>}
                </div>
              </div>
            ) : trick.length === 0 ? (
              lastWonTrick.length > 0 ? (
                <div className="play-won-trick">
                  <TrickCardRow cards={lastWonTrick} cardSize={cardSize} cardWidth={cardWidth} maxWidth={trickMaxWidth} className="play-card-row-faded" />
                  <div className="play-won-trick-label muted">
                    {lastWonTrickWinnerName ? `${lastWonTrickWinnerName} thắng vòng` : 'Thắng vòng'} · mở nước mới
                  </div>
                </div>
              ) : (
                <div className="play-empty muted">Mở nước mới</div>
              )
            ) : (
              <TrickCardRow key={flyKey} cards={trick} cardSize={cardSize} cardWidth={cardWidth} maxWidth={trickMaxWidth} className={`fly-from-${flyDirection}`} />
            )}
          </div>
        </div>

        <div className="my-hand-area" ref={handAreaRef}>
          {isXiDachRound ? (
            null
          ) : isFestivalReveal ? (
            <div className="muted">🎉 Nặn bài Cào Rùa tại chỗ ngồi của bạn phía trên</div>
          ) : myHand.length === 0 ? (
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
          {isXiDachRound ? (
            isMyXiDachTurn ? (
              <>
                <button
                  className="tlmn-btn ghost"
                  disabled={!(myCanStand || myXiDachTotal > 21)}
                  onClick={handleStandXiDach}
                  title={(myCanStand || myXiDachTotal > 21) ? 'Dừng & chốt tay' : `Phải đạt ${iAmDealer ? 15 : 16} điểm mới được dừng`}
                >
                  ✋ Dừng ({myXiDachTotal})
                </button>
                {myMustDraw && <span className="muted" style={{ alignSelf: 'center' }}>Chưa đủ điểm — nhấn bộ bài giữa bàn để rút</span>}
              </>
            ) : (
              <span className="muted" style={{ alignSelf: 'center' }}>
                {isXiDachCompare
                  ? (iAmDealer ? 'Nhấn “Xét bài” ở từng người' : 'Nhà cái đang xét bài…')
                  : `Đang chờ ${xiDachTurnName || '...'} rút bài…`}
              </span>
            )
          ) : (
          <>
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
          {canGoWhiteWin && (
            <button
              className="tlmn-btn white-win-btn"
              onClick={() => setWhiteWinConfirmOpen(true)}
              title={`Bạn có ${myWhiteWinReason} — về trắng thắng ngay (chỉ trong vòng đầu, còn ${whiteWinLeftSec}s)`}
            >
              🌟 Về trắng ({whiteWinLeftSec}s)
            </button>
          )}
          <button
            className={`tlmn-btn ghost ${autoPass ? 'auto-pass-on' : ''}`}
            onClick={() => setAutoPass(v => !v)}
            title={autoPass ? 'Đang tự bỏ qua khi có nước trên bàn — bấm để tắt' : 'Tự động bỏ qua lượt khi có nước trên bàn (mở nước thì dừng chờ bạn)'}
          >
            {autoPass ? '⏸ Tắt qua lượt tự động' : '⏩ Qua lượt tự động'}
          </button>
          </>
          )}
          {(canStartVoteReset || canSurrender || canScheduleFestival || canActivateStar || canActivateXiDach || canScheduleBreak) && (
            <div className="tlmn-options" ref={optionsMenuRef}>
              <button
                className={`tlmn-btn ghost ${optionsMenuOpen ? 'auto-pass-on' : ''}`}
                onClick={() => setOptionsMenuOpen(o => !o)}
                title="Tùy chọn: vote bỏ bài / đầu hàng / tổ chức lễ hội / Ngôi Sao Hi Vọng / Sát Phạt"
              >
                ⋯ Tùy chọn
              </button>
              {optionsMenuOpen && (
                <div className="tlmn-options-menu">
                  {canStartVoteReset && (
                    <button
                      className="tlmn-options-item"
                      onClick={() => { setOptionsMenuOpen(false); handleStartVoteReset(); }}
                    >
                      🔄 Vote bỏ bài
                    </button>
                  )}
                  {canScheduleFestival && (
                    <button
                      className="tlmn-options-item"
                      onClick={() => { setOptionsMenuOpen(false); handleScheduleFestival(); }}
                    >
                      🎉 Tổ chức lễ hội
                    </button>
                  )}
                  {canActivateStar && (
                    <button
                      className="tlmn-options-item star"
                      onClick={() => { setOptionsMenuOpen(false); setStarConfirmOpen(true); }}
                    >
                      ⭐ Ngôi Sao Hi Vọng
                    </button>
                  )}
                  {canActivateXiDach && (
                    <button
                      className="tlmn-options-item"
                      onClick={() => { setOptionsMenuOpen(false); setXiDachConfirmOpen(true); }}
                    >
                      🃏 Tổ chức Sát Phạt
                    </button>
                  )}
                  {canScheduleBreak && (
                    <button
                      className="tlmn-options-item"
                      onClick={() => { setOptionsMenuOpen(false); handleScheduleBreak(); }}
                      title="Random 1 game giải lao (Oẳn Tù Xì / Tính toán / Trí nhớ) — chơi rồi không lặp lại"
                    >
                      🎮 Giải lao zui zẻ
                    </button>
                  )}
                  {canSurrender && (
                    <button
                      className="tlmn-options-item danger"
                      onClick={() => { setOptionsMenuOpen(false); setSurrenderConfirmOpen(true); }}
                    >
                      🏳️ Đầu hàng
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>

        {whiteWinConfirmOpen && canGoWhiteWin && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.45)' }} onClick={() => setWhiteWinConfirmOpen(false)}>
            <div className="match-end-card" onClick={e => e.stopPropagation()}>
              <h2>🌟 Về trắng?</h2>
              <div className="next-round-countdown">
                Bạn có <b>{myWhiteWinReason}</b>. Về trắng để <b>thắng ngay</b> ván này? Còn <b>{whiteWinLeftSec}s</b> (chỉ trong vòng đầu).
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn primary" onClick={handleAcceptWhiteWin}>🌟 Về trắng ngay</button>
                <button className="tlmn-btn ghost" onClick={() => setWhiteWinConfirmOpen(false)}>Để sau</button>
              </div>
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

        {surrenderConfirmOpen && canSurrender && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.45)' }} onClick={() => setSurrenderConfirmOpen(false)}>
            <div className="match-end-card" onClick={e => e.stopPropagation()}>
              <h2>🏳️ Đầu hàng ván này?</h2>
              <div className="next-round-countdown">
                Bạn sẽ <b>về chót</b> và bị trừ điểm hàng còn giữ (heo, tứ quý, 3/4 đôi thông…). Ván vẫn tiếp tục cho người khác.
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn ghost danger" onClick={handleSurrender}>🏳️ Đồng ý đầu hàng</button>
                <button className="tlmn-btn primary" onClick={() => setSurrenderConfirmOpen(false)}>Bỏ</button>
              </div>
            </div>
          </div>
        )}

        {starConfirmOpen && canActivateStar && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.45)' }} onClick={() => setStarConfirmOpen(false)}>
            <div className="match-end-card" onClick={e => e.stopPropagation()}>
              <h2>⭐ Ngôi Sao Hi Vọng?</h2>
              <div className="next-round-countdown">
                Kích cho <b>round kế tiếp</b>: mọi điểm bạn <b>thắng/thua</b> với từng người sẽ được <b>×2</b> (cả 2 chiều).
                Mỗi trận chỉ dùng <b>1 lần</b> — dùng rồi mất quyền vĩnh viễn.
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn primary" onClick={handleActivateStar}>⭐ Kích ngay</button>
                <button className="tlmn-btn ghost" onClick={() => setStarConfirmOpen(false)}>Để sau</button>
              </div>
            </div>
          </div>
        )}

        {xiDachConfirmOpen && canActivateXiDach && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.45)' }} onClick={() => setXiDachConfirmOpen(false)}>
            <div className="match-end-card" onClick={e => e.stopPropagation()}>
              <h2>🃏 Tổ chức Sát Phạt?</h2>
              <div className="next-round-countdown">
                <b>Round kế tiếp</b> chơi <b>Xì Dách</b>, bạn làm <b>Nhà Cái</b>. Mỗi trận chỉ dùng <b>1 lần</b> — dùng rồi mất quyền vĩnh viễn.
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn primary" onClick={handleActivateXiDach}>🃏 Tổ chức ngay</button>
                <button className="tlmn-btn ghost" onClick={() => setXiDachConfirmOpen(false)}>Để sau</button>
              </div>
            </div>
          </div>
        )}

        {isBreakRound && rps && (
          <RpsBreakScreen
            rps={rps}
            players={matchState.players}
            myUserId={myUserId}
            leftSec={rpsLeftSec}
            revealActive={rpsRevealActive}
            onChoose={handleRps}
          />
        )}

        {isMathRound && math && (
          <MathBreakScreen
            math={math}
            players={matchState.players}
            myUserId={myUserId}
            pickLeftSec={mathPickLeftSec}
            answerLeftSec={mathAnswerLeftSec}
            myPick={mathMyPick}
            myChoiceIdx={mathMyChoice}
            onPickNumber={handleMathPick}
            onAnswer={handleMathAnswer}
          />
        )}

        {isMemoryRound && memory && (
          <MemoryBreakScreen
            memory={memory}
            players={matchState.players}
            myUserId={myUserId}
            viewLeftSec={memViewLeftSec}
            answerLeftSec={memAnswerLeftSec}
            myChoiceIdx={memMyChoice}
            onAnswer={handleMemoryAnswer}
          />
        )}

        {isReflexRound && reflex && (
          <ReflexBreakScreen
            reflex={reflex}
            players={matchState.players}
            myUserId={myUserId}
            cooldownLeftSec={reflexCooldownLeftSec}
            answerLeftSec={reflexAnswerLeftSec}
            myCellIdx={reflexMyCell}
            onPick={handleReflexPick}
          />
        )}

        {isXiDachRound && isMobile && (
          <XiDachMobilePanel
            players={matchState.players}
            myUserId={myUserId}
            dealerName={xiDachDealerName}
            isCompare={isXiDachCompare}
            iAmDealer={iAmDealer}
            isMyTurn={isMyXiDachTurn}
            turnName={xiDachTurnName}
            turnLeftSec={xiDachTurnLeftSec}
            myHand={myHand}
            myLabel={xiDachHandLabel(myHand)}
            myCount={myXiDachCount}
            myCanDraw={myCanDraw}
            myCanStand={myCanStand}
            myTotal={myXiDachTotal}
            dealerCanCompare={dealerCanCompare}
            anyUnsettled={anyUnsettledXiDach}
            playerDone={playerXiDachDone}
            handLabelOf={xiDachHandLabel}
            onDraw={handleDrawXiDach}
            onStand={handleStandXiDach}
            onCompare={handleCompareXiDach}
            onCompareAll={handleCompareXiDachAll}
          />
        )}

        {iAmOfferedGamble && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.55)' }}>
            <div className="match-end-card gamble-offer" onClick={e => e.stopPropagation()}>
              <h2>🔥 Bạn đang thắng liên tiếp 5 ván!</h2>
              <div className="next-round-countdown">
                Bạn có muốn <b>liều ăn nhiều</b> ván tiếp theo không?
                <div className="gamble-terms">
                  <div>🔥 Điểm <b>thắng/thua</b> ván sau của bạn <b>×3</b>.</div>
                  <div>⚠️ Đổi lại: nếu bạn <b>về Nhất</b> thì ván reset, người cầm <b>3♠</b> đi đầu.</div>
                  <div>✅ Nếu đồng ý, <b>Liều ăn nhiều</b> sẽ kích hoạt ở <b>ván sau</b>.</div>
                </div>
              </div>
              <div className="match-end-actions">
                <button className="tlmn-btn primary" onClick={() => handleRespondGamble(true)}>🔥 Đồng ý</button>
                <button className="tlmn-btn ghost" onClick={() => handleRespondGamble(false)}>Từ chối ({gambleOfferLeftSec}s)</button>
              </div>
            </div>
          </div>
        )}

        {isVoteResetPhase && (
          <div className="match-end-overlay" style={{ background: 'rgba(0,0,0,0.45)' }}>
            <div className="match-end-card">
              <h2>🔄 Vote chia bài lại</h2>
              <div className="next-round-countdown">
                <b>{voteResetInitiatorName}</b> đề nghị chia bài lại. Cần <b>2</b> phiếu đồng ý.
                {' '}Đã có <b>{voteResetYes}</b> phiếu. <b>{voteResetLeftSec}s</b>
              </div>
              <div className="match-end-list">
                {matchState.players.map(p => (
                  <div key={p.userId} className="match-end-row">
                    <span className="rank-tag">{p.voteResetChoice === true ? '✓' : p.voteResetChoice === false ? '✗' : '…'}</span>
                    <div className="match-end-name">
                      <div>{p.userId === myUserId ? 'Bạn' : p.displayName}</div>
                    </div>
                    <span className="muted small">
                      {p.voteResetChoice === true ? 'Đồng ý'
                        : p.voteResetChoice === false ? 'Bỏ'
                        : '… đang chọn'}
                    </span>
                  </div>
                ))}
              </div>
              {myVoteResetChoice === null ? (
                <div className="match-end-actions">
                  <button className="tlmn-btn primary" onClick={() => handleVoteReset(true)}>✓ Đồng ý chia lại</button>
                  <button className="tlmn-btn ghost" onClick={() => handleVoteReset(false)}>✗ Bỏ</button>
                </div>
              ) : (
                <div className="match-end-actions">
                  <div className="next-round-countdown">
                    {voteResetLeftSec > 0 ? <>Đang chờ người khác bỏ phiếu… <b>{voteResetLeftSec}s</b></> : <>Đang xử lý…</>}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {delayedRoundEnd && !matchEnd && !iAmOfferedGamble && (
          <div className="match-end-overlay">
            {(delayedRoundEnd.wasWhiteWin || delayedRoundEnd.wasFestival || delayedRoundEnd.wasXiDach || delayedRoundEnd.wasBreak) && <Confetti active={true} />}
            <div className="match-end-card">
              <h2>
                {delayedRoundEnd.wasBreak
                  ? (delayedRoundEnd.breakGame === 2
                      ? `🧮 Giải lao Tính toán — Ván ${delayedRoundEnd.roundNumber}`
                      : delayedRoundEnd.breakGame === 3
                      ? `🧠 Giải lao Trí nhớ — Ván ${delayedRoundEnd.roundNumber}`
                      : delayedRoundEnd.breakGame === 4
                      ? `⚡ Giải lao Phản xạ — Ván ${delayedRoundEnd.roundNumber}`
                      : `🎮 Giải lao Oẳn Tù Xì — Ván ${delayedRoundEnd.roundNumber}`)
                  : delayedRoundEnd.wasXiDach
                  ? `🃏 Sát Phạt Xì Dách — Ván ${delayedRoundEnd.roundNumber}`
                  : delayedRoundEnd.wasFestival
                  ? `🎉 Lễ hội Cào Rùa — Ván ${delayedRoundEnd.roundNumber}`
                  : delayedRoundEnd.wasWhiteWin
                  ? '🌟 Có người về trắng!'
                  : delayedRoundEnd.wasJudge
                  ? `⚖️ Phán xử — Ván ${delayedRoundEnd.roundNumber}`
                  : `🎉 Kết quả ván ${delayedRoundEnd.roundNumber}`}
              </h2>
              {delayedRoundEnd.wasXiDach
                ? <XiDachResultRows round={delayedRoundEnd} myUserId={myUserId} />
                : delayedRoundEnd.wasFestival
                ? <FestivalResultRows round={delayedRoundEnd} myUserId={myUserId} />
                : delayedRoundEnd.wasBreak && (delayedRoundEnd.breakGame === 2 || delayedRoundEnd.breakGame === 3 || delayedRoundEnd.breakGame === 4)
                ? <MathResultRows round={delayedRoundEnd} myUserId={myUserId} />
                : <RoundResultRows round={delayedRoundEnd} myUserId={myUserId} />}
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
                    const festWinner = r.results.find(x => x.festivalWinner);
                    const xdDealer = r.results.find(x => x.xiDachIsDealer);
                    const title = r.wasXiDach
                      ? `Ván ${r.roundNumber} · 🃏 Sát Phạt${xdDealer ? ` · Cái ${xdDealer.displayName}` : ''}`
                      : r.wasFestival
                      ? `Ván ${r.roundNumber} · 🎉 Lễ hội${festWinner ? ` · ${festWinner.displayName} ăn` : ''}`
                      : r.wasWhiteWin
                      ? `Ván ${r.roundNumber} · 🌟 Về trắng`
                      : r.wasJudge
                      ? `Ván ${r.roundNumber} · ⚖️ Phán xử`
                      : r.wasBreak
                      ? `Ván ${r.roundNumber} · 🎮 Giải lao ${r.breakGame === 2 ? 'Tính toán' : r.breakGame === 3 ? 'Trí nhớ' : r.breakGame === 4 ? 'Phản xạ' : 'Oẳn Tù Xì'}`
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
                        {r.wasXiDach
                          ? <XiDachResultRows round={r} myUserId={myUserId} />
                          : r.wasFestival
                          ? <FestivalResultRows round={r} myUserId={myUserId} />
                          : r.wasBreak && (r.breakGame === 2 || r.breakGame === 3 || r.breakGame === 4)
                          ? <MathResultRows round={r} myUserId={myUserId} />
                          : <RoundResultRows round={r} myUserId={myUserId} />}
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
