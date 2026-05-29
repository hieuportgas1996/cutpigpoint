import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useRoomConnection } from '../hooks/useRoomConnection';
import { useToast } from '../ui/Toast';
import { api, RoomState, RoomStatus } from '../api';
import '../game/demo.css';
import './room-lobby.css';
import './room-play.css';
import { MaiBranch } from '../game/effects/MaiBranch';
import { playLoop } from '../sounds';

const LOBBY_BGM_KEY = 'cutpig.lobbyBgmOn';
function readLobbyBgmPref(): boolean {
  try {
    const v = localStorage.getItem(LOBBY_BGM_KEY);
    return v === null ? true : v === '1';
  } catch { return true; }
}

const SEAT_POSITIONS: Array<'bottom' | 'right' | 'top' | 'left'> = ['bottom', 'right', 'top', 'left'];

export default function RoomLobbyPage() {
  const { code } = useParams<{ code: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { state } = useAuth();
  const { status, state: room, error, takeSeat, leaveSeat, startGame, onGameStarted, chatMessages, sendChat } =
    useRoomConnection(code);

  const [chatOpen, setChatOpen] = useState(false);
  const [chatInput, setChatInput] = useState('');
  const [chatSeenCount, setChatSeenCount] = useState(0);
  const chatListRef = useRef<HTMLDivElement | null>(null);
  const [seatBubbles, setSeatBubbles] = useState<Record<string, { id: string; text: string }>>({});
  const lastBubbledChatId = useRef<string | null>(null);

  useEffect(() => {
    if (chatOpen && chatListRef.current) {
      chatListRef.current.scrollTop = chatListRef.current.scrollHeight;
    }
    if (chatOpen) setChatSeenCount(chatMessages.length);
  }, [chatMessages.length, chatOpen]);

  useEffect(() => {
    if (chatMessages.length === 0) return;
    const latest = chatMessages[chatMessages.length - 1];
    if (lastBubbledChatId.current === latest.id) return;
    lastBubbledChatId.current = latest.id;
    setSeatBubbles(prev => ({ ...prev, [latest.userId]: { id: latest.id, text: latest.text } }));
    const t = setTimeout(() => {
      setSeatBubbles(prev => {
        if (prev[latest.userId]?.id !== latest.id) return prev;
        const { [latest.userId]: _drop, ...rest } = prev;
        return rest;
      });
    }, 5000);
    return () => clearTimeout(t);
  }, [chatMessages]);

  const unreadChat = Math.max(0, chatMessages.length - chatSeenCount);

  async function handleSendChat() {
    const text = chatInput.trim();
    if (!text) return;
    setChatInput('');
    try { await sendChat(text); }
    catch (e) { toast.push('error', (e as Error).message); }
  }

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

  const [bgmOn, setBgmOn] = useState<boolean>(readLobbyBgmPref);
  useEffect(() => {
    try { localStorage.setItem(LOBBY_BGM_KEY, bgmOn ? '1' : '0'); } catch { /* no-op */ }
  }, [bgmOn]);
  useEffect(() => {
    if (!bgmOn) return;
    if (room?.status !== RoomStatus.Waiting) return;
    const stop = playLoop('backgroundLobby', 0.35);
    return stop;
  }, [bgmOn, room?.status]);

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
          <button
            type="button"
            className="tlmn-btn ghost sm"
            onClick={() => setBgmOn(v => !v)}
            title={bgmOn ? 'Tắt nhạc nền' : 'Bật nhạc nền'}
          >
            {bgmOn ? '🔊 Nhạc' : '🔇 Nhạc'}
          </button>
          <div className="muted small">
            {room.seats.length}/{room.maxSeats} người
          </div>
        </div>

        <div className="tlmn-table">
          <MaiBranch corner="tl" />
          <MaiBranch corner="tr" />
          <MaiBranch corner="bl" />
          <MaiBranch corner="br" />

          {Array.from({ length: room.maxSeats }, (_, i) => {
            const seat = room.seats.find(s => s.seatIndex === i);
            const bubble = seat ? seatBubbles[seat.userId] : undefined;
            return (
              <SeatSlot
                key={i}
                seatIndex={i}
                position={SEAT_POSITIONS[i % SEAT_POSITIONS.length]}
                room={room}
                meId={state.userId}
                bubble={bubble}
                onTake={async () => {
                  try { await takeSeat(i); } catch (e) { toast.push('error', (e as Error).message); }
                }}
                onLeave={async () => {
                  try { await leaveSeat(); } catch (e) { toast.push('error', (e as Error).message); }
                }}
              />
            );
          })}

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

        <button
          className="chat-fab"
          onClick={() => setChatOpen(o => !o)}
          title="Chat trong phòng"
          aria-label="Mở chat"
        >
          💬
          {unreadChat > 0 && <span className="chat-fab-badge">{unreadChat}</span>}
        </button>

        {chatOpen && (
          <div className="chat-panel">
            <div className="chat-panel-header">
              <span>💬 Chat phòng</span>
              <button className="tlmn-btn ghost sm" onClick={() => setChatOpen(false)}>✕</button>
            </div>
            <div className="chat-panel-list" ref={chatListRef}>
              {chatMessages.length === 0 ? (
                <div className="muted small">Chưa có tin nhắn nào.</div>
              ) : (
                chatMessages.map(m => (
                  <div key={m.id} className={`chat-msg ${m.userId === state.userId ? 'mine' : ''}`}>
                    <div className="chat-msg-meta">
                      <span className="chat-msg-name">{m.userId === state.userId ? 'Bạn' : m.displayName}</span>
                      <span className="chat-msg-time muted small">
                        {new Date(m.createdAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                      </span>
                    </div>
                    <div className="chat-msg-text">{m.text}</div>
                  </div>
                ))
              )}
            </div>
            <form
              className="chat-panel-input"
              onSubmit={e => { e.preventDefault(); handleSendChat(); }}
            >
              <input
                type="text"
                placeholder="Nhập tin nhắn…"
                value={chatInput}
                onChange={e => setChatInput(e.target.value)}
                maxLength={300}
              />
              <button type="submit" className="tlmn-btn primary sm" disabled={!chatInput.trim()}>Gửi</button>
            </form>
          </div>
        )}
      </div>
    </div>
  );
}

interface SeatSlotProps {
  seatIndex: number;
  position: 'bottom' | 'right' | 'top' | 'left';
  room: RoomState;
  meId: string;
  bubble?: { id: string; text: string };
  onTake: () => void;
  onLeave: () => void;
}

function SeatSlot({ seatIndex, position, room, meId, bubble, onTake, onLeave }: SeatSlotProps) {
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
      {bubble && <div key={bubble.id} className="seat-chat-bubble">{bubble.text}</div>}
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
