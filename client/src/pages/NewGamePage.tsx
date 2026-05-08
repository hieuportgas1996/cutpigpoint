import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, GameType, Player } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { initials } from '../ui/helpers';
import { Avatar } from '../ui/Avatar';

type Mode = 'tienlen' | 'manual';

export default function NewGamePage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [mode, setMode] = useState<Mode>('tienlen');
  const [tlSeats, setTlSeats] = useState<Array<string | ''>>(['', '', '', '']);
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

  function toggleManual(id: string) {
    setManualIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  const tlAllSelected = tlSeats.every((s) => s !== '');
  const tlUnique = new Set(tlSeats.filter(Boolean)).size === tlSeats.filter(Boolean).length;
  const manualValid = manualIds.length >= 2;

  async function start() {
    setSubmitting(true);
    try {
      let g;
      if (mode === 'tienlen') {
        if (!tlAllSelected) {
          toast.push('error', 'Phải chọn đủ 4 người chơi');
          return;
        }
        if (!tlUnique) {
          toast.push('error', 'Người chơi không được trùng nhau');
          return;
        }
        g = await api.createGame(tlSeats as string[], GameType.TienLenMienNam);
      } else {
        if (!manualValid) {
          toast.push('error', 'Cần ít nhất 2 người chơi');
          return;
        }
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
            className={mode === 'manual' ? '' : 'secondary'}
            onClick={() => setMode('manual')}
          >
            <Icon name="plus" size={14} /> Tự do — chấm điểm thủ công (≥2 người)
          </button>
        </div>
        {!hasEnoughForTienLen && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Tiến Lên cần đủ 4 người (hiện có {players.length}).
          </div>
        )}
        {mode === 'manual' && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Mỗi round nhập điểm cộng/trừ tự do cho từng người. Không bắt buộc tổng = 0.
          </div>
        )}
      </div>

      {mode === 'tienlen' ? (
        <TienLenSeats
          players={players}
          seats={tlSeats}
          onSet={setSeat}
          onPick={pickPlayer}
          toast={toast}
        />
      ) : (
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
          (mode === 'tienlen' ? !tlAllSelected || !tlUnique : !manualValid)
        }
        className="block-mobile"
      >
        <Icon name="play" size={14} />
        {submitting
          ? 'Đang tạo…'
          : mode === 'tienlen'
          ? 'Bắt đầu ván'
          : `Bắt đầu ván (${manualIds.length} người)`}
      </button>
    </div>
  );
}

function TienLenSeats({
  players,
  seats,
  onSet,
  onPick,
  toast
}: {
  players: Player[];
  seats: Array<string | ''>;
  onSet: (i: number, id: string) => void;
  onPick: (slotIndex: number, p: Player) => void;
  toast: { push: (kind: 'info' | 'success' | 'error', msg: string) => void };
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
                <div className={`rank-badge r${i + 1}`}>{i + 1}</div>
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
          <div className="muted small mt-1">Đã chọn đủ 4 người. Bỏ chọn ở vị trí trên để đổi.</div>
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
                    toast.push('info', 'Đã đủ 4 người, bỏ chọn để thay');
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
