import { useMemo } from 'react';
import { motion } from 'framer-motion';

interface ConfettiProps {
  active: boolean;
  count?: number;
}

export function Confetti({ active, count = 60 }: ConfettiProps) {
  const petals = useMemo(() => {
    return Array.from({ length: count }, (_, i) => ({
      id: i,
      x: Math.random() * 100,
      delay: Math.random() * 1.2,
      duration: 2.4 + Math.random() * 2,
      drift: (Math.random() - 0.5) * 60,
      rotate: Math.random() * 360,
      scale: 0.6 + Math.random() * 1.2,
    }));
  }, [count]);

  if (!active) return null;

  return (
    <div className="tlmn-confetti">
      {petals.map(p => (
        <motion.div
          key={p.id}
          className="tlmn-petal"
          initial={{ top: '-5%', left: `${p.x}%`, opacity: 0, rotate: 0 }}
          animate={{
            top: '110%',
            left: `calc(${p.x}% + ${p.drift}px)`,
            opacity: [0, 1, 1, 0.7, 0],
            rotate: p.rotate + 360,
          }}
          transition={{
            duration: p.duration,
            delay: p.delay,
            ease: 'easeIn',
            times: [0, 0.1, 0.7, 0.9, 1],
          }}
          style={{ transform: `scale(${p.scale})` }}
        />
      ))}
    </div>
  );
}
