import { useMemo } from 'react';
import type { MatchPlayerPublic, RpsState, RpsMatchup } from '../api';
import { RpsStage } from '../api';
import { Avatar } from '../ui/Avatar';
import './rps-break.css';

// Búa = Rock(1) ✊, Bao = Paper(2) ✋, Kéo = Scissors(3) ✌️
const CHOICE_EMOJI: Record<number, string> = { 0: '❔', 1: '✊', 2: '✋', 3: '✌️' };
const CHOICE_LABEL: Record<number, string> = { 1: 'Búa', 2: 'Bao', 3: 'Kéo' };
const CHOICES = [1, 2, 3]; // Búa, Bao, Kéo

const STAGE_TITLE: Record<number, string> = {
  [RpsStage.Round1A]: 'Vòng 1 — Cặp A (Bo3)',
  [RpsStage.Round1B]: 'Vòng 1 — Cặp B (Bo3)',
  [RpsStage.ThirdPlace]: 'Tranh hạng 3 (Bo3)',
  [RpsStage.Final]: 'Chung kết (Bo5)',
  [RpsStage.Done]: 'Kết thúc',
};

export function RpsBreakScreen({
  rps, players, myUserId, leftSec, onChoose,
}: {
  rps: RpsState;
  players: MatchPlayerPublic[];
  myUserId: string;
  leftSec: number;
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

  const iAmA = !done && cur.playerAId === myUserId;
  const iAmB = !done && cur.playerBId === myUserId;
  const iPlay = iAmA || iAmB;
  const iChose = iAmA ? cur.aChosen : iAmB ? cur.bChosen : false;

  function MiniBracket({ m, label }: { m: RpsMatchup; label: string }) {
    const aWin = m.winnerId === m.playerAId;
    const bWin = m.winnerId === m.playerBId;
    return (
      <div className="rps-bracket-cell">
        <div className="rps-bracket-label">{label}</div>
        <div className={`rps-bracket-row ${aWin ? 'win' : m.winnerId ? 'lose' : ''}`}>
          <span className="rps-bracket-name">{m.playerAId ? nameOf[m.playerAId] ?? '?' : '—'}</span>
          <span className="rps-bracket-score">{m.winsA}</span>
        </div>
        <div className={`rps-bracket-row ${bWin ? 'win' : m.winnerId ? 'lose' : ''}`}>
          <span className="rps-bracket-name">{m.playerBId ? nameOf[m.playerBId] ?? '?' : '—'}</span>
          <span className="rps-bracket-score">{m.winsB}</span>
        </div>
      </div>
    );
  }

  function HandPanel({ uid, chosen, lastChoice, side }: { uid: string; chosen: boolean; lastChoice: number; side: 'a' | 'b' }) {
    const isMe = uid === myUserId;
    // Hiện lá đã ra của ván VỪA chốt (hasLast) cho cả 2; ván đang chơi chỉ hiện "đã chọn / chờ".
    const reveal = cur.hasLast ? lastChoice : 0;
    const emoji = reveal ? CHOICE_EMOJI[reveal] : (chosen ? '✅' : '❔');
    return (
      <div className={`rps-hand ${side} ${isMe ? 'is-me' : ''}`}>
        <Avatar name={nameOf[uid] ?? '?'} hasAvatar={avatarOf[uid]} playerId={uid} size="md" />
        <div className="rps-hand-name">{nameOf[uid] ?? '?'}</div>
        <div className={`rps-hand-emoji ${reveal ? 'revealed' : chosen ? 'ready' : 'waiting'}`}>{emoji}</div>
      </div>
    );
  }

  if (done) {
    const medals = ['🥇', '🥈', '🥉', '4️⃣'];
    const deltas = [2, 1, -1, -2];
    return (
      <div className="rps-break-overlay">
        <div className="rps-break-card">
          <h2>🏆 Kết quả Giải lao</h2>
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
          <div className="rps-done-hint">Điểm đã cộng vào bảng điểm trận · sắp qua ván tiếp…</div>
        </div>
      </div>
    );
  }

  return (
    <div className="rps-break-overlay">
      <div className="rps-break-card">
        <div className="rps-break-head">
          <h2>🎮 Giải lao — Oẳn Tù Xì</h2>
          <div className="rps-stage">{STAGE_TITLE[rps.stage]}</div>
        </div>

        {/* Bracket tổng quan */}
        <div className="rps-bracket">
          <MiniBracket m={rps.round1A} label="V1" />
          <MiniBracket m={rps.round1B} label="V2" />
          <MiniBracket m={rps.thirdPlace} label="Tranh 3" />
          <MiniBracket m={rps.final} label="CK" />
        </div>

        {/* Cặp đang đấu */}
        <div className="rps-arena">
          <HandPanel uid={cur.playerAId} chosen={cur.aChosen} lastChoice={cur.lastChoiceA} side="a" />
          <div className="rps-vs">
            <div className="rps-vs-text">VS</div>
            <div className="rps-score-big">{cur.winsA} <span>:</span> {cur.winsB}</div>
            <div className="rps-target">cán {cur.winTarget} thắng</div>
            {cur.hasLast && cur.lastOutcome === 0 && <div className="rps-draw">🤝 Hòa — đánh lại!</div>}
          </div>
          <HandPanel uid={cur.playerBId} chosen={cur.bChosen} lastChoice={cur.lastChoiceB} side="b" />
        </div>

        {/* Khu chọn của tôi / khán giả */}
        {iPlay ? (
          <div className="rps-pick">
            {iChose ? (
              <div className="rps-pick-waiting">⏳ Đã chọn — chờ đối thủ… ({leftSec}s)</div>
            ) : (
              <>
                <div className="rps-pick-label">Chọn đi! Còn <b>{leftSec}s</b></div>
                <div className="rps-pick-buttons">
                  {CHOICES.map(c => (
                    <button key={c} className="rps-pick-btn" onClick={() => onChoose(c)}>
                      <span className="rps-pick-emoji">{CHOICE_EMOJI[c]}</span>
                      <span className="rps-pick-cap">{CHOICE_LABEL[c]}</span>
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        ) : (
          <div className="rps-spectate">
            👀 Đang xem <b>{nameOf[cur.playerAId]}</b> vs <b>{nameOf[cur.playerBId]}</b> · {leftSec}s
          </div>
        )}
        <div className="rps-legend">✊ Búa · ✋ Bao · ✌️ Kéo</div>
      </div>
    </div>
  );
}
