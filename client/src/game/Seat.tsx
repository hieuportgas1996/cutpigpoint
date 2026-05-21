import clsx from 'clsx';
import { motion, AnimatePresence } from 'framer-motion';

export interface SeatData {
  id: number;
  name: string;
  initial: string;
  cardsLeft: number;
  score: number;
  isTurn: boolean;
  passed: boolean;
  rank?: 1 | 2 | 3 | 4;
}

interface SeatProps {
  data: SeatData;
  position: 'top' | 'left' | 'right' | 'bottom';
}

const RANK_LABEL: Record<1 | 2 | 3 | 4, string> = {
  1: 'Nhất', 2: 'Nhì', 3: 'Ba', 4: 'Tư',
};

export function Seat({ data, position }: SeatProps) {
  const scoreClass = data.score > 0 ? 'pos' : data.score < 0 ? 'neg' : '';
  return (
    <div className={clsx('tlmn-seat', `tlmn-seat-${position}`, data.isTurn && 'is-turn')}>
      <div className="tlmn-avatar">{data.initial}</div>
      <div className="tlmn-seat-info">
        <div className="tlmn-seat-name">{data.name}</div>
        <div className="tlmn-seat-meta">
          <span className="tlmn-card-count">🂠 {data.cardsLeft}</span>
          <span className={clsx('tlmn-score-pill', scoreClass)}>
            {data.score > 0 ? `+${data.score}` : data.score}
          </span>
        </div>
      </div>
      <AnimatePresence>
        {data.passed && (
          <motion.div
            key="pass"
            className="tlmn-seat-pass"
            initial={{ opacity: 0, y: -8, scale: 0.6 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, scale: 0.8 }}
            transition={{ type: 'spring', stiffness: 400, damping: 18 }}
          >
            BỎ QUA
          </motion.div>
        )}
        {data.rank && (
          <motion.div
            key="rank"
            className="tlmn-seat-pass"
            style={{ background: '#D4A445', color: '#1A0E08', top: -10, right: -10 }}
            initial={{ opacity: 0, scale: 0.4, rotate: -20 }}
            animate={{ opacity: 1, scale: 1, rotate: 0 }}
            transition={{ type: 'spring', stiffness: 360, damping: 16 }}
          >
            {RANK_LABEL[data.rank]}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
