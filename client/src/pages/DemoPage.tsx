import { useMemo, useState } from 'react';
import { Card, dealFour } from '../game/cards';
import { Hand } from '../game/Hand';
import { PlayArea } from '../game/PlayArea';
import { Table } from '../game/Table';
import { SeatData } from '../game/Seat';
import '../game/demo.css';

type SeatPos = 'top' | 'left' | 'right' | 'bottom';

const PLAYERS = [
  { name: 'Bạn', initial: 'B' },
  { name: 'Hoa', initial: 'H' },
  { name: 'Minh', initial: 'M' },
  { name: 'Lan', initial: 'L' },
];

export default function DemoPage() {
  const [seed, setSeed] = useState(() => Date.now());
  const hands = useMemo(() => dealFour(seed), [seed]);

  const [dealt, setDealt] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [played, setPlayed] = useState<Card[]>([]);
  const [playFrom, setPlayFrom] = useState<SeatPos | null>(null);
  const [myHand, setMyHand] = useState<Card[]>([]);
  const [counts, setCounts] = useState<[number, number, number, number]>([0, 0, 0, 0]);
  const [scores, setScores] = useState<[number, number, number, number]>([0, 0, 0, 0]);
  const [turn, setTurn] = useState<0 | 1 | 2 | 3>(0);
  const [passed, setPassed] = useState<[boolean, boolean, boolean, boolean]>([false, false, false, false]);
  const [winner, setWinner] = useState<string | null>(null);
  const [ranks, setRanks] = useState<[undefined | 1 | 2 | 3 | 4, undefined | 1 | 2 | 3 | 4, undefined | 1 | 2 | 3 | 4, undefined | 1 | 2 | 3 | 4]>([undefined, undefined, undefined, undefined]);

  const toggle = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleDeal = () => {
    setDealt(false);
    setPlayed([]);
    setPlayFrom(null);
    setSelected(new Set());
    setPassed([false, false, false, false]);
    setWinner(null);
    setRanks([undefined, undefined, undefined, undefined]);
    setScores([0, 0, 0, 0]);
    setTurn(0);
    setSeed(Date.now());
    setTimeout(() => {
      setMyHand(hands[0]);
      setCounts([hands[0].length, hands[1].length, hands[2].length, hands[3].length]);
      setDealt(true);
    }, 60);
  };

  const handlePlayMine = () => {
    if (selected.size === 0) return;
    const toPlay = myHand.filter(c => selected.has(c.id));
    setPlayed(toPlay);
    setPlayFrom('bottom');
    setMyHand(prev => prev.filter(c => !selected.has(c.id)));
    setCounts(prev => [prev[0] - toPlay.length, prev[1], prev[2], prev[3]]);
    setSelected(new Set());
    setPassed([false, false, false, false]);
    setTurn(1);
  };

  const fakeOtherPlay = (seatIdx: 1 | 2 | 3) => {
    const fakeCard = hands[seatIdx][counts[seatIdx] - 1] ?? hands[seatIdx][0];
    if (!fakeCard) return;
    setPlayed([fakeCard]);
    const pos: SeatPos = seatIdx === 1 ? 'right' : seatIdx === 2 ? 'top' : 'left';
    setPlayFrom(pos);
    setCounts(prev => {
      const next = [...prev] as [number, number, number, number];
      next[seatIdx] = Math.max(0, next[seatIdx] - 1);
      return next;
    });
    setPassed([false, false, false, false]);
    setTurn(((seatIdx + 1) % 4) as 0 | 1 | 2 | 3);
  };

  const handlePass = () => {
    setPassed(prev => {
      const next = [...prev] as [boolean, boolean, boolean, boolean];
      next[turn] = true;
      return next;
    });
    setTurn(prev => ((prev + 1) % 4) as 0 | 1 | 2 | 3);
  };

  const handleWin = () => {
    setWinner(PLAYERS[0].name);
    setRanks([1, 2, 3, 4]);
    setScores([2, 1, -1, -2]);
  };

  const seats: [SeatData, SeatData, SeatData, SeatData] = [
    { id: 0, name: PLAYERS[0].name, initial: PLAYERS[0].initial, cardsLeft: counts[0], score: scores[0], isTurn: turn === 0 && !winner, passed: passed[0], rank: ranks[0] },
    { id: 1, name: PLAYERS[1].name, initial: PLAYERS[1].initial, cardsLeft: counts[1], score: scores[1], isTurn: turn === 1 && !winner, passed: passed[1], rank: ranks[1] },
    { id: 2, name: PLAYERS[2].name, initial: PLAYERS[2].initial, cardsLeft: counts[2], score: scores[2], isTurn: turn === 2 && !winner, passed: passed[2], rank: ranks[2] },
    { id: 3, name: PLAYERS[3].name, initial: PLAYERS[3].initial, cardsLeft: counts[3], score: scores[3], isTurn: turn === 3 && !winner, passed: passed[3], rank: ranks[3] },
  ];

  return (
    <div className="tlmn-root">
      <div className="tlmn-stage">
        <div className="tlmn-title">Tiến Lên Miền Nam</div>
        <div className="tlmn-subtitle">Prototype đồ họa — bộ điều khiển bên dưới để thử animation</div>

        <Table
          seats={seats}
          centerSlot={
            <>
              <PlayArea cards={played} fromSeat={playFrom} />
              <Hand cards={myHand} selectedIds={selected} onToggle={toggle} dealt={dealt} />
            </>
          }
          winnerName={winner}
        />

        <div className="tlmn-controls">
          <button className="tlmn-btn primary" onClick={handleDeal}>🎴 Chia bài</button>
          <button className="tlmn-btn" onClick={handlePlayMine} disabled={!dealt || selected.size === 0 || !!winner}>▶ Đánh bài</button>
          <button className="tlmn-btn ghost" onClick={handlePass} disabled={!dealt || !!winner}>↷ Bỏ qua</button>
          <button className="tlmn-btn" onClick={() => fakeOtherPlay(1)} disabled={!dealt || !!winner}>P2 đánh</button>
          <button className="tlmn-btn" onClick={() => fakeOtherPlay(2)} disabled={!dealt || !!winner}>P3 đánh</button>
          <button className="tlmn-btn" onClick={() => fakeOtherPlay(3)} disabled={!dealt || !!winner}>P4 đánh</button>
          <button className="tlmn-btn primary" onClick={handleWin} disabled={!dealt}>🎉 Về nhất</button>
        </div>
      </div>
    </div>
  );
}
