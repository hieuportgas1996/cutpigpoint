import clsx from 'clsx';
import { Card, SUIT_COLOR, SUIT_GLYPH, rankLabel } from './cards';

interface CardSvgProps {
  card?: Card;
  faceDown?: boolean;
  selected?: boolean;
  dim?: boolean;
  size?: 'sm' | 'md' | 'lg';
  onClick?: () => void;
}

const SIZE: Record<NonNullable<CardSvgProps['size']>, { w: number; h: number }> = {
  sm: { w: 44, h: 64 },
  md: { w: 64, h: 92 },
  lg: { w: 84, h: 120 },
};

export function CardSvg({ card, faceDown, selected, dim, size = 'md', onClick }: CardSvgProps) {
  const { w, h } = SIZE[size];
  return (
    <div
      className={clsx('tlmn-card', selected && 'is-selected', dim && 'is-dim', onClick && 'is-clickable')}
      style={{ width: w, height: h }}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
    >
      {faceDown || !card ? <CardBack w={w} h={h} /> : <CardFace card={card} w={w} h={h} />}
    </div>
  );
}

function CardFace({ card, w, h }: { card: Card; w: number; h: number }) {
  const color = SUIT_COLOR[card.suit];
  const glyph = SUIT_GLYPH[card.suit];
  const label = rankLabel(card.rank);
  const fill = color === 'red' ? '#C8302C' : '#1A0E08';
  return (
    <svg viewBox={`0 0 ${w} ${h}`} width={w} height={h} xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id={`face-${card.id}`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#FFFBEC" />
          <stop offset="100%" stopColor="#F5E6C8" />
        </linearGradient>
      </defs>
      <rect x={1} y={1} width={w - 2} height={h - 2} rx={8} ry={8} fill={`url(#face-${card.id})`} stroke="#D4A445" strokeWidth={1.2} />
      <rect x={3} y={3} width={w - 6} height={h - 6} rx={6} ry={6} fill="none" stroke="#D4A44533" strokeWidth={0.6} />
      <text x={6} y={16} fontFamily="'Be Vietnam Pro', serif" fontWeight={800} fontSize={14} fill={fill}>{label}</text>
      <text x={6} y={28} fontSize={12} fill={fill}>{glyph}</text>
      <text x={w / 2} y={h / 2 + 8} textAnchor="middle" fontSize={Math.min(w, h) * 0.45} fill={fill} opacity={0.92}>{glyph}</text>
      <text x={w - 6} y={h - 8} textAnchor="end" fontFamily="'Be Vietnam Pro', serif" fontWeight={800} fontSize={14} fill={fill} transform={`rotate(180 ${w - 6} ${h - 8})`}>{label}</text>
      <text x={w - 6} y={h - 20} textAnchor="end" fontSize={12} fill={fill} transform={`rotate(180 ${w - 6} ${h - 20})`}>{glyph}</text>
    </svg>
  );
}

function CardBack({ w, h }: { w: number; h: number }) {
  return (
    <svg viewBox={`0 0 ${w} ${h}`} width={w} height={h} xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="back-grad" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#A21F1B" />
          <stop offset="100%" stopColor="#6B0F0C" />
        </linearGradient>
        <pattern id="back-mai" x="0" y="0" width="14" height="14" patternUnits="userSpaceOnUse">
          <circle cx="7" cy="7" r="1.5" fill="#D4A445" opacity="0.7" />
          <circle cx="0" cy="0" r="0.8" fill="#D4A445" opacity="0.5" />
          <circle cx="14" cy="14" r="0.8" fill="#D4A445" opacity="0.5" />
        </pattern>
      </defs>
      <rect x={1} y={1} width={w - 2} height={h - 2} rx={8} ry={8} fill="url(#back-grad)" stroke="#D4A445" strokeWidth={1.5} />
      <rect x={4} y={4} width={w - 8} height={h - 8} rx={6} ry={6} fill="url(#back-mai)" opacity={0.6} />
      <g transform={`translate(${w / 2}, ${h / 2})`}>
        {[0, 72, 144, 216, 288].map(a => (
          <ellipse key={a} cx={0} cy={-8} rx={4} ry={6} fill="#F5C842" opacity={0.95} transform={`rotate(${a})`} />
        ))}
        <circle r={3} fill="#C8302C" stroke="#D4A445" strokeWidth={0.5} />
      </g>
    </svg>
  );
}
