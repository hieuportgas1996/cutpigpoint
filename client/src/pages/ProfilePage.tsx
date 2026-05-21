import { useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';

export default function ProfilePage() {
  const { state } = useAuth();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);

  if (state.status !== 'authenticated') {
    return <div className="muted">Chưa đăng nhập.</div>;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (next !== confirm) {
      toast.push('error', 'Mật khẩu xác nhận không khớp.');
      return;
    }
    if (next.length < 4) {
      toast.push('error', 'Mật khẩu mới phải có ít nhất 4 ký tự.');
      return;
    }
    setBusy(true);
    try {
      await api.changePassword(current, next);
      toast.push('success', 'Đã đổi mật khẩu. Các phiên đăng nhập khác đã bị đăng xuất.');
      setCurrent(''); setNext(''); setConfirm('');
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 480, margin: '0 auto' }}>
      <div className="card">
        <h2 style={{ marginTop: 0 }}>Tài khoản</h2>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div><span className="muted small">Tên đăng nhập:</span> <b>{state.username}</b></div>
          <div><span className="muted small">Tên hiển thị:</span> <b>{state.displayName}</b></div>
          {state.isAdmin && <div><span className="score-pill pos">ADMIN</span></div>}
        </div>
      </div>

      <div className="card">
        <h2 style={{ marginTop: 0 }}><Icon name="flag" size={16} /> Đổi mật khẩu</h2>
        <form onSubmit={handleSubmit} style={{ display: 'grid', gap: 10 }}>
          <div>
            <label>Mật khẩu hiện tại</label>
            <input type="password" value={current} onChange={e => setCurrent(e.target.value)} required />
          </div>
          <div>
            <label>Mật khẩu mới</label>
            <input type="password" value={next} onChange={e => setNext(e.target.value)} required />
          </div>
          <div>
            <label>Xác nhận mật khẩu mới</label>
            <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} required />
          </div>
          <button type="submit" disabled={busy || !current || !next || !confirm}>
            {busy ? 'Đang đổi…' : 'Đổi mật khẩu'}
          </button>
        </form>
      </div>
    </div>
  );
}
