import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, RoomHistory, RoomSponsorEntry } from '../api';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';
import { formatScore, scoreClass, formatDateTime, initials } from '../ui/helpers';
import './lucky-wheel.css';

/**
 * Compute final scores after applying the sponsor plan (Nhất/Nhì transfer điểm cho người âm).
 * Donor mất `amount` per entry; recipient nhận `amount` per entry.
 */
function applySponsorPlan(
  scores: RoomHistory['finalScores'],
  plan: RoomSponsorEntry[] | null | undefined,
): RoomHistory['finalScores'] {
  if (!plan || plan.length === 0) return scores;
  const delta = new Map<string, number>();
  for (const e of plan) {
    delta.set(e.fromUserId, (delta.get(e.fromUserId) ?? 0) - e.amount);
    delta.set(e.toUserId, (delta.get(e.toUserId) ?? 0) + e.amount);
  }
  return scores.map(s => ({ ...s, totalScore: s.totalScore + (delta.get(s.userId) ?? 0) }));
}

export default function RoomHistoryDetailPage() {
  const { code } = useParams<{ code: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const myUserId = state.status === 'authenticated' ? state.userId : '';
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

  // Base scores (gốc, trước sponsor) sorted desc — dùng để biết ai Nhất/Nhì + ai âm.
  const baseSorted = useMemo(
    () => history ? [...history.finalScores].sort((a, b) => b.totalScore - a.totalScore) : [],
    [history],
  );
  const adjustedScores = useMemo(
    () => history ? applySponsorPlan(history.finalScores, history.sponsorPlan) : [],
    [history],
  );
  const adjustedSorted = useMemo(
    () => [...adjustedScores].sort((a, b) => b.totalScore - a.totalScore),
    [adjustedScores],
  );

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

  const champion = adjustedSorted[0];

  // Sponsor eligibility (theo điểm GỐC trước sponsor)
  const top1Base = baseSorted[0];
  const top2Base = baseSorted[1];
  const myBaseScore = baseSorted.find(s => s.userId === myUserId);
  const iAmDonor = !!myBaseScore && myBaseScore.totalScore > 0
    && (myBaseScore.userId === top1Base?.userId || myBaseScore.userId === top2Base?.userId);
  const recipients = baseSorted.filter(s => s.totalScore < 0);

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
          <h3><Icon name="trophy" size={18} /> Bảng điểm{history.sponsorPlan && history.sponsorPlan.length > 0 ? ' (sau sponsor)' : ''}</h3>
          <div className="spacer" />
          <div className="muted small">Mã: <code style={{ letterSpacing: 2 }}>{history.code}</code> · Chủ phòng: {history.hostDisplayName}</div>
        </div>
        <div className="leaderboard">
          {adjustedSorted.map((p, idx) => {
            const base = history.finalScores.find(s => s.userId === p.userId)?.totalScore ?? p.totalScore;
            const changed = base !== p.totalScore;
            return (
              <div key={p.userId} className={`leader-row ${idx === 0 ? 'top1' : ''}`}>
                <div className={`rank-badge r${idx + 1}`}>{idx + 1}</div>
                <div className="avatar sm" aria-label={p.displayName}>
                  {p.hasAvatar
                    ? <img src={api.userAvatarUrl(p.userId)} alt={p.displayName} style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: '50%' }} />
                    : initials(p.displayName)}
                </div>
                <div className="name">{p.displayName}</div>
                {changed && (
                  <span className="muted small" style={{ marginRight: 8 }}>
                    (gốc {formatScore(base)})
                  </span>
                )}
                <span className={`score-pill ${scoreClass(p.totalScore)}`}>{formatScore(p.totalScore)}</span>
              </div>
            );
          })}
          {adjustedSorted.length === 0 && (
            <div className="muted">Phòng này không có bảng điểm.</div>
          )}
        </div>
      </div>

      {recipients.length > 0 && (top1Base?.totalScore ?? 0) > 0 && (
        <SponsorSection
          history={history}
          code={code!}
          iAmDonor={iAmDonor}
          myBaseScore={myBaseScore?.totalScore ?? 0}
          recipients={recipients}
          onSaved={(h) => setHistory(h)}
        />
      )}

      <LuckyWheelSection
        history={history}
        code={code!}
        myUserId={myUserId}
        baseSorted={baseSorted}
        onSaved={(h) => setHistory(h)}
      />

      <div style={{ marginTop: 12 }}>
        <button className="sm ghost" onClick={() => navigate('/rooms')}>← Quay lại danh sách phòng</button>
      </div>
    </div>
  );
}

