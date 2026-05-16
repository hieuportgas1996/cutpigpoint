import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, Game, GamePlayer, GameType, PlayerRoundInput } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { formatScore, scoreClass, formatDateTime } from '../ui/helpers';
import { Avatar } from '../ui/Avatar';

type PlayerInputState = PlayerRoundInput;

function emptyInput(playerId: string): PlayerInputState {
  return {
    playerId,
    rank: null,
    blackPigsCut: 0,
    redPigsCut: 0,
    blackPigsLost: 0,
    redPigsLost: 0,
    threePairsStraight: false,
    threePairsVictimId: null,
    fourOfAKind: false,
    fourOfAKindVictimId: null,
    fourPairsStraight: false,
    fourPairsVictimId: null,
    whiteWin: false,
    judge: false,
    judgedVictim: false,
    blackPigsHeld: 0,
    redPigsHeld: 0,
    hasThreePairsHeld: false,
    hasFourOfAKindHeld: false,
    hasFourPairsHeld: false,
    wonByThreeOfSpades: false,
    lostByThreeOfSpades: false,
    breakAndCleared: false,
    ballHits: null,
    manualScore: null
  };
}

type RoundMode = 'normal' | 'whiteWin' | 'judge';
type BidaMode = 'normal' | 'breakClear';

