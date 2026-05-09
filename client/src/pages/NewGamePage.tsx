import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, BallConfig, GameType, Player } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { initials } from '../ui/helpers';
import { Avatar } from '../ui/Avatar';

type Mode = 'tienlen' | 'bida9' | 'manual';

const BIDA_DEFAULT_POINTS: Record<number, number> = { 3: 1, 6: 2, 9: 3 };

export default function NewGamePage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [mode, setMode] = useState<Mode>('tienlen');
  const [tlSeats, setTlSeats] = useState<Array<string | ''>>(['', '', '', '']);
  const [bidaSeats, setBidaSeats] = useState<Array<string | ''>>(['', '', '']);
  const [bidaBalls, setBidaBalls] = useState<BallConfig[]>([
    { ball: 3, points: 1 },
    { ball: 6, points: 2 },
    { ball: 9, points: 3 }
  ]);
  const [manualIds, setManualIds] = useState<string[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const nav = useNavigate();
  const toast = useToast();

  useEffect(() => {
    api.listPlayers().then(setPlayers).catch((e) => toast.push('error', (e as Error).message));
  }, [toast]);

  function setSeat(i: number, id: string) {
    setTlSeats((prev) => prev.map((v, idx) => (idx === i ? id : v)));
  }

  function pickPlayer(slotIndex: number, p: Player) {
    if (tlSeats.includes(p.id) && tlSeats[slotIndex] !== p.id) {
      const existingSlot = tlSeats.indexOf(p.id);
      setTlSeats((prev) => {
        const next = [...prev];
        next[existingSlot] = next[slotIndex];
        next[slotIndex] = p.id;
        return next;
      });
      return;
    }
    setSeat(slotIndex, p.id);
  }

  function setBidaSeat(i: number, id: string) {
    setBidaSeats((prev) => prev.map((v, idx) => (idx === i ? id : v)));
  }

  function pickBidaPlayer(slotIndex: number, p: Player) {
    if (bidaSeats.includes(p.id) && bidaSeats[slotIndex] !== p.id) {
      const existingSlot = bidaSeats.indexOf(p.id);
      setBidaSeats((prev) => {
        const next = [...prev];
        next[existingSlot] = next[slotIndex];
        next[slotIndex] = p.id;
        return next;
      });
      return;
    }
    setBidaSeat(slotIndex, p.id);
  }

  function toggleManual(id: string) {
    setManualIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function toggleBall(ball: number) {
    setBidaBalls((prev) => {
      const exists = prev.find((b) => b.ball === ball);
      if (exists) return prev.filter((b) => b.ball !== ball);
      const points = BIDA_DEFAULT_POINTS[ball] ?? ball;
      return [...prev, { ball, points }].sort((a, b) => a.ball - b.ball);
    });
  }

  function setBallPoints(ball: number, points: number) {
    setBidaBalls((prev) => prev.map((b) => (b.ball === ball ? { ...b, points } : b)));
  }

  const tlAllSelected = tlSeats.every((s) => s !== '');
  const tlUnique = new Set(tlSeats.filter(Boolean)).size === tlSeats.filter(Boolean).length;
  const bidaAllSelected = bidaSeats.every((s) => s !== '');
  const bidaUnique = new Set(bidaSeats.filter(Boolean)).size === bidaSeats.filter(Boolean).length;
  const bidaBallsValid = bidaBalls.length >= 1 && bidaBalls.every((b) => b.points > 0);
  const manualValid = manualIds.length >= 2;

  async function start() {
    setSubmitting(true);
    try {
      let g;
      if (mode === 'tienlen') {
        if (!tlAllSelected) { toast.push('error', 'Phải chọn đủ 4 người chơi'); return; }
        if (!tlUnique) { toast.push('error', 'Người chơi không được trùng nhau'); return; }
        g = await api.createGame(tlSeats as string[], GameType.TienLenMienNam);
      } else if (mode === 'bida9') {
        if (!bidaAllSelected) { toast.push('error', 'Phải chọn đủ 3 người chơi'); return; }
        if (!bidaUnique) { toast.push('error', 'Người chơi không được trùng nhau'); return; }
        if (!bidaBallsValid) { toast.push('error', 'Chọn ít nhất 1 bi và nhập điểm > 0'); return; }
        g = await api.createGame(bidaSeats as string[], GameType.Bida9Ball, bidaBalls);
      } else {
        if (!manualValid) { toast.push('error', 'Cần ít nhất 2 người chơi'); return; }
        g = await api.createGame(manualIds, GameType.Manual);
      }
      nav(`/games/${g.id}`);
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  const hasEnoughForTienLen = players.length >= 4;
  const hasEnoughForBida = players.length >= 3;
  const hasEnoughForManual = players.length >= 2;

  if (!hasEnoughForManual) {
    return (
      <div>
        <div className="page-header">
          <h1>Ván mới</h1>
        </div>
        <div className="card empty">
          <div className="empty-icon"><Icon name="users" /></div>
          <div className="bold">Cần ít nhất 2 người chơi</div>
          <div className="small dim mt-1">
            Hiện có {players.length} người. Hãy thêm người chơi trước.
          </div>
          <div className="mt-2">
            <Link to="/players"><button><Icon name="plus" size={16} />Thêm người chơi</button></Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1>Ván mới</h1>
          <div className="muted small">Chọn loại ván rồi chọn người chơi</div>
        </div>
      </div>

      <div className="card">
        <div className="section-title">Loại ván</div>
        <div className="row mt-1" style={{ flexWrap: 'wrap' }}>
          <button
            type="button"
            className={mode === 'tienlen' ? '' : 'secondary'}
            disabled={!hasEnoughForTienLen}
            onClick={() => setMode('tienlen')}
          >
            <Icon name="cards" size={14} /> Tiến Lên Miền Nam (4 người)
          </button>
          <button
            type="button"
            className={mode === 'bida9' ? '' : 'secondary'}
            disabled={!hasEnoughForBida}
            onClick={() => setMode('bida9')}
          >
            <Icon name="cards" size={14} /> Bida 9 Bi (3 người)
          </button>
          <button
            type="button"
            className={mode === 'manual' ? '' : 'secondary'}
            onClick={() => setMode('manual')}
          >
            <Icon name="plus" size={14} /> Tự do (điểm thủ công)
          </button>
        </div>
        {!hasEnoughForTienLen && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Tiến Lên cần đủ 4 người (hiện có {players.length}).
          </div>
        )}
        {!hasEnoughForBida && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Bida 9 Bi cần đủ 3 người (hiện có {players.length}).
          </div>
        )}
        {mode === 'manual' && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Mỗi round nhập điểm cộng/trừ tự do cho từng người. Không bắt buộc tổng = 0.
          </div>
        )}
      </div>

      {mode === 'tienlen' && (
        <SeatPicker
          players={players}
          seats={tlSeats}
          onSet={setSeat}
          onPick={pickPlayer}
          toast={toast}
          slotCount={4}
        />
      )}

      {mode === 'bida9' && (
        <>
          <SeatPicker
            players={players}
            seats={bidaSeats}
            onSet={setBidaSeat}
            onPick={pickBidaPlayer}
            toast={toast}
            slotCount={3}
          />
          <BidaBallConfig balls={bidaBalls} onToggle={toggleBall} onSetPoints={setBallPoints} />
        </>
      )}

      {mode === 'manual' && (
        <ManualPlayers
          players={players}
          selected={manualIds}
          onToggle={toggleManual}
          onClear={() => setManualIds([])}
          onMove={(from, to) => {
            setManualIds((prev) => {
              const next = [...prev];
              const [item] = next.splice(from, 1);
              next.splice(to, 0, item);
              return next;
            });
          }}
        />
      )}

      <button
        onClick={start}
        disabled={
          submitting ||
          (mode === 'tienlen' ? !tlAllSelected || !tlUnique :
           mode === 'bida9' ? !bidaAllSelected || !bidaUnique || !bidaBallsValid :
           !manualValid)
        }
        className="block-mobile"
      >
        <Icon name="play" size={14} />
        {submitting
          ? 'Đang tạo…'
          : mode === 'tienlen'
          ? 'Bắt đầu ván'
          : mode === 'bida9'
          ? 'Bắt đầu ván Bida'
          : `Bắt đầu ván (${manualIds.length} người)`}
      </button>
    </div>
  );
}

function SeatPicker({
  players,
  seats,
  onSet,
  onPick,
  toast,
  slotCount
}: {
  players: Player[];
  seats: Array<string | ''>;
  onSet: (i: number, id: string) => void;
  onPick: (slotIndex: number, p: Player) => void;
  toast: { push: (kind: 'info' | 'success' | 'error', msg: string) => void };
  slotCount: number;
}) {
  const remaining = useMemo(() => players.filter((p) => !seats.includes(p.id)), [players, seats]);
  return (
    <>
      <div className="card">
        <div className="section-title">Vị trí ngồi</div>
        <div className="player-grid mt-1">
          {seats.map((seat, i) => {
            const player = players.find((p) => p.id === seat);
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', padding: '0.6rem 0.75rem', background: 'var(--bg-2)', borderRadius: 'var(--radius)', border: `1px solid ${player ? 'var(--accent)' : 'var(--border)'}` }}>
                <div className={`rank-badge r${(i % 4) + 1}`}>{i + 1}</div>
                {player ? (
                  <>
                    <Avatar playerId={player.id} name={player.name} hasAvatar={player.hasAvatar} size="sm" />
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div className="bold" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{player.name}</div>
                      {player.nickname && <div className="tiny muted">{player.nickname}</div>}
                    </div>
                    <button className="ghost icon-only" onClick={() => onSet(i, '')} aria-label="Bỏ chọn">×</button>
                  </>
                ) : (
                  <div className="muted" style={{ flex: 1 }}>Chưa chọn</div>
                )}
              </div>
            );
          })}
        </div>
      </div>

      <div className="card">
        <div className="section-title">Chọn từ danh sách</div>
        <div className="muted small mb-1">Chạm vào người chơi để gán vào vị trí trống tiếp theo</div>
        {remaining.length === 0 ? (
          <div className="muted small mt-1">Đã chọn đủ {slotCount} người. Bỏ chọn ở vị trí trên để đổi.</div>
        ) : (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', marginTop: '0.5rem' }}>
            {remaining.map((p) => (
              <button
                key={p.id}
                type="button"
                className="secondary"
                onClick={() => {
                  const empty = seats.indexOf('');
                  if (empty < 0) {
                    toast.push('info', `Đã đủ ${slotCount} người, bỏ chọn để thay`);
                    return;
                  }
                  onPick(empty, p);
                }}
                style={{ padding: '0.4rem 0.75rem 0.4rem 0.4rem', gap: '0.5rem' }}
              >
                {p.hasAvatar ? (
                  <Avatar playerId={p.id} name={p.name} hasAvatar size="sm" />
                ) : (
                  <span className="avatar sm">{initials(p.name)}</span>
                )}
                {p.name}
              </button>
            ))}
          </div>
        )}
      </div>
    </>
  );
}

