import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, LuckyWheelPreview, RoomHistory, RoomSponsorEntry } from '../api';
import { useAuth } from '../auth/AuthContext';
import { useToast } from '../ui/Toast';
import { Icon } from '../ui/Icon';
import { formatScore, scoreClass, formatDateTime, initials } from '../ui/helpers';
import { useHistorySocket, WheelSpinStartedPayload } from '../hooks/useHistorySocket';
import { playSound } from '../sounds';
import './lucky-wheel.css';

const WHEEL_SPIN_DURATION_MS = 5000;
// Tổng thời gian section giữ ở chế độ "đang quay" trước khi parent unlock MoneySummary.
const WHEEL_REVEAL_BUFFER_MS = WHEEL_SPIN_DURATION_MS + 900;

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
        if (!cancelled) {
          setHistory(h);
          if (h.luckyWheelPreview) setLivePreview(h.luckyWheelPreview);
        }
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

  // Wheel spin payload broadcast tới mọi viewer khi spinner bấm "Quay". Component LuckyWheelSection
  // dùng để animate lock-step.
  const [wheelSpin, setWheelSpin] = useState<WheelSpinStartedPayload | null>(null);
  // Preview pool broadcast bởi server sau khi spinner bấm "Tạo vòng xoay".
  const [livePreview, setLivePreview] = useState<LuckyWheelPreview | null>(null);
  const handleHistoryUpdated = useCallback((h: RoomHistory) => {
    setHistory(h);
    // Server gửi preview qua history.luckyWheelPreview sau khi tạo — đồng bộ vào livePreview.
    if (h.luckyWheelPreview) setLivePreview(h.luckyWheelPreview);
  }, []);
  // True khi animation đang chạy → block MoneySummary để không lộ kết quả trước khi wheel dừng.
  const [wheelAnimating, setWheelAnimating] = useState(false);
  const handleWheelSpinStarted = useCallback((payload: WheelSpinStartedPayload) => {
    setWheelSpin(payload);
    setWheelAnimating(true);
  }, []);
  useEffect(() => {
    if (!wheelAnimating) return;
    const t = setTimeout(() => setWheelAnimating(false), WHEEL_REVEAL_BUFFER_MS);
    return () => clearTimeout(t);
  }, [wheelAnimating, wheelSpin]);
  const handleWheelPreview = useCallback((payload: LuckyWheelPreview) => {
    setLivePreview(payload);
    // Vòng xoay vừa được tạo (broadcast tới mọi viewer) → phát sound lottery.
    playSound('lottery', 0.6);
  }, []);
  const handleWheelPreviewCleared = useCallback(() => {
    setLivePreview(null);
  }, []);
  const { createLuckyWheelPreview, resetLuckyWheelPreview, startLuckyWheelSpin } = useHistorySocket({
    code,
    onHistoryUpdated: handleHistoryUpdated,
    onWheelSpinStarted: handleWheelSpinStarted,
    onWheelPreview: handleWheelPreview,
    onWheelPreviewCleared: handleWheelPreviewCleared,
  });

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

  // Tất cả donor đã quyết định (lưu hoặc bỏ qua) → spinner mới được tạo vòng quay.
  // Không có donor / không có người âm → coi như xong luôn.
  const allDonors = baseSorted
    .filter((_, i) => i < 2)
    .filter(s => s.totalScore > 0)
    .map(s => s.userId);
  const decidedSet = new Set(history.sponsorDecidedDonors ?? []);
  const sponsorReady = recipients.length === 0 || allDonors.length === 0 || allDonors.every(id => decidedSet.has(id));
  const pendingDonors = allDonors.filter(id => !decidedSet.has(id))
    .map(id => baseSorted.find(s => s.userId === id)?.displayName ?? '?');

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
        myUserId={myUserId}
        baseSorted={baseSorted}
        wheelSpin={wheelSpin}
        livePreview={livePreview}
        sponsorReady={sponsorReady}
        pendingDonors={pendingDonors}
        createPreview={createLuckyWheelPreview}
        resetPreview={resetLuckyWheelPreview}
        startSpin={startLuckyWheelSpin}
      />

      {history.luckyWheel && !wheelAnimating && (
        <MoneySummarySection
          adjustedSorted={adjustedSorted}
          multiplier={history.luckyWheel.result}
        />
      )}

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

  async function handleSkip() {
    setSaving(true);
    try {
      const updated = await api.skipSponsor(code);
      onSaved(updated);
      toast.push('success', 'Đã bỏ qua sponsor.');
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  const iAlreadyDecided = (history.sponsorDecidedDonors ?? []).includes(myUserId);

  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="card-header">
        <h3>💝 Sponsor</h3>
        <div className="spacer" />
        <div className="muted small">Vòng sponsor cấp vốn </div>
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
          <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button
              className="primary"
              onClick={handleSave}
              disabled={saving || remaining < 0}
            >
              💾 Lưu sponsor
            </button>
            <button
              className="ghost"
              onClick={handleSkip}
              disabled={saving}
              title="Đánh dấu bạn không sponsor — vòng quay sẽ mở cho người hạng bét"
            >
              ⏭ Bỏ qua sponsor
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
            {iAlreadyDecided && (
              <span className="muted small" style={{ alignSelf: 'center' }}>
                ✓ Bạn đã quyết định (có thể chỉnh lại trước khi quay)
              </span>
            )}
          </div>
        </>
      ) : (
        <div className="muted small">Chỉ người (điểm dương) mới được sponsor.</div>
      )}
    </div>
  );
}

