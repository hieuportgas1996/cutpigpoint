import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, Player } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { initials } from '../ui/helpers';
import { Avatar } from '../ui/Avatar';

export default function NewGamePage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [seats, setSeats] = useState<Array<string | ''>>(['', '', '', '']);
  const [submitting, setSubmitting] = useState(false);
  const nav = useNavigate();
  const toast = useToast();

  useEffect(() => {
    api.listPlayers().then(setPlayers).catch((e) => toast.push('error', (e as Error).message));
  }, [toast]);

  const allSelected = seats.every((s) => s !== '');
  const unique = new Set(seats.filter(Boolean)).size === seats.filter(Boolean).length;

  function setSeat(i: number, id: string) {
    setSeats((prev) => prev.map((v, idx) => (idx === i ? id : v)));
  }

  function pickPlayer(slotIndex: number, p: Player) {
    if (seats.includes(p.id) && seats[slotIndex] !== p.id) {
      const existingSlot = seats.indexOf(p.id);
      setSeats((prev) => {
        const next = [...prev];
        next[existingSlot] = next[slotIndex];
        next[slotIndex] = p.id;
        return next;
      });
      return;
    }
    setSeat(slotIndex, p.id);
  }

  async function start() {
    if (!allSelected) {
      toast.push('error', 'Phải chọn đủ 4 người chơi');
      return;
    }
    if (!unique) {
      toast.push('error', 'Người chơi không được trùng nhau');
      return;
    }
    setSubmitting(true);
    try {
      const g = await api.createGame(seats as string[]);
      nav(`/games/${g.id}`);
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  if (players.length < 4) {
    return (
      <div>
        <div className="page-header">
          <h1>Ván mới</h1>
        </div>
        <div className="card empty">
          <div className="empty-icon"><Icon name="users" /></div>
          <div className="bold">Cần ít nhất 4 người chơi</div>
          <div className="small dim mt-1">
            Hiện có {players.length} người. Hãy thêm người chơi trước.
          </div>
          <div className="mt-2">
            <Link to="/players"><button><Icon name="plus" size={16} />Thêm người chơi</button></Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Ván mới</h1>
          <div className="muted small">Chọn 4 người chơi cho ván Tiến Lên</div>
        </div>
      </div>

      <div className="card">
        <div className="section-title">Vị trí ngồi</div>
        <div className="player-grid mt-1">
          {seats.map((seat, i) => {
            const player = players.find((p) => p.id === seat);
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', padding: '0.6rem 0.75rem', background: 'var(--bg-2)', borderRadius: 'var(--radius)', border: `1px solid ${player ? 'var(--accent)' : 'var(--border)'}` }}>
                <div className={`rank-badge r${i + 1}`}>{i + 1}</div>
                {player ? (
                  <>
                    <Avatar playerId={player.id} name={player.name} hasAvatar={player.hasAvatar} size="sm" />
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div className="bold" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{player.name}</div>
                      {player.nickname && <div className="tiny muted">{player.nickname}</div>}
                    </div>
                    <button className="ghost icon-only" onClick={() => setSeat(i, '')} aria-label="Bỏ chọn">×</button>
                  </>
                ) : (
                  <div className="muted" style={{ flex: 1 }}>Chưa chọn</div>
                )}
              </div>
            );
          })}
        </div>
      </div>

      <div className="card">
        <div className="section-title">Chọn từ danh sách</div>
        <div className="muted small mb-1">Chạm vào người chơi để gán vào vị trí trống tiếp theo</div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', marginTop: '0.5rem' }}>
          {players.map((p) => {
            const seatIndex = seats.indexOf(p.id);
            const selected = seatIndex >= 0;
            return (
              <button
                key={p.id}
                type="button"
                className={selected ? '' : 'secondary'}
                onClick={() => {
                  if (selected) {
                    setSeat(seatIndex, '');
                    return;
                  }
                  const empty = seats.indexOf('');
                  if (empty < 0) {
                    toast.push('info', 'Đã đủ 4 người, bỏ chọn để thay');
                    return;
                  }
                  pickPlayer(empty, p);
                }}
                style={{ padding: '0.4rem 0.75rem 0.4rem 0.4rem', gap: '0.5rem' }}
              >
                {p.hasAvatar ? (
                  <Avatar playerId={p.id} name={p.name} hasAvatar size="sm" />
                ) : (
                  <span className="avatar sm" style={{ background: selected ? 'rgba(255,255,255,0.25)' : undefined }}>
                    {initials(p.name)}
                  </span>
                )}
                {p.name}
                {selected && <span className="tiny" style={{ opacity: 0.85 }}>#{seatIndex + 1}</span>}
              </button>
            );
          })}
        </div>
      </div>

      <button onClick={start} disabled={!allSelected || !unique || submitting} className="block-mobile">
        <Icon name="play" size={14} />
        {submitting ? 'Đang tạo…' : 'Bắt đầu ván'}
      </button>
    </div>
  );
}