function BidaBallConfig({
  balls,
  onToggle,
  onSetPoints
}: {
  balls: BallConfig[];
  onToggle: (ball: number) => void;
  onSetPoints: (ball: number, points: number) => void;
}) {
  const selected = new Set(balls.map((b) => b.ball));
  const totalPoints = balls.reduce((s, b) => s + b.points, 0);
  return (
    <div className="card">
      <div className="section-title">Bi tính điểm</div>
      <div className="muted tiny mb-1">
        <Icon name="info" size={11} /> Chọn các bi từ 1..9 và nhập điểm cho mỗi bi. Mặc định 3=1đ, 6=2đ, 9=3đ.
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.4rem', marginTop: '0.4rem' }}>
        {Array.from({ length: 9 }, (_, i) => i + 1).map((ball) => {
          const isSelected = selected.has(ball);
          return (
            <button
              key={ball}
              type="button"
              className={isSelected ? 'active' : 'secondary'}
              onClick={() => onToggle(ball)}
              style={{
                width: 44,
                height: 44,
                padding: 0,
                borderRadius: '50%',
                fontWeight: 700,
                fontSize: '1rem'
              }}
            >
              {ball}
            </button>
          );
        })}
      </div>

      {balls.length > 0 && (
        <>
          <div className="section-title mt-2">Điểm mỗi bi</div>
          <div className="col mt-1">
            {balls.map((b) => (
              <div
                key={b.ball}
                className="leader-row"
                style={{ background: 'var(--bg-2)', alignItems: 'center' }}
              >
                <div className="rank-badge r1" style={{ width: 36, height: 36 }}>{b.ball}</div>
                <div className="name">Bi {b.ball}</div>
                <div className="row gap-sm" style={{ alignItems: 'center' }}>
                  <input
                    type="number"
                    inputMode="numeric"
                    min={1}
                    value={b.points === 0 ? '' : b.points}
                    onChange={(e) => {
                      const raw = e.target.value;
                      onSetPoints(b.ball, raw === '' ? 0 : Math.max(0, Number(raw)));
                    }}
                    style={{ width: 80 }}
                  />
                  <span className="small dim">điểm</span>
                </div>
              </div>
            ))}
          </div>
          <div
            className="mt-2"
            style={{
              padding: '0.6rem 0.85rem',
              borderRadius: 'var(--radius)',
              border: '1px solid var(--border)',
              background: 'var(--bg-1)',
              color: 'var(--text-muted)',
              fontSize: '0.88rem'
            }}
          >
            <Icon name="info" size={14} /> Tổng điểm các bi = <strong>{totalPoints}</strong>.
            Phá-chấm: người phá +{totalPoints * 2}, mỗi người còn lại −{totalPoints}.
          </div>
        </>
      )}
    </div>
  );
}