interface LuckyWheelSectionProps {
  history: RoomHistory;
  myUserId: string;
  baseSorted: RoomHistory['finalScores'];
  wheelSpin: WheelSpinStartedPayload | null;
  livePreview: LuckyWheelPreview | null;
  sponsorReady: boolean;
  pendingDonors: string[];
  createPreview: (min: number, max: number, doubled: boolean) => Promise<void>;
  resetPreview: () => Promise<void>;
  startSpin: () => Promise<void>;
}

const WHEEL_COLORS = ['#ff6b6b', '#ffd166', '#06d6a0', '#118ab2', '#7c3aed', '#f59e0b', '#ec4899', '#10b981'];
function LuckyWheelSection({ history, myUserId, baseSorted, wheelSpin, livePreview, sponsorReady, pendingDonors, createPreview, resetPreview, startSpin }: LuckyWheelSectionProps) {
  const toast = useToast();
  // Người hạng bét theo điểm GỐC.
  const spinner = baseSorted[baseSorted.length - 1];
  const iAmSpinner = spinner?.userId === myUserId;
  const existing = history.luckyWheel;

  // Form state (chỉ dùng khi spinner mở chưa quay)
  const [min, setMin] = useState(1);
  const [max, setMax] = useState(5);
  const [doubled, setDoubled] = useState(false);
  const [requesting, setRequesting] = useState(false);

  // Animation state — kích hoạt khi nhận WheelSpinStarted (mọi viewer).
  const [animatedPool, setAnimatedPool] = useState<number[] | null>(null);
  const [animatedRotation, setAnimatedRotation] = useState(0);
  const [animating, setAnimating] = useState(false);
  const handledSpinRef = useRef<string | null>(null);

  useEffect(() => {
    if (!wheelSpin) return;
    const key = `${wheelSpin.spinnerUserId}|${wheelSpin.resultIndex}|${wheelSpin.pool.join(',')}`;
    if (handledSpinRef.current === key) return;
    handledSpinRef.current = key;
    const slice = 360 / wheelSpin.pool.length;
    // 5 vòng full + offset để slice resultIndex dừng dưới pointer (12h).
    const target = 360 * 5 - (wheelSpin.resultIndex * slice + slice / 2);
    setAnimatedPool(wheelSpin.pool);
    setAnimatedRotation(0);
    setAnimating(false);
    // 1 frame sau mới set rotation để CSS transition kick in.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        setAnimating(true);
        setAnimatedRotation(target);
      });
    });
    // Stop animating sau 3.5s, sau đó clear pool thêm 800ms (cho user nhìn số kết quả tĩnh trên wheel
    // trước khi component chuyển sang block hiển thị result + tổng kết tiền).
    const stop = setTimeout(() => setAnimating(false), WHEEL_SPIN_DURATION_MS + 100);
    const clear = setTimeout(() => setAnimatedPool(null), WHEEL_SPIN_DURATION_MS + 900);
    return () => { clearTimeout(stop); clearTimeout(clear); };
  }, [wheelSpin]);

  // Đã có kết quả persisted + không còn animate → hiển thị result tĩnh.
  if (existing && !animating && !animatedPool) {
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

  async function handleCreate() {
    if (min < 1 || max < min || max > 1000) {
      toast.push('error', 'Khoảng min/max không hợp lệ.');
      return;
    }
    setRequesting(true);
    try {
      // Server tạo pool + broadcast WheelPreview → parent set livePreview cho mọi viewer.
      await createPreview(min, max, doubled);
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setRequesting(false);
    }
  }

  async function handleSpin() {
    if (!livePreview) return;
    setRequesting(true);
    try {
      await startSpin();
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      // Reset luôn — WheelSpinStarted đến sẽ kích hoạt animation block tách biệt; nếu vì lý do gì
      // animation chưa kick (server không broadcast), spinner không bị stuck ở 'Đang gửi…'.
      setRequesting(false);
    }
  }

  async function handleReset() {
    setRequesting(true);
    try {
      await resetPreview();
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setRequesting(false);
    }
  }

  // Đang animate (mọi viewer).
  if (animatedPool) {
    return (
      <div className="card" style={{ marginTop: 12 }}>
        <div className="card-header">
          <h3>🎡 Vòng quay may mắn</h3>
          <div className="spacer" />
          <div className="muted small">
            {wheelSpin?.spinnerUserId === myUserId ? 'Bạn đang quay' : 'Đang quay…'}
          </div>
        </div>
        <div className="lucky-wheel-container">
          <div className="lucky-wheel-pointer">▼</div>
          <div
            className="lucky-wheel"
            style={{
              background: `conic-gradient(${animatedPool.map((_, i) => {
                const slice = 360 / animatedPool.length;
                const start = i * slice;
                const end = start + slice;
                return `${WHEEL_COLORS[i % WHEEL_COLORS.length]} ${start}deg ${end}deg`;
              }).join(', ')})`,
              transform: `rotate(${animatedRotation}deg)`,
              transition: animating ? `transform ${WHEEL_SPIN_DURATION_MS}ms cubic-bezier(0.17, 0.67, 0.16, 0.99)` : 'none',
            }}
          >
            {animatedPool.map((n, i) => {
              const slice = 360 / animatedPool.length;
              const angle = i * slice + slice / 2;
              return (
                <div
                  key={i}
                  className="lucky-wheel-label"
                  style={{ transform: `rotate(${angle}deg) translateY(calc(-1 * var(--wheel-label-radius)))` }}
                >
                  <span style={{ transform: 'rotate(90deg)', display: 'inline-block' }}>{n}</span>
                </div>
              );
            })}
          </div>
          {!animating && wheelSpin && (
            <div className="lucky-wheel-result" style={{ marginTop: 12 }}>
              <div className="lucky-wheel-number">{wheelSpin.pool[wheelSpin.resultIndex]}</div>
            </div>
          )}
        </div>
      </div>
    );
  }

  // Chưa quay — preview broadcast tới mọi viewer khi có; spinner thấy thêm nút điều khiển.
  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="card-header">
        <h3>🎡 Vòng quay may mắn</h3>
        <div className="spacer" />
        <div className="muted small">
          {iAmSpinner
            ? (sponsorReady ? 'Bạn (hạng bét) được quay 1 lần' : 'Chờ sponsor xong')
            : `Đang chờ ${spinner.displayName} quay`}
        </div>
      </div>

      {!sponsorReady && (
        <div className="muted small" style={{ marginBottom: 8 }}>
          ⏳ Đợi quyết định sponsor: <b>{pendingDonors.join(', ')}</b>
        </div>
      )}

      {/* Step 1: nhập min/max/double, bấm "Tạo vòng xoay" — chỉ spinner. */}
      {!livePreview && iAmSpinner && (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end', opacity: sponsorReady ? 1 : 0.5 }}>
          <label>
            <div className="dim small">Min</div>
            <input type="number" min={1} max={1000} value={min} onChange={e => setMin(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
              style={{ width: 80, padding: '6px 10px' }} disabled={!sponsorReady || requesting} />
          </label>
          <label>
            <div className="dim small">Max</div>
            <input type="number" min={1} max={1000} value={max} onChange={e => setMax(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
              style={{ width: 80, padding: '6px 10px' }} disabled={!sponsorReady || requesting} />
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={doubled} onChange={e => setDoubled(e.target.checked)} disabled={!sponsorReady || requesting} />
            <span>Double (mỗi số 2 lần)</span>
          </label>
          <button className="primary" onClick={handleCreate} disabled={!sponsorReady || requesting}>
            {requesting ? '⏳ Đang tạo…' : '🎨 Tạo vòng xoay'}
          </button>
        </div>
      )}

      {/* Trước khi tạo: viewer khác thấy thông báo. */}
      {!livePreview && !iAmSpinner && sponsorReady && (
        <div className="muted small">
          Chỉ người hạng bét (<b>{spinner.displayName}</b>) được tạo vòng quay.
        </div>
      )}

      {/* Step 2: preview wheel (tất cả viewer) + nút Quay (chỉ spinner) */}
      {livePreview && (
        <div className="lucky-wheel-container">
          <div className="lucky-wheel-pointer">▼</div>
          <div
            className="lucky-wheel"
            style={{
              background: `conic-gradient(${livePreview.pool.map((_, i) => {
                const slice = 360 / livePreview.pool.length;
                const start = i * slice;
                const end = start + slice;
                return `${WHEEL_COLORS[i % WHEEL_COLORS.length]} ${start}deg ${end}deg`;
              }).join(', ')})`,
            }}
          >
            {livePreview.pool.map((n, i) => {
              const slice = 360 / livePreview.pool.length;
              const angle = i * slice + slice / 2;
              return (
                <div
                  key={i}
                  className="lucky-wheel-label"
                  style={{ transform: `rotate(${angle}deg) translateY(calc(-1 * var(--wheel-label-radius)))` }}
                >
                  <span style={{ transform: 'rotate(90deg)', display: 'inline-block' }}>{n}</span>
                </div>
              );
            })}
          </div>
          {iAmSpinner ? (
            <div style={{ marginTop: 12, display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
              <button className="primary" onClick={handleSpin} disabled={requesting}>
                {requesting ? '⏳ Đang gửi…' : '🎯 Quay!'}
              </button>
              <button className="ghost" onClick={handleReset} disabled={requesting}>
                ↺ Đổi khoảng
              </button>
            </div>
          ) : (
            <div className="muted small" style={{ marginTop: 12, textAlign: 'center' }}>
              Đang chờ <b>{spinner.displayName}</b> bấm Quay…
            </div>
          )}
          <div className="muted small" style={{ marginTop: 8, textAlign: 'center' }}>
            Khoảng {livePreview.min}–{livePreview.max}{livePreview.double ? ' (×2)' : ''} · {livePreview.pool.length} ô
          </div>
        </div>
      )}
    </div>
  );
}

interface MoneySummarySectionProps {
  adjustedSorted: RoomHistory['finalScores'];
  multiplier: number;
}

/** Format tiền viết tắt: 30 → "+30k", -30 → "-30k", 0 → "0k". */
function formatMoney(value: number): string {
  const sign = value < 0 ? '-' : value > 0 ? '+' : '';
  return `${sign}${Math.abs(value)}k`;
}

function MoneySummarySection({ adjustedSorted, multiplier }: MoneySummarySectionProps) {
  return (
    <div className="card" style={{ marginTop: 12 }}>
      <div className="card-header">
        <h3>💰 Tổng kết tiền</h3>
        <div className="spacer" />
        <div className="muted small">1 điểm = {multiplier}k · sau sponsor</div>
      </div>
      <div className="leaderboard">
        {adjustedSorted.map((p, idx) => {
          const money = p.totalScore * multiplier;
          return (
            <div key={p.userId} className={`leader-row ${idx === 0 ? 'top1' : ''}`}>
              <div className={`rank-badge r${idx + 1}`}>{idx + 1}</div>
              <div className="avatar sm" aria-label={p.displayName}>
                {p.hasAvatar
                  ? <img src={api.userAvatarUrl(p.userId)} alt={p.displayName} style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: '50%' }} />
                  : initials(p.displayName)}
              </div>
              <div className="name">{p.displayName}</div>
              <span className={`score-pill ${scoreClass(p.totalScore)}`} style={{ marginRight: 8 }}>
                {formatScore(p.totalScore)}
              </span>
              <span className={`score-pill ${scoreClass(money)}`} style={{ fontWeight: 800, fontSize: 14 }}>
                {formatMoney(money)}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
