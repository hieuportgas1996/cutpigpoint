// Vẽ các hình cho game Phản xạ + tên tiếng Việt. Key khớp server ReflexGameEngine.Shapes/Colors.

export const SHAPE_NAME: Record<string, string> = {
  circle: 'hình tròn',
  square: 'hình vuông',
  oval: 'hình bầu dục',
  rectangle: 'hình chữ nhật',
  triangle: 'hình tam giác',
  trapezoid: 'hình thang',
  pentagon: 'hình ngũ giác',
  star: 'hình ngôi sao',
};

export const COLOR_HEX: Record<string, string> = {
  red: '#e23b3b',
  blue: '#3b82f6',
  green: '#22c55e',
  yellow: '#f5c518',
  orange: '#f97316',
  purple: '#a855f7',
  pink: '#ec4899',
  cyan: '#06b6d4',
  white: '#f3f4f6',
  brown: '#92633a',
};

export const COLOR_NAME: Record<string, string> = {
  red: 'đỏ',
  blue: 'xanh dương',
  green: 'xanh lá',
  yellow: 'vàng',
  orange: 'cam',
  purple: 'tím',
  pink: 'hồng',
  cyan: 'xanh ngọc',
  white: 'trắng',
  brown: 'nâu',
};

export function shapeName(s: string) { return SHAPE_NAME[s] ?? s; }
export function colorName(c: string) { return COLOR_NAME[c] ?? c; }

/** Vẽ 1 hình (shape) tô màu fill, trong viewBox 0..100. */
export function ShapeSvg({ shape, color, size = 56 }: { shape: string; color: string; size?: number }) {
  const fill = COLOR_HEX[color] ?? '#999';
  const stroke = 'rgba(0,0,0,0.35)';
  const common = { fill, stroke, strokeWidth: 2 } as const;
  let el: JSX.Element;
  switch (shape) {
    case 'circle':
      el = <circle cx={50} cy={50} r={42} {...common} />; break;
    case 'square':
      el = <rect x={10} y={10} width={80} height={80} rx={6} {...common} />; break;
    case 'oval':
      el = <ellipse cx={50} cy={50} rx={44} ry={28} {...common} />; break;
    case 'rectangle':
      el = <rect x={6} y={26} width={88} height={48} rx={5} {...common} />; break;
    case 'triangle':
      el = <polygon points="50,8 92,88 8,88" {...common} />; break;
    case 'trapezoid':
      el = <polygon points="26,20 74,20 94,84 6,84" {...common} />; break;
    case 'pentagon':
      el = <polygon points="50,6 92,38 76,90 24,90 8,38" {...common} />; break;
    case 'star':
      el = <polygon points="50,5 61,38 96,38 68,59 79,92 50,72 21,92 32,59 4,38 39,38" {...common} />; break;
    default:
      el = <circle cx={50} cy={50} r={40} {...common} />;
  }
  return (
    <svg viewBox="0 0 100 100" width={size} height={size} aria-label={`${shapeName(shape)} ${colorName(color)}`}>
      {el}
    </svg>
  );
}
