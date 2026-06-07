import { BreakGameType } from '../api';
import './break-select.css';

// Tên game hiển thị cho người chơi (theo yêu cầu): Oẳn Tù Xì → "May mắn".
export const BREAK_GAME_META: Record<number, { label: string; emoji: string; tagline: string; rules: string[] }> = {
  [BreakGameType.Rps]: {
    label: 'May mắn',
    emoji: '🎲',
    tagline: 'Oẳn tù xì đối kháng',
    rules: [
      'Giải đấu Oẳn Tù Xì (búa ✊ / bao ✋ / kéo ✌️) cho cả 4 người.',
      'Vòng 1 chia 2 cặp đấu (Bo3 — ai thắng trước 2 ván). 2 người thắng vào Chung kết, 2 người thua tranh hạng 3.',
      'Tranh hạng 3 (Bo3) và Chung kết (Bo5 — thắng trước 3 ván).',
      'Mỗi ván có 20s để chọn; không chọn kịp sẽ bị random.',
      'Xếp hạng cuối: Nhất +2 · Nhì +1 · Ba −1 · Bét −2 điểm.',
    ],
  },
  [BreakGameType.Math]: {
    label: 'Tính toán',
    emoji: '🧮',
    tagline: 'Tính nhẩm nhanh',
    rules: [
      'Mỗi người chọn 1 chữ số (0–9), 4 số ghép thành 2 phép tính.',
      'Mỗi phép tính là 1 câu trắc nghiệm 4 đáp án — chọn kết quả đúng.',
      'Mỗi câu có 20s; trả lời càng đúng & càng nhanh càng tốt.',
      'Xếp hạng: nhiều câu đúng hơn → cao hơn; bằng nhau thì ai nhanh hơn.',
      'Hạng: Nhất +2 · Nhì +1 · Ba −1 · Bét −2 điểm.',
    ],
  },
  [BreakGameType.Memory]: {
    label: 'Trí nhớ',
    emoji: '🧠',
    tagline: 'Ghi nhớ logo đội bóng',
    rules: [
      'Hiện lưới 3×3 gồm 9 logo CLB bóng đá khác nhau trong 10 giây — ghi nhớ.',
      'Lưới ẩn đi, hỏi "Ô số X là đội nào?" với 4 logo đáp án.',
      'Mỗi câu có 20s; trả lời đúng & nhanh để xếp hạng cao.',
      'Xếp hạng: nhiều câu đúng hơn → cao hơn; bằng nhau thì ai nhanh hơn.',
      'Hạng: Nhất +2 · Nhì +1 · Ba −1 · Bét −2 điểm.',
    ],
  },
  [BreakGameType.Reflex]: {
    label: 'Phản xạ',
    emoji: '⚡',
    tagline: 'Tìm 3 lá bài nhanh nhất',
    rules: [
      'Lưới 4×4 gồm 16 lá bài. Đề yêu cầu tìm đúng 3 lá nhất định.',
      'Lưới bị che 3 giây (đếm ngược) trước khi cho click — không nhìn trước được.',
      'Bấm đúng đủ 3 lá theo đề càng nhanh càng tốt; chơi 3 lượt.',
      'Xếp hạng: nhiều lượt đúng hơn → cao hơn; bằng nhau thì ai nhanh hơn.',
      'Hạng: Nhất +2 · Nhì +1 · Ba −1 · Bét −2 điểm.',
    ],
  },
};

const GAME_ORDER = [BreakGameType.Rps, BreakGameType.Math, BreakGameType.Memory, BreakGameType.Reflex];

// Pha 1: người tổ chức chọn game (modal option). Người khác chờ.
export function BreakSelectScreen({
  organizerId, organizerName, myUserId, leftSec, onSelect,
}: {
  organizerId: string | null;
  organizerName: string;
  myUserId: string;
  leftSec: number;
  onSelect: (gameType: number) => void;
}) {
  const iAmOrganizer = organizerId != null && organizerId === myUserId;
  return (
    <div className="brk-overlay">
      <div className="brk-card" onClick={e => e.stopPropagation()}>
        <div className="brk-title">🎮 Giải lao zui zẻ</div>
        <div className="brk-sub">
          {iAmOrganizer
            ? <>Chọn một trò chơi cho cả bàn ({leftSec}s)</>
            : <><b>{organizerName || 'Người tổ chức'}</b> đang chọn trò chơi… ({leftSec}s)</>}
        </div>
        <div className="brk-options">
          {GAME_ORDER.map(gt => {
            const meta = BREAK_GAME_META[gt];
            return (
              <button
                key={gt}
                className="brk-opt"
                disabled={!iAmOrganizer}
                onClick={iAmOrganizer ? () => onSelect(gt) : undefined}
              >
                <span className="brk-opt-emoji">{meta.emoji}</span>
                <span className="brk-opt-label">{meta.label}</span>
                <span className="brk-opt-tag">{meta.tagline}</span>
              </button>
            );
          })}
        </div>
        <div className="brk-note">
          {iAmOrganizer
            ? 'Không chọn kịp sẽ bốc ngẫu nhiên 1 trò.'
            : 'Chỉ người tổ chức được chọn — bạn cùng xem nhé.'}
        </div>
      </div>
    </div>
  );
}

// Pha 2: hiện luật chơi game đã chọn (30s tự bắt đầu; người tổ chức có thể bấm "Chơi ngay" skip).
export function BreakIntroScreen({
  gameType, leftSec, iAmOrganizer, onStart,
}: {
  gameType: number;
  leftSec: number;
  iAmOrganizer: boolean;
  onStart: () => void;
}) {
  const meta = BREAK_GAME_META[gameType];
  if (!meta) return null;
  return (
    <div className="brk-overlay">
      <div className="brk-card" onClick={e => e.stopPropagation()}>
        <div className="brk-intro-emoji">{meta.emoji}</div>
        <div className="brk-title">{meta.label}</div>
        <div className="brk-sub">{meta.tagline}</div>
        <ul className="brk-rules">
          {meta.rules.map((r, i) => <li key={i}>{r}</li>)}
        </ul>
        {iAmOrganizer ? (
          <button className="brk-start-btn" onClick={onStart}>
            ▶ Chơi ngay <span className="brk-start-sub">({leftSec}s)</span>
          </button>
        ) : (
          <div className="brk-countdown">Bắt đầu sau <b>{leftSec}s</b>…</div>
        )}
      </div>
    </div>
  );
}
