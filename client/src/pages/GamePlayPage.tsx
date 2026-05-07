import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, Game, GamePlayer, PlayerRoundInput } from '../api';
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
    blackPigsHeld: 0,
    redPigsHeld: 0,
    hasThreePairsHeld: false,
    hasFourOfAKindHeld: false,
    hasFourPairsHeld: false,
    manualScore: null
  };
}

type RoundMode = 'normal' | 'whiteWin' | 'judge';

export default function GamePlayPage() {
  const { id } = useParams<{ id: string }>();
  const [game, setGame] = useState<Game | null>(null);
  const [manualScoring, setManualScoring] = useState(false);
  const [mode, setMode] = useState<RoundMode>('normal');
  const [specialPlayerId, setSpecialPlayerId] = useState<string | null>(null);
  const [inputs, setInputs] = useState<PlayerInputState[]>([]);
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
    setManualScoring(false);
    setMode('normal');
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
      prev.map((it) => {
        if (it.playerId === playerId) return { ...it, rank };
        if (it.rank === rank && rank !== null) return { ...it, rank: null };
        return it;
      })
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
          return base;
        })
      );
    }
  }

  function buildSubmitInputs(): PlayerInputState[] {
    if (mode === 'normal') return inputs;
    return inputs.map((it) => {
      if (it.playerId === specialPlayerId) {
        return {
          ...emptyInput(it.playerId),
          whiteWin: mode === 'whiteWin',
          judge: mode === 'judge'
        };
      }
      if (mode === 'judge') {
        return {
          ...emptyInput(it.playerId),
          blackPigsHeld: it.blackPigsHeld,
          redPigsHeld: it.redPigsHeld,
          hasThreePairsHeld: it.hasThreePairsHeld,
          hasFourOfAKindHeld: it.hasFourOfAKindHeld,
          hasFourPairsHeld: it.hasFourPairsHeld
        };
      }
      return emptyInput(it.playerId);
    });
  }

  async function submitRound() {
    if (!id) return;
    setSubmitting(true);
    try {
      const payload = manualScoring ? inputs : buildSubmitInputs();
      await api.addRound(id, manualScoring, payload);
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

  const manualSum = manualScoring
    ? inputs.reduce((s, i) => s + (i.manualScore ?? 0), 0)
    : 0;
  const manualValid = !manualScoring || manualSum === 0;

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Ván Tiến Lên Miền Nam</h1>
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
          </div>

          {!manualScoring && (
            <ModeSwitcher
              mode={mode}
              specialPlayerId={specialPlayerId}
              players={game.players}
              onSelect={(m, pid) => setSpecial(m, pid)}
            />
          )}

          {!manualScoring && mode === 'normal' && (
            <div className="player-grid">
              {game.players.map((p) => (
                <NormalPlayerCard
                  key={p.playerId}
                  player={p}
                  others={game.players.filter((x) => x.playerId !== p.playerId)}
                  input={inputs.find((i) => i.playerId === p.playerId)!}
                  onUpdate={(patch) => updateInput(p.playerId, patch)}
                  onSetRank={(rank) => setRank(p.playerId, rank)}
                />
              ))}
            </div>
          )}

          {!manualScoring && mode === 'whiteWin' && specialPlayerId && (
            <WhiteWinSummary
              winner={game.players.find((p) => p.playerId === specialPlayerId)!}
              others={game.players.filter((p) => p.playerId !== specialPlayerId)}
            />
          )}

          {!manualScoring && mode === 'judge' && specialPlayerId && (
            <JudgePanel
              judge={game.players.find((p) => p.playerId === specialPlayerId)!}
              others={game.players.filter((p) => p.playerId !== specialPlayerId)}
              inputs={inputs}
              onUpdate={updateInput}
            />
          )}

          {manualScoring && (
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
                        type="number"
                        inputMode="numeric"
                        value={input.manualScore ?? ''}
                        onChange={(e) =>
                          updateInput(p.playerId, {
                            manualScore: e.target.value === '' ? null : Number(e.target.value)
                          })
                        }
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {manualScoring && (
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
              Tổng điểm 4 người: <strong>{formatScore(manualSum)}</strong> {manualValid ? '(hợp lệ)' : '— phải bằng 0'}
            </div>
          )}

          <button
            onClick={submitRound}
            disabled={submitting || !manualValid}
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
  onUpdate,
  onSetRank
}: {
  player: GamePlayer;
  others: GamePlayer[];
  input: PlayerInputState;
  onUpdate: (patch: Partial<PlayerInputState>) => void;
  onSetRank: (rank: number | null) => void;
}) {
  const cardClass = `player-card ${input.rank === 1 ? 'has-rank-1' : ''} ${input.rank === 4 ? 'has-rank-4' : ''}`;
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
          {[1, 2, 3, 4].map((r) => (
            <button
              key={r}
              type="button"
              className={input.rank === r ? 'active' : ''}
              onClick={() => onSetRank(input.rank === r ? null : r)}
            >
              #{r}
            </button>
          ))}
        </div>
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
  onUpdate
}: {
  judge: GamePlayer;
  others: GamePlayer[];
  inputs: PlayerInputState[];
  onUpdate: (playerId: string, patch: Partial<PlayerInputState>) => void;
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
  const totalExtra = others.reduce((s, p) => s + heldExtra(p.playerId), 0);
  const judgeFinal = 12 + totalExtra;

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
            <div className="muted small">+12 điểm cơ bản • +{totalExtra} từ bài đối thủ • <strong>Tổng dự kiến: {formatScore(judgeFinal)}</strong></div>
          </div>
        </div>
      </div>

      <div className="section-title">Bài còn trên tay 3 đối thủ</div>
      <div className="muted tiny mb-1">
        <Icon name="info" size={11} /> Tick những lá bài đối thủ đang giữ. Mỗi heo đen +1, đỏ +2; 3 đôi thông +3; tứ quý +4; 4 đôi thông +5 sẽ được cộng vào điểm phán xét và trừ vào điểm đối thủ.
      </div>

      <div className="player-grid">
        {others.map((p) => {
          const i = inputFor(p.playerId);
          const extra = heldExtra(p.playerId);
          return (
            <div key={p.playerId} className="player-card">
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
    </div>
  );
}

function SimpleToggle({
  label,
  checked,
  onChange
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
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
        transition: 'all var(--transition)'
      }}
    >
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}>
        <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="small">{label}</span>
      </span>
    </label>
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
