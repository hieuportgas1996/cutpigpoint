import { useEffect, useState } from 'react';
import { api, Player } from '../api';

export default function PlayersPage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [name, setName] = useState('');
  const [nickname, setNickname] = useState('');
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    try {
      setPlayers(await api.listPlayers());
    } catch (e) {
      setError((e as Error).message);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      if (editId) {
        await api.updatePlayer(editId, name, nickname || undefined);
      } else {
        await api.createPlayer(name, nickname || undefined);
      }
      setName('');
      setNickname('');
      setEditId(null);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  function edit(p: Player) {
    setEditId(p.id);
    setName(p.name);
    setNickname(p.nickname ?? '');
  }

  async function remove(p: Player) {
    if (!confirm(`Xoá người chơi "${p.name}"?`)) return;
    try {
      await api.deletePlayer(p.id);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div>
      <h1>Người chơi</h1>
      <div className="card">
        <h3>{editId ? 'Sửa người chơi' : 'Thêm người chơi'}</h3>
        <form onSubmit={submit} className="row">
          <input
            placeholder="Tên"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
          />
          <input
            placeholder="Biệt danh (tuỳ chọn)"
            value={nickname}
            onChange={(e) => setNickname(e.target.value)}
          />
          <button type="submit">{editId ? 'Cập nhật' : 'Thêm'}</button>
          {editId && (
            <button
              type="button"
              className="secondary"
              onClick={() => {
                setEditId(null);
                setName('');
                setNickname('');
              }}
            >
              Huỷ
            </button>
          )}
        </form>
        {error && <div className="error">{error}</div>}
      </div>

      <div className="card">
        <table>
          <thead>
            <tr>
              <th>Tên</th>
              <th>Biệt danh</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {players.map((p) => (
              <tr key={p.id}>
                <td>{p.name}</td>
                <td className="muted">{p.nickname || '—'}</td>
                <td style={{ textAlign: 'right' }}>
                  <button className="secondary" onClick={() => edit(p)}>Sửa</button>{' '}
                  <button className="danger" onClick={() => remove(p)}>Xoá</button>
                </td>
              </tr>
            ))}
            {players.length === 0 && (
              <tr>
                <td colSpan={3} className="muted">Chưa có người chơi</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
