import { motion, AnimatePresence } from 'framer-motion';
import { Card } from './cards';
import { CardSvg } from './CardSvg';

interface PlayAreaProps {
  cards: Card[];
  fromSeat: 'top' | 'left' | 'right' | 'bottom' | null;
}

const FROM: Record<'top' | 'left' | 'right' | 'bottom', { x: number; y: number }> = {
  top: { x: 0, y: -240 },
  bottom: { x: 0, y: 240 },
  left: { x: -360, y: 0 },
  right: { x: 360, y: 0 },
};

export function PlayArea({ cards, fromSeat }: PlayAreaProps) {
  const origin = fromSeat ? FROM[fromSeat] : { x: 0, y: 0 };
  return (
    <div className="tlmn-play-area">
      <AnimatePresence mode="popLayout">
        {cards.map((c, i) => (
          <motion.div
            key={c.id}
            className="tlmn-play-card"
            initial={{ x: origin.x, y: origin.y, opacity: 0, scale: 0.6, rotate: -20 }}
            animate={{ x: 0, y: 0, opacity: 1, scale: 1, rotate: (i - (cards.length - 1) / 2) * 4 }}
            exit={{ opacity: 0, scale: 0.4, transition: { duration: 0.25 } }}
            transition={{ type: 'spring', stiffness: 260, damping: 22, delay: i * 0.04 }}
          >
            <CardSvg card={c} size="md" />
          </motion.div>
        ))}
      </AnimatePresence>
    </div>
  );
}
