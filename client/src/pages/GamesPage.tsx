import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, GameType, GameTypeValue } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { relativeTime } from '../ui/helpers';
import { Avatar } from '../ui/Avatar';

interface GameSummary {
  id: string;
  type: GameTypeValue;
  startedAt: string;
  finishedAt: string | null;
  players: { playerId: string; name: string; seat: number; hasAvatar: boolean }[];
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
            <h1 style={{ marginBottom: 0 }}>Tính điểm các trò chơi trí tuệ</h1>
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
                {doneGames.map((g) => (
                  <GameRow
                    key={g.id}
                    g={g}
                    onDelete={async () => {
                      if (!confirm('Xoá ván này? Không thể hoàn tác.')) return;
                      try {
                        await api.deleteGame(g.id);
                        setGames((prev) => prev.filter((x) => x.id !== g.id));
                        toast.push('info', 'Đã xoá ván');
                      } catch (e) {
                        toast.push('error', (e as Error).message);
                      }
                    }}
                  />
                ))}
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}

function GameRow({ g, onDelete }: { g: GameSummary; onDelete?: () => void }) {
  const typeLabel =
    g.type === GameType.Manual ? 'Tự do' :
    g.type === GameType.Bida9Ball ? 'Bida 9 Bi' :
    'Tiến Lên';
  const names = g.players.map((p) => p.name).join(' • ');
  return (
    <Link to={`/games/${g.id}`} style={{ color: 'inherit' }}>
      <div className="card game-row" style={{ marginBottom: 0, cursor: 'pointer' }}>
        <div className="game-row-avatars">
          {g.players.map((p, i) => (
            <div
              key={p.playerId}
              style={{
                marginLeft: i === 0 ? 0 : -8,
                zIndex: 10 - i,
                borderRadius: '50%',
                boxShadow: '0 0 0 2px var(--bg-elev)'
              }}
              title={p.name}
            >
              <Avatar playerId={p.playerId} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
            </div>
          ))}
        </div>
        <div className="game-row-info">
          <div className="bold game-row-names" title={names}>{names}</div>
          <div className="small dim game-row-meta">
            <span className="game-row-meta-item">
              <Icon name="clock" size={12} /> {relativeTime(g.startedAt)}
            </span>
            <span className="game-row-meta-item tiny">• {typeLabel}</span>
          </div>
        </div>
        <div className="game-row-aside">
          <span className={`status ${g.finishedAt ? 'done' : 'live'}`}>
            {g.finishedAt ? 'Đã xong' : 'Đang chơi'}
          </span>
          {onDelete && (
            <button
              className="ghost icon-only danger"
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                onDelete();
              }}
              aria-label="Xoá ván"
              title="Xoá ván"
            >
              <Icon name="trash" size={14} />
            </button>
          )}
          <Icon name="chevron-right" size={16} />
        </div>
      </div>
    </Link>
  );
}