function ManualPlayers({
  players,
  selected,
  onToggle,
  onClear,
  onMove
}: {
  players: Player[];
  selected: string[];
  onToggle: (id: string) => void;
  onClear: () => void;
  onMove: (from: number, to: number) => void;
}) {
  const selectedPlayers = selected
    .map((id) => players.find((p) => p.id === id))
    .filter((p): p is Player => !!p);
  const unselected = players.filter((p) => !selected.includes(p.id));

  return (
    <>
      <div className="card">
        <div className="card-header">
          <div className="section-title" style={{ margin: 0 }}>Người chơi đã chọn ({selected.length})</div>
          <div className="spacer" />
          {selected.length > 0 && (
            <button type="button" className="ghost sm" onClick={onClear}>Xoá hết</button>
          )}
        </div>
        {selectedPlayers.length === 0 ? (
          <div className="muted small mt-1">Chưa chọn ai. Tick từ danh sách bên dưới.</div>
        ) : (
          <div className="col mt-1">
            {selectedPlayers.map((p, idx) => (
              <div
                key={p.id}
                className="leader-row"
                style={{ background: 'var(--bg-2)' }}
              >
                <div className={`rank-badge r${(idx % 4) + 1}`}>{idx + 1}</div>
                <Avatar playerId={p.id} name={p.name} hasAvatar={p.hasAvatar} size="sm" />
                <div className="name">{p.name}</div>
                <div className="row gap-sm">
                  <button
                    type="button"
                    className="ghost icon-only"
                    onClick={() => onMove(idx, idx - 1)}
                    disabled={idx === 0}
                    aria-label="Lên"
                    title="Lên"
                  >↑</button>
                  <button
                    type="button"
                    className="ghost icon-only"
                    onClick={() => onMove(idx, idx + 1)}
                    disabled={idx === selectedPlayers.length - 1}
                    aria-label="Xuống"
                    title="Xuống"
                  >↓</button>
                  <button
                    type="button"
                    className="ghost icon-only"
                    onClick={() => onToggle(p.id)}
                    aria-label="Bỏ chọn"
                    title="Bỏ chọn"
                  >×</button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {unselected.length > 0 && (
        <div className="card">
          <div className="section-title">Thêm người chơi</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', marginTop: '0.5rem' }}>
            {unselected.map((p) => (
              <button
                key={p.id}
                type="button"
                className="secondary"
                onClick={() => onToggle(p.id)}
                style={{ padding: '0.4rem 0.75rem 0.4rem 0.4rem', gap: '0.5rem' }}
              >
                {p.hasAvatar ? (
                  <Avatar playerId={p.id} name={p.name} hasAvatar size="sm" />
                ) : (
                  <span className="avatar sm">{initials(p.name)}</span>
                )}
                {p.name}
              </button>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
