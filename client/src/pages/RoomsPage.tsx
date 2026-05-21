import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, RoomStatus, RoomSummary } from '../api';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';

const STATUS_LABEL: Record<number, string> = {
  0: 'Đang chờ',
  1: 'Đang chơi',
  2: 'Đã kết thúc',
};

export default function RoomsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const isAdmin = state.status === 'authenticated' && state.isAdmin;
  const [rooms, setRooms] = useState<RoomSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [joinCode, setJoinCode] = useState('');
  const [creating, setCreating] = useState(false);
  const [maxSeats, setMaxSeats] = useState(4);

  async function refresh() {
    try {
      setRooms(await api.listRooms());
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { refresh(); }, []);

  async function handleCreate() {
    setCreating(true);
    try {
      const room = await api.createRoom(1, maxSeats);
      toast.push('success', `Đã tạo phòng ${room.code}`);
      navigate(`/rooms/${room.code}`);
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setCreating(false);
    }
  }

  function handleJoin(e: React.FormEvent) {
    e.preventDefault();
    const code = joinCode.trim().toUpperCase();
    if (!code) return;
    navigate(`/rooms/${code}`);
  }

  async function handleDelete(r: RoomSummary) {
    const note = r.status === RoomStatus.Playing
      ? ' (đang chơi — sẽ ngắt ván)'
      : '';
    if (!confirm(`Xoá phòng ${r.code}${note}?`)) return;
    try {
      await api.deleteRoom(r.id);
      toast.push('success', `Đã xoá phòng ${r.code}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div className="card">
        <h2 style={{ marginTop: 0 }}>Phòng chơi online</h2>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }} className="rooms-actions">
          <div>
            <h3 style={{ marginTop: 0, fontSize: 14 }}>Tạo phòng mới</h3>
            <div style={{ display: 'grid', gap: 10 }}>
              <div>
                <label>Số ghế tối đa</label>
                <select value={maxSeats} onChange={e => setMaxSeats(Number(e.target.value))}>
                  <option value={2}>2 người</option>
                  <option value={3}>3 người</option>
                  <option value={4}>4 người</option>
                </select>
              </div>
              <button onClick={handleCreate} disabled={creating}>
                <Icon name="plus" size={14} /> {creating ? 'Đang tạo…' : 'Tạo phòng'}
              </button>
            </div>
          </div>
          <div>
            <h3 style={{ marginTop: 0, fontSize: 14 }}>Vào phòng bằng mã</h3>
            <form onSubmit={handleJoin} style={{ display: 'grid', gap: 10 }}>
              <input
                value={joinCode}
                onChange={e => setJoinCode(e.target.value.toUpperCase())}
                placeholder="ABC123"
                maxLength={6}
                style={{ textTransform: 'uppercase', letterSpacing: 2, fontFamily: 'monospace', fontSize: 18 }}
              />
              <button type="submit" disabled={!joinCode.trim()}>
                <Icon name="cards" size={14} /> Vào phòng
              </button>
            </form>
          </div>
        </div>
      </div>

      <div className="card">
        <h3 style={{ marginTop: 0 }}>
          {isAdmin ? 'Tất cả phòng (admin)' : 'Phòng đang chờ'}
        </h3>
        {loading ? (
          <div className="muted">Đang tải…</div>
        ) : rooms.length === 0 ? (
          <div className="muted">Chưa có phòng nào đang chờ. Tạo phòng để bắt đầu.</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {rooms.map(r => (
              <div key={r.id} className="leader-row" style={{ alignItems: 'center', gap: 12 }}>
                <code style={{ fontSize: 18, fontWeight: 700, letterSpacing: 2 }}>{r.code}</code>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>Chủ phòng: {r.hostDisplayName}</div>
                  <div className="muted small">
                    {r.occupiedSeats}/{r.maxSeats} người · {STATUS_LABEL[r.status] ?? '?'}
                  </div>
                </div>
                {r.status === RoomStatus.Waiting && (
                  <button className="sm" onClick={() => navigate(`/rooms/${r.code}`)}>Vào</button>
                )}
                {isAdmin && (
                  <button className="sm danger" onClick={() => handleDelete(r)}>Xoá</button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
