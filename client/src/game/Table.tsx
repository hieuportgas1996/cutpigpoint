import { ReactNode } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Seat, SeatData } from './Seat';
import { MaiBranch } from './effects/MaiBranch';
import { Confetti } from './effects/Confetti';

interface TableProps {
  seats: [SeatData, SeatData, SeatData, SeatData];
  centerSlot: ReactNode;
  winnerName?: string | null;
}

const POSITIONS: ('bottom' | 'right' | 'top' | 'left')[] = ['bottom', 'right', 'top', 'left'];

export function Table({ seats, centerSlot, winnerName }: TableProps) {
  return (
    <div className="tlmn-table">
      <MaiBranch corner="tl" />
      <MaiBranch corner="tr" />
      <MaiBranch corner="bl" />
      <MaiBranch corner="br" />

      {seats.map((s, i) => (
        <Seat key={s.id} data={s} position={POSITIONS[i]} />
      ))}

      {centerSlot}

      <Confetti active={!!winnerName} />

      <AnimatePresence>
        {winnerName && (
          <motion.div
            key="badge"
            className="tlmn-rank-badge"
            initial={{ scale: 0.2, opacity: 0, rotate: -8 }}
            animate={{ scale: 1, opacity: 1, rotate: 0 }}
            exit={{ scale: 0.5, opacity: 0 }}
            transition={{ type: 'spring', stiffness: 220, damping: 18 }}
          >
            🎉 {winnerName} VỀ NHẤT 🎉
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
