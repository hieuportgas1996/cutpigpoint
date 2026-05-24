import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, RoomHistory, RoomStatus, RoomSummary } from '../api';
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
  const [history, setHistory] = useState<RoomHistory[]>([]);
  const [historyPage, setHistoryPage] = useState(1);
  const HISTORY_PAGE_SIZE = 5;
  const [loading, setLoading] = useState(true);
  const [joinCode, setJoinCode] = useState('');
  const [creating, setCreating] = useState(false);
  const [maxSeats, setMaxSeats] = useState(4);
  const [roomName, setRoomName] = useState('');

  async function refresh() {
    try {
      const [rs, hs] = await Promise.all([api.listRooms(), api.listRoomHistory().catch(() => [])]);
      setRooms(rs);
      setHistory(hs);
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
      const room = await api.createRoom(1, maxSeats, roomName.trim() || undefined);
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
                <label>Tên phòng (tuỳ chọn)</label>
                <input
                  value={roomName}
                  onChange={e => setRoomName(e.target.value)}
                  placeholder="VD: Bàn nhậu cuối tuần"
                  maxLength={50}
                  style={{ width: '100%' }}
                />
              </div>
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
              <div>
                <label>Mã phòng</label>
                <input
                  value={joinCode}
                  onChange={e => setJoinCode(e.target.value.toUpperCase())}
                  placeholder="ABC123"
                  maxLength={6}
                  style={{ textTransform: 'uppercase', letterSpacing: 2, fontFamily: 'monospace', fontSize: 18, width: '100%' }}
                />
              </div>
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
                  <div style={{ fontWeight: 600 }}>
                    {r.name ? r.name : <span className="muted">Không tên</span>}
                  </div>
                  <div className="muted small">
                    Chủ phòng: {r.hostDisplayName} · {r.occupiedSeats}/{r.maxSeats} người · {STATUS_LABEL[r.status] ?? '?'}
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

      <div className="card">
        <h3 style={{ marginTop: 0 }}>Lịch sử phòng đã kết thúc</h3>
        {loading ? (
          <div className="muted">Đang tải…</div>
        ) : history.length === 0 ? (
          <div className="muted">Chưa có phòng nào đã kết thúc.</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {history.slice((historyPage - 1) * HISTORY_PAGE_SIZE, historyPage * HISTORY_PAGE_SIZE).map(h => (
              <div
                key={h.id}
                className="leader-row"
                style={{ flexDirection: 'column', alignItems: 'stretch', gap: 8, cursor: 'pointer' }}
                onClick={() => navigate(`/rooms/${h.code}/history`)}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <code style={{ fontSize: 16, fontWeight: 700, letterSpacing: 2 }}>{h.code}</code>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 600 }}>
                      {h.name ? h.name : <span className="muted">Không tên</span>}
                    </div>
                    <div className="muted small">
                      Chủ phòng: {h.hostDisplayName}
                      {h.finishedAt && ' · Kết thúc ' + new Date(h.finishedAt).toLocaleString('vi-VN')}
                    </div>
                  </div>
                </div>
                {h.finalScores.length > 0 && (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                    {h.finalScores.map((s, idx) => (
                      <div
                        key={s.userId}
                        style={{
                          padding: '4px 10px',
                          borderRadius: 8,
                          background: idx === 0 ? 'rgba(250, 204, 21, 0.18)' : 'rgba(148, 163, 184, 0.12)',
                          border: idx === 0 ? '1px solid rgba(250, 204, 21, 0.4)' : '1px solid rgba(148, 163, 184, 0.2)',
                          fontSize: 13,
                          display: 'flex',
                          gap: 8,
                          alignItems: 'center',
                        }}
                      >
                        <span style={{ fontWeight: 600 }}>{idx + 1}. {s.displayName}</span>
                        <span style={{ fontWeight: 700, color: s.totalScore > 0 ? '#4ade80' : s.totalScore < 0 ? '#f87171' : 'inherit' }}>
                          {s.totalScore > 0 ? '+' : ''}{s.totalScore}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            ))}
            {history.length > HISTORY_PAGE_SIZE && (
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, marginTop: 8 }}>
                <button
                  className="sm ghost"
                  disabled={historyPage <= 1}
                  onClick={() => setHistoryPage(p => Math.max(1, p - 1))}
                >
                  ← Trước
                </button>
                <span className="muted small">
                  Trang {historyPage} / {Math.ceil(history.length / HISTORY_PAGE_SIZE)}
                </span>
                <button
                  className="sm ghost"
                  disabled={historyPage >= Math.ceil(history.length / HISTORY_PAGE_SIZE)}
                  onClick={() => setHistoryPage(p => Math.min(Math.ceil(history.length / HISTORY_PAGE_SIZE), p + 1))}
                >
                  Sau →
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
