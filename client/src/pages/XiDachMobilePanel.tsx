import type { MatchPlayerPublic } from '../api';
import { Card, cardFromDto } from '../game/cards';
import { CardSvg } from '../game/CardSvg';
import { Avatar } from '../ui/Avatar';
import './xidach-mobile.css';

/**
 * Layout Xì Dách RIÊNG cho điện thoại — full-screen, xếp dọc 1 hàng/người (tinh gọn): avatar + tên +
 * nhãn Nhà Cái + bài (mình & người đã lật hiện mặt, chưa lật úp lưng theo số lá) + số lá + điểm/nhãn +
 * trạng thái (dừng/đã xét) + nút "Xét bài" (nhà cái). Header: tên Nhà Cái + trạng thái lượt. Footer: nút
 * Rút / Dừng / Xét hết. Thay cho layout bàn tròn (bị chồng lá khi màn nhỏ) — chỉ render khi isMobile.
 */
export function XiDachMobilePanel({
  players, myUserId, dealerName, isCompare, iAmDealer, isMyTurn, turnName, turnLeftSec,
  myHand, myLabel, myCount, myCanDraw, myCanStand, myTotal,
  dealerCanCompare, anyUnsettled, playerDone, handLabelOf,
  onDraw, onStand, onCompare, onCompareAll,
}: {
  players: MatchPlayerPublic[];
  myUserId: string;
  dealerName: string;
  isCompare: boolean;
  iAmDealer: boolean;
  isMyTurn: boolean;
  turnName: string;
  turnLeftSec: number;
  myHand: Card[];
  myLabel: string;
  myCount: number;
  myCanDraw: boolean;
  myCanStand: boolean;
  myTotal: number;
  dealerCanCompare: boolean;
  anyUnsettled: boolean;
  playerDone: (p: MatchPlayerPublic) => boolean;
  handLabelOf: (cards: Card[]) => string;
  onDraw: () => void;
  onStand: () => void;
  onCompare: (userId: string) => void;
  onCompareAll: () => void;
}) {
  // Nhà cái lên đầu, rồi theo seat.
  const rows = [...players].sort((a, b) =>
    (b.isXiDachDealer ? 1 : 0) - (a.isXiDachDealer ? 1 : 0) || a.seatIndex - b.seatIndex);

  return (
    <div className="xdm-overlay">
      <div className="xdm-card">
        <div className="xdm-title">🃏 Sát Phạt — {dealerName || '?'} làm cái</div>
        <div className="xdm-status">
          {isCompare
            ? (iAmDealer ? 'Bấm “Xét bài” từng người (hoặc “Xét hết”)' : 'Nhà cái đang xét bài…')
            : isMyTurn
              ? <>Lượt của <b>bạn</b> · <b className={turnLeftSec <= 5 ? 'low' : ''}>{turnLeftSec}s</b></>
              : <>Đang chờ <b>{turnName || '...'}</b> · {turnLeftSec}s</>}
        </div>

        <div className="xdm-list">
          {rows.map(p => {
            const isMe = p.userId === myUserId;
            const revealed = p.xiDachRevealed && p.xiDachVisibleCards;
            const cards: Card[] = isMe ? myHand : (revealed ? p.xiDachVisibleCards!.map(cardFromDto) : []);
            const showCompare = dealerCanCompare && !p.isXiDachDealer && !p.xiDachSettled && playerDone(p);
            const label = isMe ? myLabel : (revealed ? handLabelOf(p.xiDachVisibleCards!.map(cardFromDto)) : null);
            return (
              <div key={p.userId} className={`xdm-row ${p.isXiDachDealer ? 'dealer' : ''} ${isMe ? 'me' : ''} ${isMyTurn && isMe ? 'turn' : ''}`}>
                <div className="xdm-row-head">
                  <Avatar name={isMe ? 'Bạn' : p.displayName} hasAvatar={p.hasAvatar} playerId={p.userId} size="sm" />
                  <div className="xdm-row-name">
                    {p.isXiDachDealer && <span className="xdm-dealer-badge">🏦 Cái</span>}
                    {isMe ? 'Bạn' : p.displayName}
                  </div>
                  <div className="xdm-row-status">
                    {p.xiDachStood && !p.xiDachSettled && <span className="xdm-stood">DỪNG</span>}
                    {p.xiDachSettled && <span className="xdm-settled">✓ đã xét</span>}
                  </div>
                </div>
                <div className="xdm-row-cards">
                  {cards.map((c, i) => <CardSvg key={i} card={c} size="sm" />)}
                  {!isMe && !revealed && Array.from({ length: p.cardsLeft }).map((_, i) => (
                    <CardSvg key={`b${i}`} faceDown size="sm" />
                  ))}
                  <span className="xdm-row-info">
                    <span className="xdm-count">{p.cardsLeft} lá</span>
                    {label && <span className="xdm-label">· {label}</span>}
                  </span>
                  {showCompare && (
                    <button className="tlmn-btn primary xdm-compare" onClick={() => onCompare(p.userId)}>Xét bài</button>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        <div className="xdm-actions">
          {!isCompare && isMyTurn && (
            <>
              <button className="tlmn-btn primary" disabled={!myCanDraw} onClick={onDraw}>🎴 Rút ({myCount})</button>
              <button className="tlmn-btn ghost" disabled={!(myCanStand || myTotal > 21)} onClick={onStand}>✋ Dừng ({myTotal})</button>
            </>
          )}
          {dealerCanCompare && anyUnsettled && (
            <button className="tlmn-btn primary" onClick={onCompareAll}>⚖️ Xét hết</button>
          )}
          {!isCompare && !isMyTurn && (
            <span className="xdm-wait">Đang chờ {turnName || '...'}…</span>
          )}
        </div>
      </div>
    </div>
  );
}
