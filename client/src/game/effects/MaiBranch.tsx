interface MaiBranchProps {
  corner: 'tl' | 'tr' | 'bl' | 'br';
}

export function MaiBranch({ corner }: MaiBranchProps) {
  return (
    <svg className={`tlmn-mai-corner ${corner}`} viewBox="0 0 140 140" xmlns="http://www.w3.org/2000/svg">
      <path d="M10 130 Q 40 80, 80 60 T 130 20" stroke="#3D2410" strokeWidth="3" fill="none" strokeLinecap="round" />
      <path d="M40 100 Q 60 90, 75 75" stroke="#3D2410" strokeWidth="2" fill="none" strokeLinecap="round" />
      <path d="M70 70 Q 85 55, 100 45" stroke="#3D2410" strokeWidth="2" fill="none" strokeLinecap="round" />
      {[
        { cx: 80, cy: 60 },
        { cx: 110, cy: 35 },
        { cx: 50, cy: 95 },
        { cx: 95, cy: 50 },
        { cx: 65, cy: 75 },
      ].map((p, i) => (
        <Flower key={i} cx={p.cx} cy={p.cy} />
      ))}
    </svg>
  );
}

function Flower({ cx, cy }: { cx: number; cy: number }) {
  return (
    <g transform={`translate(${cx} ${cy})`}>
      {[0, 72, 144, 216, 288].map(a => (
        <ellipse
          key={a}
          cx={0}
          cy={-6}
          rx={3.6}
          ry={5.4}
          fill="#F5C842"
          stroke="#D4A445"
          strokeWidth={0.6}
          transform={`rotate(${a})`}
        />
      ))}
      <circle r={2} fill="#C8302C" />
    </g>
  );
}
