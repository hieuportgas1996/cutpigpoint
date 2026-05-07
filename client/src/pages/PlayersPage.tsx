import { useEffect, useState } from 'react';
import { api, Player } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { initials } from '../ui/helpers';

export default function PlayersPage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [name, setName] = useState('');
  const [nickname, setNickname] = useState('');
  const [editId, setEditId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const toast = useToast();

  async function refresh() {
    try {
      setPlayers(await api.listPlayers());
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    try {
      if (editId) {
        await api.updatePlayer(editId, name, nickname || undefined);
        toast.push('success', `Đã cập nhật ${name}`);
      } else {
        await api.createPlayer(name, nickname || undefined);
        toast.push('success', `Đã thêm ${name}`);
      }
      setName('');
      setNickname('');
      setEditId(null);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  function edit(p: Player) {
    setEditId(p.id);
    setName(p.name);
    setNickname(p.nickname ?? '');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function cancelEdit() {
    setEditId(null);
    setName('');
    setNickname('');
  }

  async function remove(p: Player) {
    if (!confirm(`Xoá người chơi "${p.name}"?`)) return;
    try {
      await api.deletePlayer(p.id);
      toast.push('info', `Đã xoá ${p.name}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Người chơi</h1>
          <div className="muted small">Quản lý danh sách người chơi để thêm vào ván</div>
        </div>
        <span className="status done"><Icon name="users" size={14} />{players.length} người</span>
      </div>

      <div className="card">
        <h3 style={{ marginBottom: '0.85rem' }}>
          {editId ? 'Sửa người chơi' : 'Thêm người chơi'}
        </h3>
        <form onSubmit={submit}>
          <div className="form-row">
            <div>
              <label htmlFor="p-name">Tên</label>
              <input
                id="p-name"
                placeholder="Ví dụ: Nguyễn Văn A"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                autoComplete="off"
              />
            </div>
            <div>
              <label htmlFor="p-nick">Biệt danh <span className="dim">(tuỳ chọn)</span></label>
              <input
                id="p-nick"
                placeholder="Ví dụ: A Cá"
                value={nickname}
                onChange={(e) => setNickname(e.target.value)}
                autoComplete="off"
              />
            </div>
          </div>
          <div className="row mt-2">
            <button type="submit" className="block-mobile">
              <Icon name={editId ? 'check' : 'plus'} size={16} />
              {editId ? 'Cập nhật' : 'Thêm người chơi'}
            </button>
            {editId && (
              <button type="button" className="ghost block-mobile" onClick={cancelEdit}>
                Huỷ
              </button>
            )}
          </div>
        </form>
      </div>

      <div className="card card-flush">
        {loading ? (
          <div className="empty">
            <div className="empty-icon"><Icon name="clock" /></div>
            <div>Đang tải…</div>
          </div>
        ) : players.length === 0 ? (
          <div className="empty">
            <div className="empty-icon"><Icon name="users" /></div>
            <div>Chưa có người chơi nào</div>
            <div className="small dim mt-1">Thêm người chơi đầu tiên ở form phía trên</div>
          </div>
        ) : (
          <div style={{ padding: '0.5rem 0' }}>
            {players.map((p) => (
              <div
                key={p.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.85rem',
                  padding: '0.7rem 1rem',
                  borderBottom: '1px solid var(--border)'
                }}
              >
                <div className="avatar">{initials(p.name)}</div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="bold" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {p.name}
                  </div>
                  {p.nickname && <div className="small muted">{p.nickname}</div>}
                </div>
                <button className="ghost icon-only" onClick={() => edit(p)} aria-label="Sửa">
                  <Icon name="edit" size={16} />
                </button>
                <button className="danger icon-only" onClick={() => remove(p)} aria-label="Xoá">
                  <Icon name="trash" size={16} />
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