export default function GamePlayPage() {
  const { id } = useParams<{ id: string }>();
  const [game, setGame] = useState<Game | null>(null);
  const [manualScoring, setManualScoring] = useState(false);
  const [mode, setMode] = useState<RoundMode>('normal');
  const [bidaMode, setBidaMode] = useState<BidaMode>('normal');
  const [breakerId, setBreakerId] = useState<string | null>(null);
  const [specialPlayerId, setSpecialPlayerId] = useState<string | null>(null);
  const [inputs, setInputs] = useState<PlayerInputState[]>([]);
  const [manualScoreText, setManualScoreText] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const toast = useToast();

  const refresh = useCallback(async () => {
    if (!id) return;
    try {
      const g = await api.getGame(id);
      setGame(g);
      resetInputs(g);
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }, [id, toast]);

  function resetInputs(g: Game) {
    setInputs(g.players.map((p) => emptyInput(p.playerId)));
    setManualScoreText({});
    setManualScoring(false);
    setMode('normal');
    setBidaMode('normal');
    setBreakerId(null);
    setSpecialPlayerId(null);
  }

  useEffect(() => {
    refresh();
  }, [refresh]);

  const ranking = useMemo(() => {
    if (!game) return [];
    return [...game.players].sort((a, b) => b.totalScore - a.totalScore);
  }, [game]);

  function updateInput(playerId: string, patch: Partial<PlayerInputState>) {
    setInputs((prev) => prev.map((it) => (it.playerId === playerId ? { ...it, ...patch } : it)));
  }

  function setRank(playerId: string, rank: number | null) {
    setInputs((prev) =>
      prev.map((it) =>
        it.playerId === playerId
          ? {
              ...it,
              rank,
              wonByThreeOfSpades: rank === 1 ? it.wonByThreeOfSpades : false,
              lostByThreeOfSpades: rank === 4 ? it.lostByThreeOfSpades : false
            }
          : it
      )
    );
  }

  function setSpecial(newMode: RoundMode, playerId: string | null) {
    setMode(newMode);
    setSpecialPlayerId(playerId);
    if (game) {
      setInputs(
        game.players.map((p) => {
          const base = emptyInput(p.playerId);
          if (newMode === 'whiteWin' && p.playerId === playerId) base.whiteWin = true;
          if (newMode === 'judge' && p.playerId === playerId) base.judge = true;
          // default: judge xử cả 3 người (case 1)
          if (newMode === 'judge' && playerId && p.playerId !== playerId) base.judgedVictim = true;
          return base;
        })
      );
    }
  }

  function buildSubmitInputs(): PlayerInputState[] {
    if (mode === 'normal') return inputs;

    if (mode === 'whiteWin') {
      return inputs.map((it) => ({
        ...emptyInput(it.playerId),
        whiteWin: it.playerId === specialPlayerId
      }));
    }

    // mode === 'judge'
    return inputs.map((it) => {
      if (it.playerId === specialPlayerId) {
        return { ...emptyInput(it.playerId), judge: true };
      }
      if (it.judgedVictim) {
        return {
          ...emptyInput(it.playerId),
          judgedVictim: true,
          blackPigsHeld: it.blackPigsHeld,
          redPigsHeld: it.redPigsHeld,
          hasThreePairsHeld: it.hasThreePairsHeld,
          hasFourOfAKindHeld: it.hasFourOfAKindHeld,
          hasFourPairsHeld: it.hasFourPairsHeld
        };
      }
      // pardoned: keep rank + pigs + bonuses for case 3, ignored by backend in case 1/2
      return {
        ...emptyInput(it.playerId),
        rank: it.rank,
        blackPigsCut: it.blackPigsCut,
        redPigsCut: it.redPigsCut,
        blackPigsLost: it.blackPigsLost,
        redPigsLost: it.redPigsLost,
        threePairsStraight: it.threePairsStraight,
        threePairsVictimId: it.threePairsVictimId,
        fourOfAKind: it.fourOfAKind,
        fourOfAKindVictimId: it.fourOfAKindVictimId,
        fourPairsStraight: it.fourPairsStraight,
        fourPairsVictimId: it.fourPairsVictimId
      };
    });
  }

  function addBallHit(playerId: string, ball: number, points: number) {
    setInputs((prev) =>
      prev.map((it) =>
        it.playerId === playerId
          ? { ...it, ballHits: [...(it.ballHits ?? []), { ball, points, victimPlayerId: '' }] }
          : it
      )
    );
  }

  function removeBallHit(playerId: string, idx: number) {
    setInputs((prev) =>
      prev.map((it) =>
        it.playerId === playerId
          ? { ...it, ballHits: (it.ballHits ?? []).filter((_, i) => i !== idx) }
          : it
      )
    );
  }

  function setBallHitVictim(playerId: string, idx: number, victimId: string) {
    setInputs((prev) =>
      prev.map((it) =>
        it.playerId === playerId
          ? {
              ...it,
              ballHits: (it.ballHits ?? []).map((h, i) =>
                i === idx ? { ...h, victimPlayerId: victimId } : h
              )
            }
          : it
      )
    );
  }

  function buildBidaInputs(): PlayerInputState[] {
    if (bidaMode === 'breakClear') {
      return inputs.map((it) => ({
        ...emptyInput(it.playerId),
        breakAndCleared: it.playerId === breakerId
      }));
    }
    return inputs.map((it) => ({
      ...emptyInput(it.playerId),
      ballHits: it.ballHits && it.ballHits.length > 0 ? it.ballHits : null
    }));
  }

  async function submitRound() {
    if (!id || !game) return;
    setSubmitting(true);
    try {
      const isManual = game.type === GameType.Manual;
      const isBida = game.type === GameType.Bida9Ball;
      const useManualScoring = isManual || (manualScoring && !isBida);
      let payload: PlayerInputState[];
      if (useManualScoring) payload = inputs;
      else if (isBida) payload = buildBidaInputs();
      else payload = buildSubmitInputs();
      await api.addRound(id, useManualScoring, payload);
      toast.push('success', `Đã lưu round #${(game?.rounds.length ?? 0) + 1}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  async function deleteRound(roundId: string, num: number) {
    if (!id) return;
    if (!confirm(`Xoá round #${num}?`)) return;
    try {
      await api.deleteRound(id, roundId);
      toast.push('info', `Đã xoá round #${num}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function finishGame() {
    if (!id || !game) return;
    if (!confirm('Kết thúc ván này?')) return;
    try {
      const g = await api.finishGame(id);
      setGame(g);
      toast.push('success', 'Đã kết thúc ván');
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  if (!game) {
    return (
      <div className="card empty">
        <div className="empty-icon"><Icon name="clock" /></div>
        <div>Đang tải…</div>
      </div>
    );
  }

  const finished = !!game.finishedAt;
  const nextRoundNum = game.rounds.length + 1;
  const champion = finished && ranking.length > 0 ? ranking[0] : null;
  const isManualGame = game.type === GameType.Manual;
  const isBidaGame = game.type === GameType.Bida9Ball;
  const effectiveManualScoring = isManualGame || (manualScoring && !isBidaGame);

  const manualHasPositive = effectiveManualScoring && inputs.some((i) => (i.manualScore ?? 0) > 0);
  const manualHasNegative = effectiveManualScoring && inputs.some((i) => (i.manualScore ?? 0) < 0);
  const manualSum = effectiveManualScoring ? inputs.reduce((s, i) => s + (i.manualScore ?? 0), 0) : 0;
  const tienLenManualValid = !manualScoring || (manualHasPositive && manualHasNegative && manualSum === 0);
  const manualGameValid = inputs.some((i) => i.manualScore !== null && i.manualScore !== 0);
  const manualValid = isManualGame ? manualGameValid : tienLenManualValid;

  const totalBidaHits = inputs.reduce((s, i) => s + (i.ballHits?.length ?? 0), 0);
  const allBidaHitsHaveVictim = inputs.every((i) =>
    !i.ballHits || i.ballHits.every((h) => !!h.victimPlayerId && h.victimPlayerId !== i.playerId)
  );
  const bidaTotalBallPoints = (game.ballConfig ?? []).reduce((s, b) => s + b.points, 0);
  const bidaLosers = Math.max(1, game.players.length - 1);
  const bidaBreakerEvenSplit = (bidaTotalBallPoints * 2) % bidaLosers === 0;
  const bidaValid = isBidaGame
    ? bidaMode === 'breakClear'
      ? !!breakerId && bidaBreakerEvenSplit
      : totalBidaHits > 0 && allBidaHitsHaveVictim
    : true;

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>
            {isManualGame ? 'Ván tự do (chấm tay)' :
             isBidaGame ? 'Ván Bida 9 Bi' :
             'Ván Tiến Lên Miền Nam'}
          </h1>
          <div className="muted small">
            <Icon name="clock" size={12} /> Bắt đầu {formatDateTime(game.startedAt)}
            {finished && ` • Kết thúc ${formatDateTime(game.finishedAt!)}`}
          </div>
        </div>
        <span className={`status ${finished ? 'done' : 'live'}`}>
          {finished ? 'Đã kết thúc' : 'Đang chơi'}
        </span>
      </div>

      {champion && (
        <div className="hero" style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
          <div className="rank-badge r1" style={{ width: 56, height: 56, fontSize: '1.5rem' }}>
            <Icon name="trophy" size={28} />
          </div>
          <div>
            <div className="dim small">Người thắng</div>
            <div style={{ fontSize: '1.5rem', fontWeight: 800 }}>{champion.name}</div>
            <div className="muted small">{formatScore(champion.totalScore)} điểm</div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <h3><Icon name="trophy" size={18} /> Bảng điểm</h3>
          <div className="spacer" />
          {!finished && (
            <button className="danger sm" onClick={finishGame}>
              <Icon name="flag" size={14} /> Kết thúc ván
            </button>
          )}
        </div>
        <div className="leaderboard">
          {ranking.map((p, idx) => (
            <div key={p.playerId} className={`leader-row ${idx === 0 ? 'top1' : ''}`}>
              <div className={`rank-badge r${idx + 1}`}>{idx + 1}</div>
              <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
              <div className="name">{p.name}</div>
              <span className={`score-pill ${scoreClass(p.totalScore)}`}>{formatScore(p.totalScore)}</span>
            </div>
          ))}
        </div>
      </div>

      {!finished && (
        <div className="card">
          <div className="card-header">
            <h3 style={{ margin: 0 }}>Round #{nextRoundNum}</h3>
            <div className="spacer" />
            {!isManualGame && !isBidaGame && (
              <label className="inline">
                <input
                  type="checkbox"
                  checked={manualScoring}
                  onChange={(e) => {
                    setManualScoring(e.target.checked);
                    if (e.target.checked) setMode('normal');
                  }}
                />
                <span className="small">Nhập điểm thủ công</span>
              </label>
            )}
          </div>

          {isBidaGame && (
            <BidaRoundPanel
              players={game.players}
              ballConfig={game.ballConfig ?? []}
              inputs={inputs}
              mode={bidaMode}
              breakerId={breakerId}
              onModeChange={(m) => {
                setBidaMode(m);
                setBreakerId(null);
                setInputs(game.players.map((p) => emptyInput(p.playerId)));
              }}
              onBreakerChange={setBreakerId}
              onAddHit={addBallHit}
              onRemoveHit={removeBallHit}
              onSetVictim={setBallHitVictim}
            />
          )}

          {!isManualGame && !isBidaGame && !manualScoring && (
            <ModeSwitcher
              mode={mode}
              specialPlayerId={specialPlayerId}
              players={game.players}
              onSelect={(m, pid) => setSpecial(m, pid)}
            />
          )}

          {!isManualGame && !isBidaGame && !manualScoring && mode === 'normal' && (
            <div className="player-grid">
              {game.players.map((p) => (
                <NormalPlayerCard
                  key={p.playerId}
                  player={p}
                  others={game.players.filter((x) => x.playerId !== p.playerId)}
                  input={inputs.find((i) => i.playerId === p.playerId)!}
                  allInputs={inputs}
                  onUpdate={(patch) => updateInput(p.playerId, patch)}
                  onSetRank={(rank) => setRank(p.playerId, rank)}
                />
              ))}
            </div>
          )}

          {!isManualGame && !isBidaGame && !manualScoring && mode === 'whiteWin' && specialPlayerId && (
            <WhiteWinSummary
              winner={game.players.find((p) => p.playerId === specialPlayerId)!}
              others={game.players.filter((p) => p.playerId !== specialPlayerId)}
            />
          )}

          {!isManualGame && !isBidaGame && !manualScoring && mode === 'judge' && specialPlayerId && (
            <JudgePanel
              judge={game.players.find((p) => p.playerId === specialPlayerId)!}
              others={game.players.filter((p) => p.playerId !== specialPlayerId)}
              inputs={inputs}
              onUpdate={updateInput}
              onSetRank={setRank}
            />
          )}

          {effectiveManualScoring && (
            <div className="player-grid">
              {game.players.map((p) => {
                const input = inputs.find((i) => i.playerId === p.playerId)!;
                return (
                  <div key={p.playerId} className="player-card">
                    <div className="player-card-head">
                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                        <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                        <h4>{p.name}</h4>
                      </div>
                    </div>
                    <div>
                      <label htmlFor={`m-${p.playerId}`}>Điểm</label>
                      <input
                        id={`m-${p.playerId}`}
                        type="text"
                        inputMode="text"
                        pattern="-?[0-9]*"
                        value={manualScoreText[p.playerId] ?? (input.manualScore?.toString() ?? '')}
                        onChange={(e) => {
                          const raw = e.target.value.trim();
                          if (!/^-?\d*$/.test(raw)) return;
                          setManualScoreText((prev) => ({ ...prev, [p.playerId]: raw }));
                          if (raw === '' || raw === '-') {
                            updateInput(p.playerId, { manualScore: null });
                          } else {
                            updateInput(p.playerId, { manualScore: Number(raw) });
                          }
                        }}
                        onBlur={() => {
                          setManualScoreText((prev) => {
                            const next = { ...prev };
                            delete next[p.playerId];
                            return next;
                          });
                        }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {effectiveManualScoring && (
            <div
              className="mt-2"
              style={{
                padding: '0.6rem 0.85rem',
                borderRadius: 'var(--radius)',
                border: `1px solid ${manualValid ? 'var(--border)' : 'var(--danger)'}`,
                background: manualValid ? 'var(--bg-1)' : 'var(--danger-bg)',
                color: manualValid ? 'var(--text-muted)' : 'var(--danger)',
                fontSize: '0.88rem'
              }}
            >
              <Icon name={manualValid ? 'info' : 'alert'} size={14} />{' '}
              {isManualGame
                ? manualValid
                  ? `Tổng round: ${manualSum > 0 ? '+' : ''}${manualSum} • Nhập điểm cộng/trừ tự do, không bắt buộc tổng = 0`
                  : 'Cần nhập điểm cho ít nhất 1 người (khác 0)'
                : manualValid
                ? `Hợp lệ — tổng điểm = 0`
                : !manualHasPositive || !manualHasNegative
                ? 'Round phải có cả người điểm dương và người điểm âm'
                : `Tổng điểm phải bằng 0 (hiện tại: ${manualSum > 0 ? '+' : ''}${manualSum})`}
            </div>
          )}

          <button
            onClick={submitRound}
            disabled={submitting || (isBidaGame ? !bidaValid : !manualValid)}
            className="block-mobile mt-2"
          >
            <Icon name="check" size={16} />
            {submitting ? 'Đang lưu…' : `Lưu round #${nextRoundNum}`}
          </button>
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <h3>Lịch sử rounds</h3>
          <span className="status done">{game.rounds.length} round</span>
        </div>
        {game.rounds.length === 0 ? (
          <div className="empty" style={{ padding: '1.5rem 0' }}>
            <div className="muted">Chưa có round nào.</div>
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>#</th>
                  {game.players.map((p) => (
                    <th key={p.playerId} style={{ textAlign: 'right' }}>{p.name}</th>
                  ))}
                  {!finished && <th></th>}
                </tr>
              </thead>
              <tbody>
                {game.rounds.map((r) => {
                  const tag = r.results.find((x) => x.judge)
                    ? 'PX'
                    : r.results.find((x) => x.whiteWin)
                    ? 'VT'
                    : r.results.find((x) => x.breakAndCleared)
                    ? 'PC'
                    : r.manualScoring
                    ? 'TC'
                    : null;
                  return (
                    <tr key={r.id}>
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                          <span className="bold">#{r.roundNumber}</span>
                          {tag && <span className="tiny dim">{tag}</span>}
                        </div>
                      </td>
                      {game.players.map((p) => {
                        const res = r.results.find((rr) => rr.playerId === p.playerId);
                        const score = res?.score ?? 0;
                        return (
                          <td key={p.playerId} style={{ textAlign: 'right' }}>
                            <span className={`score-pill ${scoreClass(score)}`}>{formatScore(score)}</span>
                          </td>
                        );
                      })}
                      {!finished && (
                        <td style={{ textAlign: 'right' }}>
                          <button
                            className="ghost icon-only"
                            onClick={() => deleteRound(r.id, r.roundNumber)}
                            aria-label="Xoá"
                          >
                            <Icon name="trash" size={14} />
                          </button>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

/* ---------- Mode Switcher ---------- */

function ModeSwitcher({
  mode,
  specialPlayerId,
  players,
  onSelect
}: {
  mode: RoundMode;
  specialPlayerId: string | null;
  players: GamePlayer[];
  onSelect: (mode: RoundMode, playerId: string | null) => void;
}) {
  return (
    <div className="card" style={{ background: 'var(--bg-1)', marginBottom: '1rem', padding: '0.85rem 1rem' }}>
      <div className="section-title">Chế độ round</div>
      <div className="row mt-1">
        <button
          type="button"
          className={mode === 'normal' ? '' : 'secondary'}
          onClick={() => onSelect('normal', null)}
        >
          Hạng + Heo
        </button>
        <SpecialPicker
          label="Về trắng"
          icon="star"
          active={mode === 'whiteWin'}
          activePlayerId={mode === 'whiteWin' ? specialPlayerId : null}
          players={players}
          onPick={(pid) => onSelect('whiteWin', pid)}
          onClear={() => onSelect('normal', null)}
        />
        <SpecialPicker
          label="Phán xét"
          icon="flag"
          active={mode === 'judge'}
          activePlayerId={mode === 'judge' ? specialPlayerId : null}
          players={players}
          onPick={(pid) => onSelect('judge', pid)}
          onClear={() => onSelect('normal', null)}
        />
      </div>
      {mode === 'whiteWin' && (
        <div className="muted tiny mt-1">
          <Icon name="info" size={11} /> Người về trắng +6, mỗi người còn lại −2. Round kết thúc, không cần nhập gì khác.
        </div>
      )}
      {mode === 'judge' && (
        <div className="muted tiny mt-1">
          <Icon name="info" size={11} /> Người phán xét +12, mỗi người còn lại −4, cộng thêm heo và bonus còn trên tay 3 người kia.
        </div>
      )}
    </div>
  );
}

function SpecialPicker({
  label,
  icon,
  active,
  activePlayerId,
  players,
  onPick,
  onClear
}: {
  label: string;
  icon: 'star' | 'flag';
  active: boolean;
  activePlayerId: string | null;
  players: GamePlayer[];
  onPick: (pid: string) => void;
  onClear: () => void;
}) {
  const [open, setOpen] = useState(false);
  const activePlayer = active ? players.find((p) => p.playerId === activePlayerId) : null;

  if (active && activePlayer) {
    return (
      <div className="row gap-sm" style={{ alignItems: 'center' }}>
        <div className="row gap-sm" style={{ background: 'var(--accent-grad-soft)', border: '1px solid var(--accent)', borderRadius: 'var(--radius)', padding: '0.35rem 0.6rem' }}>
          <Icon name={icon} size={14} />
          <span className="small bold">{label}:</span>
          <Avatar playerId={activePlayer.playerId} name={activePlayer.name} hasAvatar={activePlayer.hasAvatar} size="sm" />
          <span className="small">{activePlayer.name}</span>
        </div>
        <button type="button" className="ghost sm" onClick={onClear}>
          Bỏ chọn
        </button>
      </div>
    );
  }

  return (
    <div style={{ position: 'relative' }}>
      <button type="button" className="secondary" onClick={() => setOpen((v) => !v)}>
        <Icon name={icon} size={14} /> {label}
      </button>
      {open && (
        <div
          style={{
            position: 'absolute',
            top: 'calc(100% + 6px)',
            left: 0,
            zIndex: 10,
            background: 'var(--bg-elev)',
            border: '1px solid var(--border-strong)',
            borderRadius: 'var(--radius)',
            boxShadow: 'var(--shadow-lg)',
            minWidth: 200,
            padding: '0.4rem'
          }}
        >
          <div className="tiny dim" style={{ padding: '0.3rem 0.5rem' }}>Chọn người ăn {label.toLowerCase()}</div>
          {players.map((p) => (
            <button
              key={p.playerId}
              type="button"
              className="ghost"
              style={{ width: '100%', justifyContent: 'flex-start', padding: '0.45rem 0.6rem' }}
              onClick={() => {
                onPick(p.playerId);
                setOpen(false);
              }}
            >
              <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
              <span>{p.name}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/* ---------- Normal player card ---------- */

function NormalPlayerCard({
  player,
  others,
  input,
  allInputs,
  onUpdate,
  onSetRank
}: {
  player: GamePlayer;
  others: GamePlayer[];
  input: PlayerInputState;
  allInputs: PlayerInputState[];
  onUpdate: (patch: Partial<PlayerInputState>) => void;
  onSetRank: (rank: number | null) => void;
}) {
  const cardClass = `player-card ${input.rank === 1 ? 'has-rank-1' : ''} ${input.rank === 4 ? 'has-rank-4' : ''}`;
  const takenRanks = new Set(
    allInputs
      .filter((i) => i.playerId !== player.playerId && i.rank !== null)
      .map((i) => i.rank as number)
  );
  return (
    <div className={cardClass}>
      <div className="player-card-head">
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <Avatar playerId={player.playerId} name={player.name} hasAvatar={player.hasAvatar} size="sm" />
          <h4>{player.name}</h4>
        </div>
        {input.rank && <div className={`rank-badge r${input.rank}`}>#{input.rank}</div>}
      </div>

      <div>
        <div className="section-title">Hạng</div>
        <div className="pill-group">
          {[1, 2, 3, 4].map((r) => {
            const isMine = input.rank === r;
            const isTaken = takenRanks.has(r);
            return (
              <button
                key={r}
                type="button"
                className={isMine ? 'active' : ''}
                disabled={isTaken && !isMine}
                onClick={() => onSetRank(isMine ? null : r)}
              >
                #{r}
              </button>
            );
          })}
        </div>
        {input.rank === 1 && (
          <SimpleToggle
            label="Về nhất 3 bích (+3, mỗi người khác −1)"
            checked={input.wonByThreeOfSpades}
            disabled={!input.wonByThreeOfSpades && allInputs.some((i) => i.lostByThreeOfSpades)}
            onChange={(v) => onUpdate({ wonByThreeOfSpades: v })}
          />
        )}
        {input.rank === 4 && (
          <SimpleToggle
            label="Về chót 3 bích (−3, mỗi người khác +1)"
            checked={input.lostByThreeOfSpades}
            disabled={!input.lostByThreeOfSpades && allInputs.some((i) => i.wonByThreeOfSpades)}
            onChange={(v) => onUpdate({ lostByThreeOfSpades: v })}
          />
        )}
      </div>

      <div>
        <div className="section-title">Heo bị chặt (mất điểm)</div>
        <Stepper label="Heo đen" value={input.blackPigsLost} onChange={(v) => onUpdate({ blackPigsLost: v })} />
        <Stepper label="Heo đỏ" value={input.redPigsLost} onChange={(v) => onUpdate({ redPigsLost: v })} />
      </div>

      <div>
        <div className="section-title">Heo chặt được (ăn điểm)</div>
        <Stepper label="Heo đen" value={input.blackPigsCut} onChange={(v) => onUpdate({ blackPigsCut: v })} />
        <Stepper label="Heo đỏ" value={input.redPigsCut} onChange={(v) => onUpdate({ redPigsCut: v })} />
      </div>

      <div>
        <div className="section-title">Bonus đặc biệt (chọn người thua)</div>
        <BonusVictimRow
          label="3 đôi thông"
          value="+3 / -3"
          checked={input.threePairsStraight}
          victimId={input.threePairsVictimId}
          others={others}
          onCheck={(v) => onUpdate({ threePairsStraight: v, threePairsVictimId: v ? input.threePairsVictimId : null })}
          onVictim={(id) => onUpdate({ threePairsVictimId: id })}
        />
        <BonusVictimRow
          label="Tứ quý"
          value="+4 / -4"
          checked={input.fourOfAKind}
          victimId={input.fourOfAKindVictimId}
          others={others}
          onCheck={(v) => onUpdate({ fourOfAKind: v, fourOfAKindVictimId: v ? input.fourOfAKindVictimId : null })}
          onVictim={(id) => onUpdate({ fourOfAKindVictimId: id })}
        />
        <BonusVictimRow
          label="4 đôi thông"
          value="+5 / -5"
          checked={input.fourPairsStraight}
          victimId={input.fourPairsVictimId}
          others={others}
          onCheck={(v) => onUpdate({ fourPairsStraight: v, fourPairsVictimId: v ? input.fourPairsVictimId : null })}
          onVictim={(id) => onUpdate({ fourPairsVictimId: id })}
        />
      </div>
    </div>
  );
}

function BonusVictimRow({
  label,
  value,
  checked,
  victimId,
  others,
  onCheck,
  onVictim
}: {
  label: string;
  value: string;
  checked: boolean;
  victimId: string | null;
  others: GamePlayer[];
  onCheck: (v: boolean) => void;
  onVictim: (id: string) => void;
}) {
  return (
    <div
      style={{
        padding: '0.55rem 0.7rem',
        background: checked ? 'var(--accent-grad-soft)' : 'var(--bg-1)',
        border: `1px solid ${checked ? 'var(--accent)' : 'var(--border)'}`,
        borderRadius: 'var(--radius-sm)',
        marginTop: '0.4rem',
        transition: 'all var(--transition)'
      }}
    >
      <label className="inline" style={{ width: '100%', justifyContent: 'space-between' }}>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}>
          <input type="checkbox" checked={checked} onChange={(e) => onCheck(e.target.checked)} />
          <span className="small">{label}</span>
        </span>
        <span className="tiny dim bold">{value}</span>
      </label>
      {checked && (
        <div className="mt-1">
          <select
            value={victimId ?? ''}
            onChange={(e) => onVictim(e.target.value)}
            style={{ fontSize: '0.85rem' }}
          >
            <option value="">— Chọn người thua —</option>
            {others.map((o) => (
              <option key={o.playerId} value={o.playerId}>{o.name}</option>
            ))}
          </select>
        </div>
      )}
    </div>
  );
}

/* ---------- White win summary ---------- */

function WhiteWinSummary({
  winner,
  others
}: {
  winner: GamePlayer;
  others: GamePlayer[];
}) {
  return (
    <div
      className="card"
      style={{
        background: 'linear-gradient(135deg, rgba(96,165,250,0.12), rgba(167,139,250,0.12))',
        border: '1px solid var(--accent)',
        marginBottom: 0
      }}
    >
      <div className="row" style={{ alignItems: 'center', gap: '0.85rem' }}>
        <div className="rank-badge r1" style={{ width: 44, height: 44 }}>
          <Icon name="star" size={20} />
        </div>
        <div style={{ flex: 1 }}>
          <div className="dim small">Người về trắng</div>
          <div className="bold" style={{ fontSize: '1.1rem' }}>{winner.name}</div>
          <div className="muted small">+6 điểm</div>
        </div>
      </div>
      <div className="section-title mt-2">3 người còn lại</div>
      <div className="col mt-1">
        {others.map((o) => (
          <div key={o.playerId} className="leader-row" style={{ background: 'var(--bg-2)' }}>
            <Avatar playerId={o.playerId} name={o.name} hasAvatar={o.hasAvatar} size="sm" />
            <div className="name">{o.name}</div>
            <span className="score-pill neg">-2</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ---------- Judge panel ---------- */

function JudgePanel({
  judge,
  others,
  inputs,
  onUpdate,
  onSetRank
}: {
  judge: GamePlayer;
  others: GamePlayer[];
  inputs: PlayerInputState[];
  onUpdate: (playerId: string, patch: Partial<PlayerInputState>) => void;
  onSetRank: (playerId: string, rank: number | null) => void;
}) {
  function inputFor(pid: string) {
    return inputs.find((i) => i.playerId === pid)!;
  }
  function heldExtra(pid: string) {
    const i = inputFor(pid);
    return (
      i.blackPigsHeld * 1 +
      i.redPigsHeld * 2 +
      (i.hasThreePairsHeld ? 3 : 0) +
      (i.hasFourOfAKindHeld ? 4 : 0) +
      (i.hasFourPairsHeld ? 5 : 0)
    );
  }

  const victims = others.filter((p) => inputFor(p.playerId).judgedVictim);
  const pardoned = others.filter((p) => !inputFor(p.playerId).judgedVictim);
  const victimCount = victims.length;
  const totalExtra = victims.reduce((s, p) => s + heldExtra(p.playerId), 0);
  const judgeBase = victimCount === 1 ? 4 : victimCount === 2 ? 9 : 12;
  const judgeFinal = judgeBase + totalExtra;

  return (
    <div>
      <div
        className="card"
        style={{
          background: 'linear-gradient(135deg, rgba(251,191,36,0.12), rgba(167,139,250,0.12))',
          border: '1px solid var(--accent-2)',
          marginBottom: '1rem'
        }}
      >
        <div className="row" style={{ alignItems: 'center', gap: '0.85rem' }}>
          <div className="rank-badge r1" style={{ width: 44, height: 44 }}>
            <Icon name="flag" size={20} />
          </div>
          <div style={{ flex: 1 }}>
            <div className="dim small">Người phán xét</div>
            <div className="bold" style={{ fontSize: '1.1rem' }}>{judge.name}</div>
            <div className="muted small">
              +{judgeBase} cơ bản
              {totalExtra > 0 && <> • +{totalExtra} từ bài bị xử</>}
              {' • '}
              <strong>Tổng dự kiến: {formatScore(judgeFinal)}</strong>
            </div>
          </div>
        </div>
      </div>

      <div className="section-title">Chọn người bị xử (1-3 người)</div>
      <div className="muted tiny mb-1">
        <Icon name="info" size={11} /> Tick những người bị phán xét. Người được tha sẽ chịu phạt nhẹ hoặc chơi bình thường.
      </div>
      <div className="row mt-1" style={{ marginBottom: '0.85rem' }}>
        {others.map((p) => {
          const i = inputFor(p.playerId);
          const isVictim = i.judgedVictim;
          return (
            <button
              key={p.playerId}
              type="button"
              className={isVictim ? '' : 'secondary'}
              onClick={() => {
                onUpdate(p.playerId, {
                  judgedVictim: !isVictim,
                  // Khi chuyển trạng thái, reset rank của người này
                  rank: null,
                  blackPigsCut: 0,
                  redPigsCut: 0,
                  blackPigsLost: 0,
                  redPigsLost: 0,
                  threePairsStraight: false,
                  threePairsVictimId: null,
                  fourOfAKind: false,
                  fourOfAKindVictimId: null,
                  fourPairsStraight: false,
                  fourPairsVictimId: null,
                  blackPigsHeld: 0,
                  redPigsHeld: 0,
                  hasThreePairsHeld: false,
                  hasFourOfAKindHeld: false,
                  hasFourPairsHeld: false
                });
              }}
              style={{ padding: '0.4rem 0.7rem 0.4rem 0.4rem', gap: '0.5rem' }}
            >
              <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
              <span>{p.name}</span>
              {isVictim && <Icon name="check" size={14} />}
            </button>
          );
        })}
      </div>

      {victimCount === 0 && (
        <div className="card empty" style={{ padding: '1rem' }}>
          <div className="muted small">Chưa chọn ai bị phán xét. Tick ít nhất 1 người ở trên.</div>
        </div>
      )}

      {victimCount >= 1 && (
        <>
          <div className="section-title">Bài còn trên tay người bị xử</div>
          <div className="muted tiny mb-1">
            <Icon name="info" size={11} /> Heo đen +1, đỏ +2 • 3 đôi thông +3 • tứ quý +4 • 4 đôi thông +5
          </div>
          <div className="player-grid">
            {victims.map((p) => {
              const i = inputFor(p.playerId);
              const extra = heldExtra(p.playerId);
              return (
                <div key={p.playerId} className="player-card has-rank-4">
                  <div className="player-card-head">
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                      <h4>{p.name}</h4>
                    </div>
                    <span className="score-pill neg">-{4 + extra}</span>
                  </div>
                  <div>
                    <div className="section-title">Heo trên tay</div>
                    <Stepper label="Heo đen" value={i.blackPigsHeld} onChange={(v) => onUpdate(p.playerId, { blackPigsHeld: v })} />
                    <Stepper label="Heo đỏ" value={i.redPigsHeld} onChange={(v) => onUpdate(p.playerId, { redPigsHeld: v })} />
                  </div>
                  <div>
                    <div className="section-title">Bonus trên tay</div>
                    <SimpleToggle label="3 đôi thông (+3)" checked={i.hasThreePairsHeld} onChange={(v) => onUpdate(p.playerId, { hasThreePairsHeld: v })} />
                    <SimpleToggle label="Tứ quý (+4)" checked={i.hasFourOfAKindHeld} onChange={(v) => onUpdate(p.playerId, { hasFourOfAKindHeld: v })} />
                    <SimpleToggle label="4 đôi thông (+5)" checked={i.hasFourPairsHeld} onChange={(v) => onUpdate(p.playerId, { hasFourPairsHeld: v })} />
                  </div>
                </div>
              );
            })}
          </div>
        </>
      )}

      {victimCount === 2 && pardoned.length === 1 && (
        <div className="mt-2">
          <div className="section-title">Người được tha</div>
          <div className="leader-row" style={{ background: 'var(--bg-2)' }}>
            <Avatar playerId={pardoned[0].playerId} name={pardoned[0].name} hasAvatar={pardoned[0].hasAvatar} size="sm" />
            <div className="name">{pardoned[0].name}</div>
            <span className="score-pill neg">-1</span>
          </div>
        </div>
      )}

      {victimCount === 1 && pardoned.length === 2 && (
        <div className="mt-3">
          <div className="section-title">2 người không bị xử — chia hạng #2 và #3</div>
          <div className="muted tiny mb-1">
            <Icon name="info" size={11} /> Chọn hạng cho 2 người này. Có thể cộng heo chặt nhau và bonus 1-vs-1.
          </div>
          <div className="player-grid">
            {pardoned.map((p) => {
              const input = inputFor(p.playerId);
              const takenRanks = new Set(
                pardoned
                  .filter((x) => x.playerId !== p.playerId)
                  .map((x) => inputFor(x.playerId).rank)
                  .filter((r): r is number => r !== null)
              );
              return (
                <div
                  key={p.playerId}
                  className={`player-card ${input.rank === 2 ? 'has-rank-1' : ''}`}
                >
                  <div className="player-card-head">
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                      <h4>{p.name}</h4>
                    </div>
                    {input.rank && <div className={`rank-badge r${input.rank}`}>#{input.rank}</div>}
                  </div>
                  <div>
                    <div className="section-title">Hạng</div>
                    <div className="pill-group">
                      {[2, 3].map((r) => {
                        const isMine = input.rank === r;
                        const isTaken = takenRanks.has(r);
                        return (
                          <button
                            key={r}
                            type="button"
                            className={isMine ? 'active' : ''}
                            disabled={isTaken && !isMine}
                            onClick={() => onSetRank(p.playerId, isMine ? null : r)}
                          >
                            #{r}
                          </button>
                        );
                      })}
                    </div>
                  </div>
                  <div>
                    <div className="section-title">Heo bị chặt (mất điểm)</div>
                    <Stepper label="Heo đen" value={input.blackPigsLost} onChange={(v) => onUpdate(p.playerId, { blackPigsLost: v })} />
                    <Stepper label="Heo đỏ" value={input.redPigsLost} onChange={(v) => onUpdate(p.playerId, { redPigsLost: v })} />
                  </div>
                  <div>
                    <div className="section-title">Heo chặt được (ăn điểm)</div>
                    <Stepper label="Heo đen" value={input.blackPigsCut} onChange={(v) => onUpdate(p.playerId, { blackPigsCut: v })} />
                    <Stepper label="Heo đỏ" value={input.redPigsCut} onChange={(v) => onUpdate(p.playerId, { redPigsCut: v })} />
                  </div>
                  <div>
                    <div className="section-title">Bonus đặc biệt</div>
                    <BonusVictimRow
                      label="3 đôi thông"
                      value="+3 / -3"
                      checked={input.threePairsStraight}
                      victimId={input.threePairsVictimId}
                      others={pardoned.filter((x) => x.playerId !== p.playerId)}
                      onCheck={(v) => onUpdate(p.playerId, { threePairsStraight: v, threePairsVictimId: v ? input.threePairsVictimId : null })}
                      onVictim={(vid) => onUpdate(p.playerId, { threePairsVictimId: vid })}
                    />
                    <BonusVictimRow
                      label="Tứ quý"
                      value="+4 / -4"
                      checked={input.fourOfAKind}
                      victimId={input.fourOfAKindVictimId}
                      others={pardoned.filter((x) => x.playerId !== p.playerId)}
                      onCheck={(v) => onUpdate(p.playerId, { fourOfAKind: v, fourOfAKindVictimId: v ? input.fourOfAKindVictimId : null })}
                      onVictim={(vid) => onUpdate(p.playerId, { fourOfAKindVictimId: vid })}
                    />
                    <BonusVictimRow
                      label="4 đôi thông"
                      value="+5 / -5"
                      checked={input.fourPairsStraight}
                      victimId={input.fourPairsVictimId}
                      others={pardoned.filter((x) => x.playerId !== p.playerId)}
                      onCheck={(v) => onUpdate(p.playerId, { fourPairsStraight: v, fourPairsVictimId: v ? input.fourPairsVictimId : null })}
                      onVictim={(vid) => onUpdate(p.playerId, { fourPairsVictimId: vid })}
                    />
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

function SimpleToggle({
  label,
  checked,
  onChange,
  disabled
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <label
      className="inline mt-1"
      style={{
        padding: '0.4rem 0.6rem',
        background: checked ? 'var(--accent-grad-soft)' : 'var(--bg-1)',
        border: `1px solid ${checked ? 'var(--accent)' : 'var(--border)'}`,
        borderRadius: 'var(--radius-sm)',
        width: '100%',
        justifyContent: 'space-between',
        transition: 'all var(--transition)',
        opacity: disabled ? 0.5 : 1,
        cursor: disabled ? 'not-allowed' : 'pointer'
      }}
    >
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}>
        <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
        <span className="small">{label}</span>
      </span>
    </label>
  );
}

/* ---------- Bida 9 Ball round panel ---------- */

function BidaRoundPanel({
  players,
  ballConfig,
  inputs,
  mode,
  breakerId,
  onModeChange,
  onBreakerChange,
  onAddHit,
  onRemoveHit,
  onSetVictim
}: {
  players: GamePlayer[];
  ballConfig: { ball: number; points: number }[];
  inputs: PlayerInputState[];
  mode: BidaMode;
  breakerId: string | null;
  onModeChange: (m: BidaMode) => void;
  onBreakerChange: (id: string | null) => void;
  onAddHit: (playerId: string, ball: number, points: number) => void;
  onRemoveHit: (playerId: string, idx: number) => void;
  onSetVictim: (playerId: string, idx: number, victimId: string) => void;
}) {
  function inputFor(pid: string) {
    return inputs.find((i) => i.playerId === pid)!;
  }
  const totalBallPoints = ballConfig.reduce((s, b) => s + b.points, 0);
  const losersCount = Math.max(1, players.length - 1);
  const breakerWinScore = totalBallPoints * 2;
  const breakerLoserScore = -Math.floor(breakerWinScore / losersCount);
  const breakerEvenSplit = breakerWinScore % losersCount === 0;

  const totals = useMemo(() => {
    const map = new Map<string, number>();
    players.forEach((p) => map.set(p.playerId, 0));
    if (mode === 'breakClear') {
      if (breakerId) {
        map.set(breakerId, breakerWinScore);
        players.forEach((p) => {
          if (p.playerId !== breakerId) map.set(p.playerId, breakerLoserScore);
        });
      }
      return map;
    }
    inputs.forEach((it) => {
      (it.ballHits ?? []).forEach((h) => {
        if (!h.victimPlayerId) return;
        map.set(it.playerId, (map.get(it.playerId) ?? 0) + h.points);
        map.set(h.victimPlayerId, (map.get(h.victimPlayerId) ?? 0) - h.points);
      });
    });
    return map;
  }, [inputs, mode, breakerId, players, breakerWinScore, breakerLoserScore]);

  return (
    <>
      <div className="card" style={{ background: 'var(--bg-1)', marginBottom: '1rem', padding: '0.85rem 1rem' }}>
        <div className="section-title">Chế độ round Bida</div>
        <div className="row mt-1">
          <button
            type="button"
            className={mode === 'normal' ? '' : 'secondary'}
            onClick={() => onModeChange('normal')}
          >
            Ăn bi bình thường
          </button>
          <button
            type="button"
            className={mode === 'breakClear' ? '' : 'secondary'}
            onClick={() => onModeChange('breakClear')}
          >
            <Icon name="star" size={14} /> Phá-chấm
          </button>
        </div>
        {mode === 'breakClear' ? (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Tổng điểm các bi = {totalBallPoints}. Người phá chấm xong: +{breakerWinScore}, mỗi người còn lại {breakerLoserScore}.
            {!breakerEvenSplit && <> ⚠️ Tổng điểm không chia đều cho {losersCount} người thua — hãy điều chỉnh điểm bi.</>}
          </div>
        ) : (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Mỗi lần ăn 1 bi tính điểm: chọn bi và người bị trừ. Có thể nhiều entry/người.
          </div>
        )}
      </div>

      {mode === 'breakClear' ? (
        <div className="card" style={{
          background: 'linear-gradient(135deg, rgba(96,165,250,0.12), rgba(167,139,250,0.12))',
          border: '1px solid var(--accent)',
          marginBottom: 0
        }}>
          <div className="section-title">Chọn người phá-chấm</div>
          <div className="row mt-1" style={{ flexWrap: 'wrap' }}>
            {players.map((p) => (
              <button
                key={p.playerId}
                type="button"
                className={breakerId === p.playerId ? '' : 'secondary'}
                onClick={() => onBreakerChange(breakerId === p.playerId ? null : p.playerId)}
                style={{ padding: '0.4rem 0.7rem 0.4rem 0.4rem', gap: '0.5rem' }}
              >
                <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                <span>{p.name}</span>
                {breakerId === p.playerId && <Icon name="check" size={14} />}
              </button>
            ))}
          </div>

          {breakerId && (
            <div className="col mt-2">
              {players.map((p) => {
                const score = totals.get(p.playerId) ?? 0;
                return (
                  <div key={p.playerId} className="leader-row" style={{ background: 'var(--bg-2)' }}>
                    <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                    <div className="name">{p.name}</div>
                    <span className={`score-pill ${scoreClass(score)}`}>{formatScore(score)}</span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      ) : (
        <div className="player-grid">
          {players.map((p) => {
            const it = inputFor(p.playerId);
            const hits = it.ballHits ?? [];
            const others = players.filter((x) => x.playerId !== p.playerId);
            const playerScore = totals.get(p.playerId) ?? 0;
            return (
              <div key={p.playerId} className="player-card">
                <div className="player-card-head">
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                    <h4>{p.name}</h4>
                  </div>
                  <span className={`score-pill ${scoreClass(playerScore)}`}>{formatScore(playerScore)}</span>
                </div>

                <div>
                  <div className="section-title">Ăn bi</div>
                  <div className="muted tiny mb-1">
                    <Icon name="info" size={11} /> Chạm vào bi để thêm 1 lần ăn.
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.4rem' }}>
                    {ballConfig.map((b) => (
                      <button
                        key={b.ball}
                        type="button"
                        className="secondary"
                        onClick={() => onAddHit(p.playerId, b.ball, b.points)}
                        style={{
                          minWidth: 60,
                          padding: '0.35rem 0.55rem',
                          fontWeight: 700,
                          gap: '0.3rem'
                        }}
                        title={`Bi ${b.ball} (+${b.points})`}
                      >
                        <span style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          width: 24, height: 24,
                          borderRadius: '50%',
                          background: 'var(--accent-grad-soft)',
                          fontSize: '0.85rem'
                        }}>{b.ball}</span>
                        <span className="tiny dim">+{b.points}</span>
                      </button>
                    ))}
                  </div>
                </div>

                {hits.length > 0 && (
                  <div>
                    <div className="section-title">Đã ăn ({hits.length})</div>
                    <div className="col mt-1">
                      {hits.map((h, idx) => (
                        <div
                          key={idx}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '0.5rem',
                            padding: '0.45rem 0.6rem',
                            background: 'var(--bg-1)',
                            border: `1px solid ${h.victimPlayerId ? 'var(--border)' : 'var(--danger)'}`,
                            borderRadius: 'var(--radius-sm)'
                          }}
                        >
                          <span style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            width: 28, height: 28,
                            borderRadius: '50%',
                            background: 'var(--accent-grad-soft)',
                            fontWeight: 700
                          }}>{h.ball}</span>
                          <span className="tiny dim bold">+{h.points}</span>
                          <select
                            value={h.victimPlayerId}
                            onChange={(e) => onSetVictim(p.playerId, idx, e.target.value)}
                            style={{ flex: 1, fontSize: '0.85rem' }}
                          >
                            <option value="">— Chọn người bị trừ —</option>
                            {others.map((o) => (
                              <option key={o.playerId} value={o.playerId}>{o.name}</option>
                            ))}
                          </select>
                          <button
                            type="button"
                            className="ghost icon-only"
                            onClick={() => onRemoveHit(p.playerId, idx)}
                            aria-label="Xoá"
                          >
                            <Icon name="trash" size={14} />
                          </button>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </>
  );
}

/* ---------- Stepper ---------- */

function Stepper({
  label,
  value,
  onChange
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="stepper-row mt-1">
      <span className="label">{label}</span>
      <div className="stepper">
        <button type="button" onClick={() => onChange(Math.max(0, value - 1))} aria-label="Giảm">−</button>
        <input
          type="number"
          inputMode="numeric"
          min={0}
          value={value}
          onChange={(e) => onChange(Math.max(0, Number(e.target.value || 0)))}
        />
        <button type="button" onClick={() => onChange(value + 1)} aria-label="Tăng">+</button>
      </div>
    </div>
  );
}
