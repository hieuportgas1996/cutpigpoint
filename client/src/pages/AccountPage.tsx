import { FormEvent, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';

export default function AccountPage() {
  const { state, setUsername, logout } = useAuth();
  const toast = useToast();

  const currentUsername = state.status === 'authenticated' ? state.username : '';

  const [newUsername, setNewUsername] = useState(currentUsername);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const usernameChanged = newUsername.trim() !== currentUsername && newUsername.trim().length > 0;
  const passwordChanged = newPassword.length > 0;
  const passwordMatch = !passwordChanged || newPassword === confirmPassword;
  const canSubmit = !!currentPassword && (usernameChanged || passwordChanged) && passwordMatch && !submitting;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    try {
      const res = await api.updateAccount(
        currentPassword,
        usernameChanged ? newUsername.trim() : null,
        passwordChanged ? newPassword : null
      );
      setUsername(res.username);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      if (passwordChanged) {
        toast.push('success', 'Đã đổi mật khẩu, vui lòng đăng nhập lại');
        setTimeout(() => logout(), 1000);
      } else {
        toast.push('success', 'Đã cập nhật tài khoản');
      }
    } catch (err) {
      toast.push('error', (err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Tài khoản</h1>
          <div className="muted small">Đổi tên đăng nhập hoặc mật khẩu</div>
        </div>
      </div>

      <form onSubmit={onSubmit} className="card" style={{ maxWidth: 480 }}>
        <div className="section-title">Tên đăng nhập</div>
        <input
          type="text"
          value={newUsername}
          onChange={(e) => setNewUsername(e.target.value)}
          autoComplete="username"
        />

        <div className="section-title mt-2">Đổi mật khẩu (tuỳ chọn)</div>
        <div>
          <label htmlFor="new-password">Mật khẩu mới</label>
          <input
            id="new-password"
            type="password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder="Để trống nếu không đổi"
          />
        </div>
        <div className="mt-1">
          <label htmlFor="confirm-password">Nhập lại mật khẩu mới</label>
          <input
            id="confirm-password"
            type="password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
          />
        </div>
        {passwordChanged && !passwordMatch && (
          <div className="muted tiny mt-1" style={{ color: 'var(--danger)' }}>
            <Icon name="alert" size={11} /> Mật khẩu nhập lại không khớp
          </div>
        )}

        <div className="section-title mt-2">Xác nhận bằng mật khẩu hiện tại</div>
        <input
          type="password"
          autoComplete="current-password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          required
        />

        <button type="submit" disabled={!canSubmit} className="mt-2 block-mobile">
          <Icon name="check" size={14} />
          {submitting ? 'Đang lưu…' : 'Lưu thay đổi'}
        </button>
      </form>

      <div className="card mt-2" style={{ maxWidth: 480 }}>
        <div className="section-title">Phiên đăng nhập</div>
        <div className="muted small">Token hết hạn sau 24 giờ kể từ lần đăng nhập gần nhất.</div>
        <button type="button" className="danger mt-2" onClick={() => logout()}>
          <Icon name="flag" size={14} /> Đăng xuất
        </button>
      </div>
    </div>
  );
}
