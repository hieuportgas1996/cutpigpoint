import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, Game, PlayerRoundInput } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { formatScore, initials, scoreClass, formatDateTime } from '../ui/helpers';

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
    fourOfAKind: false,
    fourPairsStraight: false,
    whiteWin: false,
    manualScore: null
  };
}

export default function GamePlayPage() {
  const { id } = useParams<{ id: string }>();
  const [game, setGame] = useState<Game | null>(null);
  const [manualScoring, setManualScoring] = useState(false);
  const [inputs, setInputs] = useState<PlayerInputState[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const toast = useToast();

  const refresh = useCallback(async () => {
    if (!id) return;
    try {
      const g = await api.getGame(id);
      setGame(g);
      setInputs(g.players.map((p) => emptyInput(p.playerId)));
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }, [id, toast]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const ranking = useMemo(() => {
    if (!game) return [];
    return [...game.players].sort((a, b) => b.totalScore - a.totalScore);
  }, [game]);

  function updateInput(playerId: string, patch: Partial<PlayerInputState>) {
    setInputs((prev) =>
      prev.map((it) => (it.playerId === playerId ? { ...it, ...patch } : it))
    );
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

  async function submitRound() {
    if (!id) return;
    setSubmitting(true);
    try {
      await api.addRound(id, manualScoring, inputs);
      toast.push('success', `Đã lưu round #${(game?.rounds.length ?? 0) + 1}`);
      await refresh();
      setManualScoring(false);
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
              <div className="avatar sm">{initials(p.name)}</div>
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
                onChange={(e) => setManualScoring(e.target.checked)}
              />
              <span className="small">Nhập điểm thủ công</span>
            </label>
          </div>

          <div className="player-grid">
            {game.players.map((p) => {
              const input = inputs.find((i) => i.playerId === p.playerId)!;
              const cardClass = `player-card ${input.rank === 1 ? 'has-rank-1' : ''} ${input.rank === 4 ? 'has-rank-4' : ''}`;
              return (
                <div key={p.playerId} className={cardClass}>
                  <div className="player-card-head">
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                      <div className="avatar sm">{initials(p.name)}</div>
                      <h4>{p.name}</h4>
                    </div>
                    {input.rank && <div className={`rank-badge r${input.rank}`}>#{input.rank}</div>}
                  </div>

                  {!manualScoring && (
                    <>
                      <div>
                        <div className="section-title">Hạng</div>
                        <div className="pill-group">
                          {[1, 2, 3, 4].map((r) => (
                            <button
                              key={r}
                              type="button"
                              className={input.rank === r ? 'active' : ''}
                              onClick={() => setRank(p.playerId, input.rank === r ? null : r)}
                            >
                              #{r}
                            </button>
                          ))}
                        </div>
                      </div>

                      <div>
                        <div className="section-title">Heo bị chặt (mất điểm)</div>
                        <Stepper
                          label="Heo đen"
                          value={input.blackPigsLost}
                          onChange={(v) => updateInput(p.playerId, { blackPigsLost: v })}
                        />
                        <Stepper
                          label="Heo đỏ"
                          value={input.redPigsLost}
                          onChange={(v) => updateInput(p.playerId, { redPigsLost: v })}
                        />
                      </div>

                      <div>
                        <div className="section-title">Heo chặt được (ăn điểm)</div>
                        <Stepper
                          label="Heo đen"
                          value={input.blackPigsCut}
                          onChange={(v) => updateInput(p.playerId, { blackPigsCut: v })}
                        />
                        <Stepper
                          label="Heo đỏ"
                          value={input.redPigsCut}
                          onChange={(v) => updateInput(p.playerId, { redPigsCut: v })}
                        />
                      </div>

                      <div>
                        <div className="section-title">Bonus</div>
                        <div className="col gap-sm">
                          <BonusToggle
                            label="3 đôi thông"
                            value="+3"
                            checked={input.threePairsStraight}
                            onChange={(v) => updateInput(p.playerId, { threePairsStraight: v })}
                          />
                          <BonusToggle
                            label="Tứ quý"
                            value="+4"
                            checked={input.fourOfAKind}
                            onChange={(v) => updateInput(p.playerId, { fourOfAKind: v })}
                          />
                          <BonusToggle
                            label="4 đôi thông"
                            value="+5"
                            checked={input.fourPairsStraight}
                            onChange={(v) => updateInput(p.playerId, { fourPairsStraight: v })}
                          />
                          <BonusToggle
                            label="Về trắng"
                            value="+6"
                            checked={input.whiteWin}
                            onChange={(v) => updateInput(p.playerId, { whiteWin: v })}
                          />
                        </div>
                      </div>
                    </>
                  )}

                  {manualScoring && (
                    <div>
                      <label htmlFor={`m-${p.playerId}`}>Điểm cho người này</label>
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
                  )}
                </div>
              );
            })}
          </div>

          <button onClick={submitRound} disabled={submitting} className="block-mobile mt-2">
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
                {game.rounds.map((r) => (
                  <tr key={r.id}>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                        <span className="bold">#{r.roundNumber}</span>
                        {r.manualScoring && <span className="tiny dim">TC</span>}
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
                        <button className="ghost icon-only" onClick={() => deleteRound(r.id, r.roundNumber)} aria-label="Xoá">
                          <Icon name="trash" size={14} />
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

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

function BonusToggle({
  label,
  value,
  checked,
  onChange
}: {
  label: string;
  value: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label className="inline" style={{
      padding: '0.5rem 0.7rem',
      background: checked ? 'var(--accent-grad-soft)' : 'var(--bg-1)',
      border: `1px solid ${checked ? 'var(--accent)' : 'var(--border)'}`,
      borderRadius: 'var(--radius-sm)',
      width: '100%',
      justifyContent: 'space-between',
      transition: 'all var(--transition)'
    }}>
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }}>
        <input
          type="checkbox"
          checked={checked}
          onChange={(e) => onChange(e.target.checked)}
        />
        <span className="small">{label}</span>
      </span>
      <span className="tiny dim bold">{value}</span>
    </label>
  );
}
