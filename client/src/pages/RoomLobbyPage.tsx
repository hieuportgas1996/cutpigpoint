import { useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { api, RoomState, RoomStatus } from '../api';
import '../game/demo.css';
import './room-lobby.css';
import { MaiBranch } from '../game/effects/MaiBranch';

const SEAT_POSITIONS: Array<'bottom' | 'right' | 'top' | 'left'> = ['bottom', 'right', 'top', 'left'];

export default function RoomLobbyPage() {
  const { code } = useParams<{ code: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const { status, state: room, error, takeSeat, leaveSeat, startGame, onGameStarted } =
    useRoomConnection(code);

  useEffect(() => {
    const unsub = onGameStarted(() => {
      navigate(`/play/${code}`);
    });
    return () => unsub();
  }, [onGameStarted, navigate]);

  useEffect(() => {
    if (room?.status === RoomStatus.Playing && code) {
      navigate(`/play/${code}`);
    }
  }, [room?.status, code, navigate]);

  if (state.status !== 'authenticated') return null;

  if (error) {
    return (
      <div className="card">
        <div style={{ color: 'var(--danger)' }}>{error}</div>
        <button className="ghost sm" onClick={() => navigate('/rooms')}>← Về danh sách phòng</button>
      </div>
    );
  }

  if (status !== 'connected' || !room) {
    return (
      <div className="card">
        <div className="muted">Đang kết nối phòng {code}…</div>
      </div>
    );
  }

  const isHost = room.hostUserId === state.userId;
  const mySeat = room.seats.find(s => s.userId === state.userId);

  return (
    <div className="tlmn-root room-lobby">
      <div className="tlmn-stage">
        <div className="lobby-header">
          <button className="tlmn-btn ghost" onClick={() => navigate('/rooms')}>← Về danh sách</button>
          <div className="lobby-code">
            <span className="muted small">Mã phòng</span>
            <code>{room.code}</code>
          </div>
          <div className="muted small">
            {room.seats.length}/{room.maxSeats} người
          </div>
        </div>

        <div className="tlmn-table">
          <MaiBranch corner="tl" />
          <MaiBranch corner="tr" />
          <MaiBranch corner="bl" />
          <MaiBranch corner="br" />

          {Array.from({ length: room.maxSeats }, (_, i) => (
            <SeatSlot
              key={i}
              seatIndex={i}
              position={SEAT_POSITIONS[i % SEAT_POSITIONS.length]}
              room={room}
              meId={state.userId}
              onTake={async () => {
                try { await takeSeat(i); } catch (e) { toast.push('error', (e as Error).message); }
              }}
              onLeave={async () => {
                try { await leaveSeat(); } catch (e) { toast.push('error', (e as Error).message); }
              }}
            />
          ))}

          <div className="lobby-center">
            <div className="lobby-center-title">Đang chờ người chơi</div>
            <div className="lobby-center-sub muted small">
              {room.seats.length < 2
                ? `Cần thêm ${2 - room.seats.length} người để bắt đầu`
                : isHost
                ? 'Sẵn sàng — bấm Bắt đầu'
                : 'Đang chờ chủ phòng bắt đầu'}
            </div>
          </div>
        </div>

        <div className="tlmn-controls">
          {isHost && (
            <button
              className="tlmn-btn primary"
              onClick={async () => {
                try { await startGame(); } catch (e) { toast.push('error', (e as Error).message); }
              }}
              disabled={room.seats.length < 2}
            >
              🎴 Bắt đầu
            </button>
          )}
          {mySeat && (
            <button className="tlmn-btn ghost" onClick={async () => {
              try { await leaveSeat(); } catch (e) { toast.push('error', (e as Error).message); }
            }}>
              Rời ghế
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

interface SeatSlotProps {
  seatIndex: number;
  position: 'bottom' | 'right' | 'top' | 'left';
  room: RoomState;
  meId: string;
  onTake: () => void;
  onLeave: () => void;
}

function SeatSlot({ seatIndex, position, room, meId, onTake, onLeave }: SeatSlotProps) {
  const seat = room.seats.find(s => s.seatIndex === seatIndex);
  const isMe = seat?.userId === meId;
  const meInOther = !!room.seats.find(s => s.userId === meId && s.seatIndex !== seatIndex);

  if (!seat) {
    return (
      <button
        className={`tlmn-seat tlmn-seat-${position} seat-empty`}
        onClick={onTake}
        disabled={meInOther}
        title={meInOther ? 'Bạn đang ngồi ghế khác' : 'Ngồi vào ghế'}
      >
        <span className="seat-empty-icon">+</span>
        <span className="seat-empty-label">Ngồi vào</span>
      </button>
    );
  }

  return (
    <div className={`tlmn-seat tlmn-seat-${position} ${seat.isOnline ? '' : 'seat-offline'}`}>
      <div className="tlmn-avatar">
        {seat.hasAvatar
          ? <img src={api.userAvatarUrl(seat.userId)} alt={seat.displayName} />
          : seat.displayName.charAt(0).toUpperCase()}
      </div>
      <div className="tlmn-seat-info">
        <div className="tlmn-seat-name">
          {seat.displayName}
          {seat.isHost && <span className="host-badge">CHỦ</span>}
        </div>
        <div className="tlmn-seat-meta">
          {isMe ? (
            <button className="leave-mini" onClick={onLeave}>Rời ghế</button>
          ) : (
            <span className="muted small">{seat.isOnline ? '● Online' : '○ Offline'}</span>
          )}
        </div>
      </div>
    </div>
  );
}
