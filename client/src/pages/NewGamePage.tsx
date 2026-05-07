import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, Player } from '../api';

export default function NewGamePage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [seats, setSeats] = useState<Array<string | ''>>(['', '', '', '']);
  const [error, setError] = useState<string | null>(null);
  const nav = useNavigate();

  useEffect(() => {
    api.listPlayers().then(setPlayers).catch((e) => setError((e as Error).message));
  }, []);

  const allSelected = seats.every((s) => s !== '');
  const unique = new Set(seats.filter(Boolean)).size === seats.filter(Boolean).length;

  async function start() {
    setError(null);
    if (!allSelected) {
      setError('Phải chọn đủ 4 người chơi.');
      return;
    }
    if (!unique) {
      setError('Người chơi không được trùng nhau.');
      return;
    }
    try {
      const g = await api.createGame(seats as string[]);
      nav(`/games/${g.id}`);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  function setSeat(i: number, id: string) {
    setSeats((prev) => prev.map((v, idx) => (idx === i ? id : v)));
  }

  return (
    <div>
      <h1>Tạo ván Tiến Lên Miền Nam</h1>
      <div className="card">
        <p className="muted small">Chọn 4 người chơi cho ván này.</p>
        <div className="player-grid">
          {seats.map((seat, i) => (
            <div key={i} className="col">
              <label>Vị trí {i + 1}</label>
              <select value={seat} onChange={(e) => setSeat(i, e.target.value)}>
                <option value="">— chọn —</option>
                {players.map((p) => (
                  <option key={p.id} value={p.id}>{p.name}{p.nickname ? ` (${p.nickname})` : ''}</option>
                ))}
              </select>
            </div>
          ))}
        </div>
        {error && <div className="error">{error}</div>}
        <div className="row" style={{ marginTop: '1rem' }}>
          <button onClick={start} disabled={!allSelected || !unique}>Bắt đầu ván</button>
        </div>
      </div>
    </div>
  );
}
