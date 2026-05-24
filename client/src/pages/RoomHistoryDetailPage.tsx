import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, RoomHistory } from '../api';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';
import { formatScore, scoreClass, formatDateTime, initials } from '../ui/helpers';

export default function RoomHistoryDetailPage() {
  const { code } = useParams<{ code: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const [history, setHistory] = useState<RoomHistory | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!code) return;
    let cancelled = false;
    (async () => {
      try {
        const h = await api.getRoomHistory(code);
        if (!cancelled) setHistory(h);
      } catch (e) {
        if (!cancelled) {
          setError((e as Error).message);
          toast.push('error', (e as Error).message);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [code]);

  if (loading) {
    return (
      <div className="container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '50vh' }}>
        <div className="muted"><Icon name="clock" size={14} /> Đang tải…</div>
      </div>
    );
  }

  if (error || !history) {
    return (
      <div className="card">
        <div className="muted">{error || 'Không tìm thấy phòng.'}</div>
        <div style={{ marginTop: 12 }}>
          <button className="sm" onClick={() => navigate('/rooms')}>← Quay lại</button>
        </div>
      </div>
    );
  }

  const champion = history.finalScores[0];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>{history.name || `Phòng ${history.code}`}</h1>
          <div className="muted small">
            <Icon name="clock" size={12} /> Bắt đầu {formatDateTime(history.createdAt)}
            {history.finishedAt && ` • Kết thúc ${formatDateTime(history.finishedAt)}`}
          </div>
        </div>
        <span className="status done">Đã kết thúc</span>
      </div>

      {champion && (
        <div className="hero" style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
          <div className="rank-badge r1" style={{ width: 56, height: 56, fontSize: '1.5rem' }}>
            <Icon name="trophy" size={28} />
          </div>
          <div>
            <div className="dim small">Người thắng</div>
            <div style={{ fontSize: '1.5rem', fontWeight: 800 }}>{champion.displayName}</div>
            <div className="muted small">{formatScore(champion.totalScore)} điểm</div>
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-header">
          <h3><Icon name="trophy" size={18} /> Bảng điểm</h3>
          <div className="spacer" />
          <div className="muted small">Mã: <code style={{ letterSpacing: 2 }}>{history.code}</code> · Chủ phòng: {history.hostDisplayName}</div>
        </div>
        <div className="leaderboard">
          {history.finalScores.map((p, idx) => (
            <div key={p.userId} className={`leader-row ${idx === 0 ? 'top1' : ''}`}>
              <div className={`rank-badge r${idx + 1}`}>{idx + 1}</div>
              <div className="avatar sm" aria-label={p.displayName}>
                {p.hasAvatar
                  ? <img src={api.userAvatarUrl(p.userId)} alt={p.displayName} style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: '50%' }} />
                  : initials(p.displayName)}
              </div>
              <div className="name">{p.displayName}</div>
              <span className={`score-pill ${scoreClass(p.totalScore)}`}>{formatScore(p.totalScore)}</span>
            </div>
          ))}
          {history.finalScores.length === 0 && (
            <div className="muted">Phòng này không có bảng điểm.</div>
          )}
        </div>
      </div>

      <div style={{ marginTop: 12 }}>
        <button className="sm ghost" onClick={() => navigate('/rooms')}>← Quay lại danh sách phòng</button>
      </div>
    </div>
  );
}
