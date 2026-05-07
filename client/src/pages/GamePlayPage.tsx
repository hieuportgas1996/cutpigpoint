import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, Game, PlayerRoundInput } from '../api';

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
  const [error, setError] = useState<string | null>(null);

  const [manualScoring, setManualScoring] = useState(false);
  const [inputs, setInputs] = useState<PlayerInputState[]>([]);
  const [submitting, setSubmitting] = useState(false);

  const refresh = useCallback(async () => {
    if (!id) return;
    try {
      const g = await api.getGame(id);
      setGame(g);
      setInputs(g.players.map((p) => emptyInput(p.playerId)));
    } catch (e) {
      setError((e as Error).message);
    }
  }, [id]);

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
    setError(null);
    setSubmitting(true);
    try {
      await api.addRound(id, manualScoring, inputs);
      await refresh();
      setManualScoring(false);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  async function deleteRound(roundId: string) {
    if (!id) return;
    if (!confirm('Xoá round này?')) return;
    try {
      await api.deleteRound(id, roundId);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function finishGame() {
    if (!id || !game) return;
    if (!confirm('Kết thúc ván này?')) return;
    try {
      const g = await api.finishGame(id);
      setGame(g);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  if (!game) return <p className="muted">Đang tải…</p>;

  const finished = !!game.finishedAt;

  return (
    <div>
      <h1>Ván Tiến Lên Miền Nam</h1>
      <p className="muted small">
        Bắt đầu: {new Date(game.startedAt).toLocaleString('vi-VN')}
        {finished && ` • Kết thúc: ${new Date(game.finishedAt!).toLocaleString('vi-VN')}`}
      </p>

      <div className="card">
        <h3>Bảng điểm</h3>
        <table>
          <thead>
            <tr>
              <th>Hạng</th>
              <th>Tên</th>
              <th style={{ textAlign: 'right' }}>Điểm</th>
            </tr>
          </thead>
          <tbody>
            {ranking.map((p, idx) => (
              <tr key={p.playerId}>
                <td className={`rank-${idx + 1}`}>#{idx + 1}</td>
                <td>{p.name}</td>
                <td style={{ textAlign: 'right' }}>
                  <span className={`score-pill ${p.totalScore > 0 ? 'pos' : p.totalScore < 0 ? 'neg' : ''}`}>
                    {p.totalScore > 0 ? `+${p.totalScore}` : p.totalScore}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!finished && (
          <div style={{ marginTop: '1rem' }}>
            <button className="danger" onClick={finishGame}>Kết thúc ván</button>
          </div>
        )}
      </div>

      {!finished && (
        <div className="card">
          <div className="row">
            <h3 style={{ margin: 0 }}>Thêm round #{game.rounds.length + 1}</h3>
            <div className="spacer" />
            <label>
              <input
                type="checkbox"
                checked={manualScoring}
                onChange={(e) => setManualScoring(e.target.checked)}
              />
              Nhập điểm thủ công
            </label>
          </div>

          <div className="player-grid" style={{ marginTop: '1rem' }}>
            {game.players.map((p) => {
              const input = inputs.find((i) => i.playerId === p.playerId)!;
              return (
                <div key={p.playerId} className="card" style={{ background: 'var(--panel-2)' }}>
                  <h4 style={{ margin: '0 0 0.5rem 0' }}>{p.name}</h4>

                  {!manualScoring && (
                    <>
                      <div className="row">
                        <span className="muted small">Hạng:</span>
                        {[1, 2, 3, 4].map((r) => (
                          <button
                            key={r}
                            type="button"
                            className={input.rank === r ? '' : 'secondary'}
                            onClick={() => setRank(p.playerId, input.rank === r ? null : r)}
                            style={{ padding: '0.3rem 0.6rem' }}
                          >
                            #{r}
                          </button>
                        ))}
                      </div>

                      <div className="col" style={{ marginTop: '0.6rem' }}>
                        <span className="muted small">Heo bị chặt (mất điểm):</span>
                        <div className="row">
                          <NumberInput
                            label="Heo đen"
                            value={input.blackPigsLost}
                            onChange={(v) => updateInput(p.playerId, { blackPigsLost: v })}
                          />
                          <NumberInput
                            label="Heo đỏ"
                            value={input.redPigsLost}
                            onChange={(v) => updateInput(p.playerId, { redPigsLost: v })}
                          />
                        </div>
                      </div>

                      <div className="col" style={{ marginTop: '0.6rem' }}>
                        <span className="muted small">Heo chặt được (ăn điểm):</span>
                        <div className="row">
                          <NumberInput
                            label="Heo đen"
                            value={input.blackPigsCut}
                            onChange={(v) => updateInput(p.playerId, { blackPigsCut: v })}
                          />
                          <NumberInput
                            label="Heo đỏ"
                            value={input.redPigsCut}
                            onChange={(v) => updateInput(p.playerId, { redPigsCut: v })}
                          />
                        </div>
                      </div>

                      <div className="col" style={{ marginTop: '0.6rem' }}>
                        <span className="muted small">Bonus:</span>
                        <label>
                          <input
                            type="checkbox"
                            checked={input.threePairsStraight}
                            onChange={(e) => updateInput(p.playerId, { threePairsStraight: e.target.checked })}
                          />
                          3 đôi thông (+3)
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            checked={input.fourOfAKind}
                            onChange={(e) => updateInput(p.playerId, { fourOfAKind: e.target.checked })}
                          />
                          Tứ quý (+4)
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            checked={input.fourPairsStraight}
                            onChange={(e) => updateInput(p.playerId, { fourPairsStraight: e.target.checked })}
                          />
                          4 đôi thông (+5)
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            checked={input.whiteWin}
                            onChange={(e) => updateInput(p.playerId, { whiteWin: e.target.checked })}
                          />
                          Về trắng (+6)
                        </label>
                      </div>
                    </>
                  )}

                  {manualScoring && (
                    <div className="col">
                      <label>Điểm thủ công</label>
                      <input
                        type="number"
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

          {error && <div className="error">{error}</div>}
          <div className="row" style={{ marginTop: '1rem' }}>
            <button onClick={submitRound} disabled={submitting}>
              {submitting ? 'Đang lưu…' : 'Lưu round'}
            </button>
          </div>
        </div>
      )}

      <div className="card">
        <h3>Lịch sử các round ({game.rounds.length})</h3>
        {game.rounds.length === 0 ? (
          <p className="muted">Chưa có round nào.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>#</th>
                {game.players.map((p) => (
                  <th key={p.playerId} style={{ textAlign: 'right' }}>{p.name}</th>
                ))}
                <th></th>
              </tr>
            </thead>
            <tbody>
              {game.rounds.map((r) => (
                <tr key={r.id}>
                  <td>{r.roundNumber}{r.manualScoring && <span className="muted small"> (TC)</span>}</td>
                  {game.players.map((p) => {
                    const res = r.results.find((rr) => rr.playerId === p.playerId);
                    const score = res?.score ?? 0;
                    return (
                      <td key={p.playerId} style={{ textAlign: 'right' }}>
                        <span className={`score-pill ${score > 0 ? 'pos' : score < 0 ? 'neg' : ''}`}>
                          {score > 0 ? `+${score}` : score}
                        </span>
                      </td>
                    );
                  })}
                  <td style={{ textAlign: 'right' }}>
                    {!finished && (
                      <button className="danger" onClick={() => deleteRound(r.id)}>Xoá</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

function NumberInput({
  label,
  value,
  onChange
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
}) {
  return (
    <label className="col" style={{ alignItems: 'flex-start' }}>
      <span className="small muted">{label}</span>
      <input
        type="number"
        min={0}
        value={value}
        onChange={(e) => onChange(Math.max(0, Number(e.target.value || 0)))}
        style={{ width: '5rem' }}
      />
    </label>
  );
}