interface SponsorSectionProps {
  history: RoomHistory;
  code: string;
  iAmDonor: boolean;
  myBaseScore: number;
  recipients: RoomHistory['finalScores'];
  onSaved: (h: RoomHistory) => void;
}

function SponsorSection({ history, code, iAmDonor, myBaseScore, recipients, onSaved }: SponsorSectionProps) {
  const toast = useToast();
  const { state } = useAuth();
  const myUserId = state.status === 'authenticated' ? state.userId : '';
  // Mine sponsor entries from existing plan (so a donor can edit their own portion).
  const existingMine = useMemo(
    () => (history.sponsorPlan ?? []).filter(e => e.fromUserId === myUserId),
    [history.sponsorPlan, myUserId],
  );
  const [allocations, setAllocations] = useState<Record<string, number>>(() => {
    const map: Record<string, number> = {};
    for (const r of recipients) {
      const found = existingMine.find(e => e.toUserId === r.userId);
      map[r.userId] = found?.amount ?? 0;
    }
    return map;
  });
  const [saving, setSaving] = useState(false);

  // Re-init khi history đổi (vd save xong)
  useEffect(() => {
    const map: Record<string, number> = {};
    for (const r of recipients) {
      const found = existingMine.find(e => e.toUserId === r.userId);
      map[r.userId] = found?.amount ?? 0;
    }
    setAllocations(map);
  }, [history.sponsorPlan]);

  const totalGiven = Object.values(allocations).reduce((a, b) => a + b, 0);
  const remaining = myBaseScore - totalGiven;

  function setAmount(uid: string, raw: string) {
    const n = Math.max(0, Math.floor(Number(raw) || 0));
    setAllocations(prev => ({ ...prev, [uid]: n }));
  }

  async function handleSave() {
    if (remaining < 0) {
      toast.push('error', `Tổng đã vượt điểm của bạn (${myBaseScore}).`);
      return;
    }
    setSaving(true);
    try {
      const plan = Object.entries(allocations)
        .filter(([, v]) => v > 0)
        .map(([toUserId, amount]) => ({ fromUserId: myUserId, toUserId, amount }));
      const updated = await api.saveSponsorPlan(code, plan);
      onSaved(updated);
      toast.push('success', 'Đã lưu sponsor.');
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="card-header">
        <h3>💝 Sponsor</h3>
        <div className="spacer" />
        <div className="muted small">Nhất/Nhì có thể chia điểm cho người điểm âm</div>
      </div>

      {/* Hiển thị plan hiện có của mọi người để xem */}
      {history.sponsorPlan && history.sponsorPlan.length > 0 && (
        <div style={{ marginBottom: 12 }}>
          <div className="dim small" style={{ marginBottom: 4 }}>Đã sponsor:</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
            {history.sponsorPlan.map((e, i) => {
              const from = history.finalScores.find(s => s.userId === e.fromUserId)?.displayName ?? '?';
              const to = history.finalScores.find(s => s.userId === e.toUserId)?.displayName ?? '?';
              return (
                <span key={i} className="score-chip" style={{ background: 'rgba(125,211,168,0.15)' }}>
                  {from} → {to}: <b>+{e.amount}</b>
                </span>
              );
            })}
          </div>
        </div>
      )}

      {iAmDonor ? (
        <>
          <div className="muted small" style={{ marginBottom: 8 }}>
            Bạn có <b>{formatScore(myBaseScore)}</b> điểm — còn lại <b className={remaining < 0 ? 'neg' : ''}>{formatScore(remaining)}</b> để chia.
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {recipients.map(r => (
              <div key={r.userId} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div className="avatar sm" aria-label={r.displayName}>
                  {r.hasAvatar
                    ? <img src={api.userAvatarUrl(r.userId)} alt={r.displayName} style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: '50%' }} />
                    : initials(r.displayName)}
                </div>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>{r.displayName}</div>
                  <div className="muted small">{formatScore(r.totalScore)} điểm</div>
                </div>
                <input
                  type="number"
                  min={0}
                  max={myBaseScore}
                  step={1}
                  value={allocations[r.userId] ?? 0}
                  onChange={e => setAmount(r.userId, e.target.value)}
                  style={{ width: 100, padding: '6px 10px' }}
                  disabled={saving}
                />
              </div>
            ))}
          </div>
          <div style={{ marginTop: 12, display: 'flex', gap: 8 }}>
            <button
              className="primary"
              onClick={handleSave}
              disabled={saving || remaining < 0}
            >
              💾 Lưu sponsor
            </button>
            <button
              className="ghost"
              onClick={() => {
                const map: Record<string, number> = {};
                for (const r of recipients) map[r.userId] = 0;
                setAllocations(map);
              }}
              disabled={saving}
            >
              Reset
            </button>
          </div>
        </>
      ) : (
        <div className="muted small">Chỉ Nhất hoặc Nhì (điểm dương) mới được sponsor.</div>
      )}
    </div>
  );
}

