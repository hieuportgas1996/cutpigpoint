import { motion, AnimatePresence } from 'framer-motion';
import { Card } from './cards';
import { CardSvg } from './CardSvg';

interface HandProps {
  cards: Card[];
  selectedIds: Set<string>;
  onToggle: (cardId: string) => void;
  dealt: boolean;
}

export function Hand({ cards, selectedIds, onToggle, dealt }: HandProps) {
  const spread = Math.min(28, 480 / Math.max(cards.length, 1));
  return (
    <div className="tlmn-hand">
      <AnimatePresence>
        {dealt && cards.map((c, idx) => {
          const offset = (idx - (cards.length - 1) / 2) * spread;
          const rot = (idx - (cards.length - 1) / 2) * 1.4;
          return (
            <motion.div
              key={c.id}
              className="tlmn-hand-slot"
              initial={{ x: -offset, y: -200, opacity: 0, rotate: rot - 30 }}
              animate={{ x: offset, y: 0, opacity: 1, rotate: rot }}
              exit={{ y: -260, opacity: 0, scale: 0.6, transition: { duration: 0.35 } }}
              transition={{ type: 'spring', stiffness: 220, damping: 24, delay: idx * 0.018 }}
              style={{ position: 'absolute', left: '50%', marginLeft: -32 }}
            >
              <CardSvg
                card={c}
                size="md"
                selected={selectedIds.has(c.id)}
                onClick={() => onToggle(c.id)}
              />
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}
