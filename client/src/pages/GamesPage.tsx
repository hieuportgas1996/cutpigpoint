import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { initials, relativeTime } from '../ui/helpers';

interface GameSummary {
  id: string;
  startedAt: string;
  finishedAt: string | null;
  players: { playerId: string; name: string; seat: number }[];
}

export default function GamesPage() {
  const [games, setGames] = useState<GameSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const toast = useToast();

  useEffect(() => {
    api.listGames()
      .then(setGames)
      .catch((e) => toast.push('error', (e as Error).message))
      .finally(() => setLoading(false));
  }, [toast]);

  const liveGames = games.filter((g) => !g.finishedAt);
  const doneGames = games.filter((g) => g.finishedAt);

  return (
    <div>
      <div className="hero">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '1rem', flexWrap: 'wrap' }}>
          <div>
            <h1 style={{ marginBottom: 0 }}>Tiến Lên Miền Nam</h1>
            <div className="muted">Tính điểm tự động • Lưu trữ lịch sử ván chơi</div>
          </div>
          <Link to="/new">
            <button className="block-mobile">
              <Icon name="plus" size={16} /> Ván mới
            </button>
          </Link>
        </div>
      </div>

      {loading ? (
        <div className="card empty">
          <div className="empty-icon"><Icon name="clock" /></div>
          <div>Đang tải…</div>
        </div>
      ) : games.length === 0 ? (
        <div className="card empty">
          <div className="empty-icon"><Icon name="cards" /></div>
          <div className="bold">Chưa có ván nào</div>
          <div className="small dim mt-1">Tạo ván đầu tiên để bắt đầu</div>
          <div className="mt-2">
            <Link to="/new"><button><Icon name="plus" size={16} />Tạo ván mới</button></Link>
          </div>
        </div>
      ) : (
        <>
          {liveGames.length > 0 && (
            <>
              <div className="section-title">Đang chơi ({liveGames.length})</div>
              <div className="col">
                {liveGames.map((g) => <GameRow key={g.id} g={g} />)}
              </div>
            </>
          )}
          {doneGames.length > 0 && (
            <>
              <div className="section-title mt-3">Đã kết thúc ({doneGames.length})</div>
              <div className="col">
                {doneGames.map((g) => <GameRow key={g.id} g={g} />)}
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}

function GameRow({ g }: { g: GameSummary }) {
  return (
    <Link to={`/games/${g.id}`} style={{ color: 'inherit' }}>
      <div className="card" style={{ marginBottom: 0, cursor: 'pointer' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '0.75rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '-0.4rem' }}>
            <div style={{ display: 'flex' }}>
              {g.players.map((p, i) => (
                <div
                  key={p.playerId}
                  className="avatar sm"
                  style={{
                    marginLeft: i === 0 ? 0 : -8,
                    border: '2px solid var(--bg-elev)',
                    zIndex: 4 - i
                  }}
                  title={p.name}
                >
                  {initials(p.name)}
                </div>
              ))}
            </div>
            <div style={{ marginLeft: '0.85rem' }}>
              <div className="bold">{g.players.map((p) => p.name).join(' • ')}</div>
              <div className="small dim">
                <Icon name="clock" size={12} /> {relativeTime(g.startedAt)}
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <span className={`status ${g.finishedAt ? 'done' : 'live'}`}>
              {g.finishedAt ? 'Đã xong' : 'Đang chơi'}
            </span>
            <Icon name="chevron-right" size={16} />
          </div>
        </div>
      </div>
    </Link>
  );
}
