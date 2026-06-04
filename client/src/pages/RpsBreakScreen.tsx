import { useEffect, useMemo, useRef, useState } from 'react';
import type { MatchPlayerPublic, RpsState, RpsMatchup } from '../api';
import { RpsStage } from '../api';
import { Avatar } from '../ui/Avatar';
import './rps-break.css';

// Búa = Rock(1) ✊, Bao = Paper(2) ✋, Kéo = Scissors(3) ✌️ (hiển thị nắm đấm nằm ngang)
const CHOICE_EMOJI: Record<number, string> = { 1: '✊', 2: '✋', 3: '✌️' };
const CHOICE_LABEL: Record<number, string> = { 1: 'Búa', 2: 'Bao', 3: 'Kéo' };
const CHOICES = [1, 2, 3];

const STAGE_TITLE: Record<number, string> = {
  [RpsStage.Round1A]: 'Vòng 1 · Cặp A',
  [RpsStage.Round1B]: 'Vòng 1 · Cặp B',
  [RpsStage.ThirdPlace]: 'Tranh hạng 3',
  [RpsStage.Final]: 'Chung kết',
};

export function RpsBreakScreen({
  rps, players, myUserId, leftSec, revealActive, onChoose,
}: {
  rps: RpsState;
  players: MatchPlayerPublic[];
  myUserId: string;
  leftSec: number;
  revealActive: boolean;   // server đang ở pha hiện kết quả 2s (rpsRevealUntil còn hạn)
  onChoose: (choice: number) => void;
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

  const done = rps.stage === RpsStage.Done;
  const cur: RpsMatchup = rps.stage === RpsStage.Round1A ? rps.round1A
    : rps.stage === RpsStage.Round1B ? rps.round1B
    : rps.stage === RpsStage.ThirdPlace ? rps.thirdPlace
    : rps.final;

  // Pha hiện kết quả (server giữ ~4s): dấu ? LẮC ~0.9s → LẬT hiện kéo/búa/bao (tỉ số GIỮ CŨ) →
  // ~0.8s sau mới NHẢY tỉ số mới ('score'). Kích hoạt khi server vào pha reveal (revealActive) HOẶC khi
  // vừa có kết quả ván mới (chữ ký đổi) — bắt cả 2 để chắc chắn hiển thị dù `now` ticker (1s) trễ.
  const sig = `${rps.stage}|${cur?.winsA ?? 0}|${cur?.winsB ?? 0}|${cur?.lastChoiceA ?? 0}|${cur?.lastChoiceB ?? 0}|${cur?.hasLast ? 1 : 0}`;
  const [phase, setPhase] = useState<'idle' | 'shake' | 'reveal' | 'score'>('idle');
  const lastSigRef = useRef<string>('');
  useEffect(() => {
    if (done) { setPhase('idle'); return; }
    const justResolved = cur?.hasLast && sig !== lastSigRef.current;
    lastSigRef.current = sig;
    if (revealActive || justResolved) {
      setPhase('shake');
      const t1 = setTimeout(() => setPhase('reveal'), 900);  // lắc 0.9s rồi lật
      const t2 = setTimeout(() => setPhase('score'), 1700);  // 0.8s sau khi lật mới nhảy tỉ số
      return () => { clearTimeout(t1); clearTimeout(t2); };
    }
    setPhase('idle');
  }, [sig, revealActive, done]);

  const iAmA = !done && cur.playerAId === myUserId;
  const iAmB = !done && cur.playerBId === myUserId;
  const iPlay = iAmA || iAmB;
  const iChose = iAmA ? cur.aChosen : iAmB ? cur.bChosen : false;

  // ---- Kết quả cuối ----
  if (done) {
    const medals = ['🥇', '🥈', '🥉', '4️⃣'];
    const deltas = [2, 1, -1, -2];
    return (
      <div className="rps-overlay">
        <div className="rps-card">
          <div className="rps-title">🏆 Kết quả Giải lao</div>
          <div className="rps-ranking">
            {rps.finalRanking.map((uid, i) => (
              <div key={uid} className={`rps-rank-row rank-${i + 1}`}>
                <span className="rps-rank-medal">{medals[i]}</span>
                <Avatar name={nameOf[uid] ?? '?'} hasAvatar={avatarOf[uid]} playerId={uid} size="sm" />
                <span className="rps-rank-name">{nameOf[uid] ?? '?'}</span>
                <span className={`rps-rank-delta ${deltas[i] > 0 ? 'pos' : 'neg'}`}>{deltas[i] > 0 ? `+${deltas[i]}` : deltas[i]}</span>
              </div>
            ))}
          </div>
          <div className="rps-hint">Điểm đã cộng vào bảng điểm trận · sắp qua ván tiếp…</div>
        </div>
      </div>
    );
  }

  // Lật bài hiện trong shake/reveal/score. Pha shake: ? lắc; reveal+score: hiện kéo/búa/bao.
  const showLast = phase === 'shake' || phase === 'reveal' || phase === 'score';
  function fistContent(chosen: boolean, lastChoice: number) {
    if (showLast && cur.hasLast) {
      if (phase === 'shake') return <span className="rps-mark shaking">?</span>;
      return <span className="rps-fist revealed">{CHOICE_EMOJI[lastChoice] ?? '?'}</span>;
    }
    return <span className={`rps-mark ${chosen ? 'chosen' : 'waiting'}`}>?</span>;
  }

  // Tỉ số HIỂN THỊ: trong shake/reveal vẫn là tỉ số CŨ (trước ván vừa lật); chỉ NHẢY khi 'score'/'idle'.
  // Server đã +1 vào wins, nên trừ ngược kết quả ván vừa rồi (lastOutcome) để ra tỉ số cũ.
  const beforeScore = phase === 'shake' || phase === 'reveal';
  let dispA = cur.winsA, dispB = cur.winsB;
  if (beforeScore && cur.hasLast) {
    if (cur.lastOutcome === 1) dispA = Math.max(0, cur.winsA - 1);
    else if (cur.lastOutcome === 2) dispB = Math.max(0, cur.winsB - 1);
  }
  const scoreBump = phase === 'score';
  const draw = (phase === 'reveal' || phase === 'score') && cur.lastOutcome === 0;

  return (
    <div className="rps-overlay">
      <div className="rps-card">
        <div className="rps-title">🎮 Oẳn Tù Xì</div>
        <div className="rps-stage">{STAGE_TITLE[rps.stage]} · cán {cur.winTarget} thắng</div>

        {/* Bracket gọn 1 hàng */}
        <div className="rps-bracket">
          <BracketCell m={rps.round1A} label="V1" nameOf={nameOf} active={rps.stage === RpsStage.Round1A} />
          <BracketCell m={rps.round1B} label="V2" nameOf={nameOf} active={rps.stage === RpsStage.Round1B} />
          <BracketCell m={rps.thirdPlace} label="Hạng 3" nameOf={nameOf} active={rps.stage === RpsStage.ThirdPlace} />
          <BracketCell m={rps.final} label="CK" nameOf={nameOf} active={rps.stage === RpsStage.Final} />
        </div>

        {/* Đấu trường: 2 nắm đấm nằm ngang đối nhau */}
        <div className="rps-arena">
          <div className={`rps-side left ${iAmA ? 'me' : ''}`}>
            <Avatar name={nameOf[cur.playerAId] ?? '?'} hasAvatar={avatarOf[cur.playerAId]} playerId={cur.playerAId} size="sm" />
            <div className="rps-side-name">{nameOf[cur.playerAId] ?? '?'}</div>
            {fistContent(cur.aChosen, cur.lastChoiceA)}
          </div>

          <div className="rps-center">
            <div className={`rps-score ${scoreBump ? 'bump' : ''}`}>{dispA} <i>:</i> {dispB}</div>
            <div className="rps-vs">VS</div>
            {draw && <div className="rps-draw">🤝 Hòa — đánh lại!</div>}
          </div>

          <div className={`rps-side right ${iAmB ? 'me' : ''}`}>
            <Avatar name={nameOf[cur.playerBId] ?? '?'} hasAvatar={avatarOf[cur.playerBId]} playerId={cur.playerBId} size="sm" />
            <div className="rps-side-name">{nameOf[cur.playerBId] ?? '?'}</div>
            {fistContent(cur.bChosen, cur.lastChoiceB)}
          </div>
        </div>

        {/* Khu chọn / khán giả */}
        {iPlay ? (
          iChose ? (
            <div className="rps-status">⏳ Đã chọn — chờ đối thủ… <b>{leftSec}s</b></div>
          ) : (
            <div className="rps-pick">
              <div className="rps-pick-label">Chọn đi! <b>{leftSec}s</b></div>
              <div className="rps-pick-row">
                {CHOICES.map(c => (
                  <button key={c} className="rps-pick-btn" onClick={() => onChoose(c)} disabled={showLast}>
                    <span className="rps-pick-emoji">{CHOICE_EMOJI[c]}</span>
                    <span className="rps-pick-cap">{CHOICE_LABEL[c]}</span>
                  </button>
                ))}
              </div>
            </div>
          )
        ) : (
          <div className="rps-status">👀 Xem <b>{nameOf[cur.playerAId]}</b> vs <b>{nameOf[cur.playerBId]}</b> · {leftSec}s</div>
        )}
      </div>
    </div>
  );
}

function BracketCell({ m, label, nameOf, active }: { m: RpsMatchup; label: string; nameOf: Record<string, string>; active: boolean }) {
  const aWin = m.winnerId && m.winnerId === m.playerAId;
  const bWin = m.winnerId && m.winnerId === m.playerBId;
  return (
    <div className={`rps-bcell ${active ? 'active' : ''} ${m.winnerId ? 'done' : ''}`}>
      <div className="rps-blabel">{label}</div>
      <div className={`rps-brow ${aWin ? 'w' : m.winnerId ? 'l' : ''}`}>
        <span>{m.playerAId ? nameOf[m.playerAId] ?? '?' : '—'}</span><b>{m.winsA}</b>
      </div>
      <div className={`rps-brow ${bWin ? 'w' : m.winnerId ? 'l' : ''}`}>
        <span>{m.playerBId ? nameOf[m.playerBId] ?? '?' : '—'}</span><b>{m.winsB}</b>
      </div>
    </div>
  );
}
