import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

interface GameSummary {
  id: string;
  startedAt: string;
  finishedAt: string | null;
  players: { playerId: string; name: string; seat: number }[];
}

export default function GamesPage() {
  const [games, setGames] = useState<GameSummary[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.listGames().then(setGames).catch((e) => setError((e as Error).message));
  }, []);

  return (
    <div>
      <h1>Ván chơi</h1>
      {error && <div className="error">{error}</div>}
      <div className="card">
        {games.length === 0 ? (
          <p className="muted">
            Chưa có ván nào. <Link to="/new">Tạo ván mới</Link>
          </p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Bắt đầu</th>
                <th>Người chơi</th>
                <th>Trạng thái</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {games.map((g) => (
                <tr key={g.id}>
                  <td>{new Date(g.startedAt).toLocaleString('vi-VN')}</td>
                  <td>{g.players.map((p) => p.name).join(', ')}</td>
                  <td>
                    {g.finishedAt ? (
                      <span className="muted">Đã kết thúc</span>
                    ) : (
                      <span style={{ color: 'var(--success)' }}>Đang chơi</span>
                    )}
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <Link to={`/games/${g.id}`}>
                      <button className="secondary">Mở</button>
                    </Link>
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
