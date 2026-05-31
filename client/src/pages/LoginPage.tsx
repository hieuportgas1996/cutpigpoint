import { FormEvent, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { Icon } from '../ui/Icon';

export default function LoginPage() {
  const { login } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await login(username.trim(), password);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1rem'
      }}
    >
      <form
        onSubmit={onSubmit}
        className="card"
        style={{ width: '100%', maxWidth: 380, marginBottom: 0 }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1rem' }}>
          <span className="brand-icon"><Icon name="cards" size={18} /></span>
          <h2 style={{ margin: 0 }}>Cut Pig</h2>
        </div>
        <div>
          <label htmlFor="username">Tên đăng nhập</label>
          <input
            id="username"
            type="text"
            autoComplete="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            required
            autoFocus
          />
        </div>

        <div className="mt-1">
          <label htmlFor="password">Mật khẩu</label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </div>

        {error && (
          <div
            className="mt-2"
            style={{
              padding: '0.55rem 0.75rem',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--danger)',
              background: 'var(--danger-bg)',
              color: 'var(--danger)',
              fontSize: '0.85rem'
            }}
          >
            <Icon name="alert" size={13} /> {error}
          </div>
        )}

        <button
          type="submit"
          className="block-mobile mt-2"
          style={{ width: '100%' }}
          disabled={submitting || !username.trim() || !password}
        >
          <Icon name="check" size={14} />
          {submitting ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>

        <div className="muted small" style={{ textAlign: 'center', marginTop: '1.25rem' }}>
          © 2026 Cut Pig by HieuDo. All rights reserved.
        </div>
      </form>
    </div>
  );
}
