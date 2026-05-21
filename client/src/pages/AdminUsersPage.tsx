import { useEffect, useState } from 'react';
import { api, AdminUser } from '../api';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';
import { useAuth } from '../auth/AuthContext';

export default function AdminUsersPage() {
  const { state } = useAuth();
  const toast = useToast();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);

  const [newUsername, setNewUsername] = useState('');
  const [newDisplayName, setNewDisplayName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newIsAdmin, setNewIsAdmin] = useState(false);
  const [creating, setCreating] = useState(false);

  const [resetPwId, setResetPwId] = useState<string | null>(null);
  const [resetPwValue, setResetPwValue] = useState('');

  async function refresh() {
    try {
      setUsers(await api.listAdminUsers());
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { refresh(); }, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!newUsername.trim() || !newPassword) return;
    setCreating(true);
    try {
      await api.createAdminUser({
        username: newUsername.trim(),
        password: newPassword,
        displayName: newDisplayName.trim() || undefined,
        isAdmin: newIsAdmin
      });
      toast.push('success', `Đã tạo user ${newUsername}. Đưa username/password cho người chơi.`);
      setNewUsername('');
      setNewDisplayName('');
      setNewPassword('');
      setNewIsAdmin(false);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setCreating(false);
    }
  }

  async function handleToggleAdmin(u: AdminUser) {
    try {
      await api.updateAdminUser(u.id, { isAdmin: !u.isAdmin });
      toast.push('success', !u.isAdmin ? `Đã cấp quyền admin cho ${u.username}` : `Đã bỏ quyền admin của ${u.username}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handleResetPassword(id: string) {
    if (!resetPwValue || resetPwValue.length < 4) {
      toast.push('error', 'Mật khẩu phải có ít nhất 4 ký tự.');
      return;
    }
    try {
      await api.updateAdminUser(id, { password: resetPwValue });
      toast.push('success', 'Đã đặt lại mật khẩu. Người chơi cần đăng nhập lại.');
      setResetPwId(null);
      setResetPwValue('');
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  async function handleDelete(u: AdminUser) {
    if (!confirm(`Xoá user ${u.username}? Không thể hoàn tác.`)) return;
    try {
      await api.deleteAdminUser(u.id);
      toast.push('success', `Đã xoá ${u.username}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  if (state.status !== 'authenticated' || !state.isAdmin) {
    return (
      <div className="card">
        <div className="muted"><Icon name="flag" size={14} /> Chỉ admin mới truy cập được trang này.</div>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div className="card">
        <h2 style={{ marginTop: 0 }}>Tạo người chơi mới</h2>
        <p className="muted small">Tạo username/password rồi đưa cho người chơi để họ đăng nhập.</p>
        <form onSubmit={handleCreate} style={{ display: 'grid', gap: 10 }}>
          <div>
            <label>Tên đăng nhập</label>
            <input value={newUsername} onChange={e => setNewUsername(e.target.value)} placeholder="vd: hoanguyen" required />
          </div>
          <div>
            <label>Tên hiển thị (tuỳ chọn)</label>
            <input value={newDisplayName} onChange={e => setNewDisplayName(e.target.value)} placeholder="vd: Hoa Nguyễn" />
          </div>
          <div>
            <label>Mật khẩu</label>
            <input type="text" value={newPassword} onChange={e => setNewPassword(e.target.value)} placeholder="Tối thiểu 4 ký tự" required />
          </div>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <input type="checkbox" checked={newIsAdmin} onChange={e => setNewIsAdmin(e.target.checked)} />
            <span>Cấp quyền admin</span>
          </label>
          <button type="submit" disabled={creating || !newUsername || !newPassword}>
            {creating ? 'Đang tạo…' : 'Tạo người chơi'}
          </button>
        </form>
      </div>

      <div className="card">
        <h2 style={{ marginTop: 0 }}>Danh sách người chơi</h2>
        {loading ? (
          <div className="muted">Đang tải…</div>
        ) : users.length === 0 ? (
          <div className="muted">Chưa có người chơi nào.</div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {users.map(u => (
              <div key={u.id} className="leader-row" style={{ alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
                <div style={{ flex: 1, minWidth: 200 }}>
                  <div style={{ fontWeight: 700 }}>
                    {u.displayName}
                    {u.isAdmin && <span className="score-pill pos" style={{ marginLeft: 8, fontSize: 11 }}>ADMIN</span>}
                    {u.id === state.userId && <span className="muted small" style={{ marginLeft: 8 }}>(bạn)</span>}
                  </div>
                  <div className="muted small">@{u.username}</div>
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  {resetPwId === u.id ? (
                    <>
                      <input
                        type="text"
                        value={resetPwValue}
                        onChange={e => setResetPwValue(e.target.value)}
                        placeholder="Mật khẩu mới"
                        style={{ width: 160 }}
                      />
                      <button className="sm" onClick={() => handleResetPassword(u.id)}>Lưu</button>
                      <button className="sm ghost" onClick={() => { setResetPwId(null); setResetPwValue(''); }}>Huỷ</button>
                    </>
                  ) : (
                    <>
                      <button className="sm ghost" onClick={() => { setResetPwId(u.id); setResetPwValue(''); }}>
                        Đặt lại mật khẩu
                      </button>
                      {u.id !== state.userId && (
                        <button className="sm ghost" onClick={() => handleToggleAdmin(u)}>
                          {u.isAdmin ? 'Bỏ admin' : 'Cấp admin'}
                        </button>
                      )}
                      {u.id !== state.userId && (
                        <button className="sm danger" onClick={() => handleDelete(u)}>Xoá</button>
                      )}
                    </>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