interface LuckyWheelSectionProps {
  history: RoomHistory;
  code: string;
  myUserId: string;
  baseSorted: RoomHistory['finalScores'];
  onSaved: (h: RoomHistory) => void;
}

/** Build the wheel pool (mỗi số xuất hiện 1 hoặc 2 lần), random order. */
function buildWheelPool(min: number, max: number, doubled: boolean): number[] {
  const base: number[] = [];
  for (let n = min; n <= max; n++) base.push(n);
  const pool = doubled ? [...base, ...base] : base;
  // Fisher-Yates shuffle
  for (let i = pool.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [pool[i], pool[j]] = [pool[j], pool[i]];
  }
  return pool;
}

const WHEEL_COLORS = ['#ff6b6b', '#ffd166', '#06d6a0', '#118ab2', '#7c3aed', '#f59e0b', '#ec4899', '#10b981'];

function LuckyWheelSection({ history, code, myUserId, baseSorted, onSaved }: LuckyWheelSectionProps) {
  const toast = useToast();
  // Người hạng bét theo điểm GỐC (như đã chốt với user).
  const spinner = baseSorted[baseSorted.length - 1];
  const iAmSpinner = spinner?.userId === myUserId;
  const existing = history.luckyWheel;

  const [min, setMin] = useState(1);
  const [max, setMax] = useState(5);
  const [doubled, setDoubled] = useState(false);
  const [pool, setPool] = useState<number[] | null>(null);
  const [rotation, setRotation] = useState(0);
  const [spinning, setSpinning] = useState(false);
  const [resultIdx, setResultIdx] = useState<number | null>(null);
  const saveRef = useRef(false);

  // Nếu đã có kết quả persisted: dựng lại pool với seed cố định? Không cần — chỉ hiển thị result.
  if (existing) {
    return (
      <div className="card" style={{ marginTop: 12 }}>
        <div className="card-header">
          <h3>🎡 Vòng quay may mắn</h3>
          <div className="spacer" />
          <div className="muted small">Đã có kết quả</div>
        </div>
        <div className="lucky-wheel-result">
          <div className="lucky-wheel-number">{existing.result}</div>
          <div className="muted small">
            Khoảng {existing.min}–{existing.max}{existing.double ? ' (×2)' : ''} · Người quay:{' '}
            <b>{history.finalScores.find(s => s.userId === existing.spinnerUserId)?.displayName ?? '?'}</b>
          </div>
        </div>
      </div>
    );
  }

  if (!spinner) return null;

  function handleStart() {
    if (min < 1 || max < min || max > 1000) {
      toast.push('error', 'Khoảng min/max không hợp lệ.');
      return;
    }
    setPool(buildWheelPool(min, max, doubled));
    setRotation(0);
    setResultIdx(null);
  }

  async function handleSpin() {
    if (!pool || spinning || saveRef.current) return;
    const idx = Math.floor(Math.random() * pool.length);
    const slice = 360 / pool.length;
    // Quay 5 vòng + tới vị trí target. Pointer ở trên (12h), slice i ở góc i*slice → muốn slice idx
    // dừng dưới pointer → rotation = -(idx * slice + slice / 2) + 5*360.
    const target = 360 * 5 - (idx * slice + slice / 2);
    setSpinning(true);
    setRotation(target);
    setResultIdx(idx);
    // Sau khi animation xong (~3.5s), save server.
    setTimeout(async () => {
      if (saveRef.current) return;
      saveRef.current = true;
      try {
        const updated = await api.saveLuckyWheel(code, {
          min,
          max,
          double: doubled,
          result: pool[idx],
        });
        onSaved(updated);
        toast.push('success', `Kết quả: ${pool[idx]}`);
      } catch (e) {
        toast.push('error', (e as Error).message);
        saveRef.current = false;
      } finally {
        setSpinning(false);
      }
    }, 3600);
  }

  if (!iAmSpinner) {
    return (
      <div className="card" style={{ marginTop: 12 }}>
        <div className="card-header">
          <h3>🎡 Vòng quay may mắn</h3>
          <div className="spacer" />
          <div className="muted small">Đang chờ {spinner.displayName} quay</div>
        </div>
        <div className="muted small">
          Chỉ người hạng bét (<b>{spinner.displayName}</b>) được quay vòng.
        </div>
      </div>
    );
  }

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="card-header">
        <h3>🎡 Vòng quay may mắn</h3>
        <div className="spacer" />
        <div className="muted small">Bạn (hạng bét) được quay 1 lần</div>
      </div>

      {!pool && (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end' }}>
          <label>
            <div className="dim small">Min</div>
            <input type="number" min={1} max={1000} value={min} onChange={e => setMin(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
              style={{ width: 80, padding: '6px 10px' }} />
          </label>
          <label>
            <div className="dim small">Max</div>
            <input type="number" min={1} max={1000} value={max} onChange={e => setMax(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
              style={{ width: 80, padding: '6px 10px' }} />
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={doubled} onChange={e => setDoubled(e.target.checked)} />
            <span>Double (mỗi số 2 lần)</span>
          </label>
          <button className="primary" onClick={handleStart}>Tạo vòng quay</button>
        </div>
      )}

      {pool && (
        <div className="lucky-wheel-container">
          <div className="lucky-wheel-pointer">▼</div>
          <div
            className="lucky-wheel"
            style={{
              background: `conic-gradient(${pool.map((_, i) => {
                const slice = 360 / pool.length;
                const start = i * slice;
                const end = start + slice;
                return `${WHEEL_COLORS[i % WHEEL_COLORS.length]} ${start}deg ${end}deg`;
              }).join(', ')})`,
              transform: `rotate(${rotation}deg)`,
              transition: spinning ? 'transform 3.5s cubic-bezier(0.17, 0.67, 0.16, 0.99)' : 'none',
            }}
          >
            {pool.map((n, i) => {
              const slice = 360 / pool.length;
              const angle = i * slice + slice / 2;
              return (
                <div
                  key={i}
                  className="lucky-wheel-label"
                  style={{
                    transform: `rotate(${angle}deg) translateY(-80%)`,
                  }}
                >
                  <span style={{ transform: 'rotate(90deg)', display: 'inline-block' }}>{n}</span>
                </div>
              );
            })}
          </div>
          <div style={{ marginTop: 12, display: 'flex', gap: 8, justifyContent: 'center' }}>
            <button className="primary" onClick={handleSpin} disabled={spinning}>
              {spinning ? '🌀 Đang quay…' : '🎯 Quay!'}
            </button>
            {!spinning && (
              <button className="ghost" onClick={() => setPool(null)}>Đổi khoảng</button>
            )}
          </div>
          {resultIdx !== null && !spinning && (
            <div className="lucky-wheel-result" style={{ marginTop: 12 }}>
              <div className="lucky-wheel-number">{pool[resultIdx]}</div>
              <div className="muted small">Đang lưu…</div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
