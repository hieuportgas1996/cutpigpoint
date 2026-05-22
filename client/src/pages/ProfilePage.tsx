import { useRef, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';
import { fileToAvatarDataUrl } from '../ui/image';

export default function ProfilePage() {
  const { state, refreshAvatar } = useAuth();
  const toast = useToast();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [avatarBusy, setAvatarBusy] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

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

  async function handleAvatarPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file || state.status !== 'authenticated') return;
    setAvatarBusy(true);
    try {
      const dataUrl = await fileToAvatarDataUrl(file);
      await api.setMyAvatar(dataUrl);
      refreshAvatar(true);
      toast.push('success', 'Đã cập nhật avatar.');
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setAvatarBusy(false);
    }
  }

  async function handleAvatarDelete() {
    if (state.status !== 'authenticated') return;
    if (!window.confirm('Xoá avatar hiện tại?')) return;
    setAvatarBusy(true);
    try {
      await api.deleteMyAvatar();
      refreshAvatar(false);
      toast.push('success', 'Đã xoá avatar.');
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setAvatarBusy(false);
    }
  }

  const avatarSrc = state.hasAvatar
    ? `${api.userAvatarUrl(state.userId)}?v=${state.avatarVersion}`
    : null;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 480, margin: '0 auto' }}>
      <div className="card">
        <h2 style={{ marginTop: 0 }}>Tài khoản</h2>
        <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
          <div className="profile-avatar">
            {avatarSrc ? (
              <img src={avatarSrc} alt={state.displayName} />
            ) : (
              <span>{state.displayName.charAt(0).toUpperCase()}</span>
            )}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: 1 }}>
            <div><span className="muted small">Tên đăng nhập:</span> <b>{state.username}</b></div>
            <div><span className="muted small">Tên hiển thị:</span> <b>{state.displayName}</b></div>
            {state.isAdmin && <div><span className="score-pill pos">ADMIN</span></div>}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
          <input
            type="file"
            accept="image/jpeg,image/png,image/webp"
            ref={fileInputRef}
            onChange={handleAvatarPick}
            style={{ display: 'none' }}
          />
          <button type="button" disabled={avatarBusy} onClick={() => fileInputRef.current?.click()}>
            {avatarBusy ? 'Đang xử lý…' : state.hasAvatar ? 'Đổi avatar' : 'Tải avatar lên'}
          </button>
          {state.hasAvatar && (
            <button type="button" className="ghost" disabled={avatarBusy} onClick={handleAvatarDelete}>
              Xoá avatar
            </button>
          )}
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
