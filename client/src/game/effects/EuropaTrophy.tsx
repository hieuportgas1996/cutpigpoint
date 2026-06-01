// Cúp C2 (UEFA Europa League) silhouette vẽ inline SVG — không dùng logo bản quyền,
// chỉ tái hiện dáng cúp đặc trưng: thân amphora cao thon có gân dọc, 2 quai nhỏ,
// đế bát giác đen. Tông vàng/đồng để phân biệt với cúp C1 (bạc).
export function EuropaTrophy({ size = 84 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size * 1.4}
      viewBox="0 0 100 140"
      fill="none"
      aria-hidden="true"
      className="europa-trophy-svg"
    >
      <defs>
        <linearGradient id="uelGold" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#fff4cf" />
          <stop offset="35%" stopColor="#f0c75e" />
          <stop offset="70%" stopColor="#c79633" />
          <stop offset="100%" stopColor="#9c7421" />
        </linearGradient>
        <linearGradient id="uelShine" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#ffffff" stopOpacity="0.85" />
          <stop offset="55%" stopColor="#ffffff" stopOpacity="0" />
        </linearGradient>
        <radialGradient id="uelGlow" cx="50%" cy="38%" r="62%">
          <stop offset="0%" stopColor="#ffe39a" stopOpacity="0.8" />
          <stop offset="100%" stopColor="#ffe39a" stopOpacity="0" />
        </radialGradient>
      </defs>

      {/* hào quang phía sau */}
      <ellipse cx="50" cy="50" rx="46" ry="50" fill="url(#uelGlow)" />

      {/* quai trái */}
      <path
        d="M30 26 C12 24 10 50 28 58 L31 50 C20 46 22 34 32 36 Z"
        fill="url(#uelGold)"
        stroke="#7a5c18"
        strokeWidth="1.1"
      />
      {/* quai phải */}
      <path
        d="M70 26 C88 24 90 50 72 58 L69 50 C80 46 78 34 68 36 Z"
        fill="url(#uelGold)"
        stroke="#7a5c18"
        strokeWidth="1.1"
      />

      {/* miệng cúp loe */}
      <path
        d="M30 12 C30 9 33 8 36 8 L64 8 C67 8 70 9 70 12 L66 22 L34 22 Z"
        fill="url(#uelGold)"
        stroke="#7a5c18"
        strokeWidth="1.3"
      />

      {/* thân amphora thon */}
      <path
        d="M34 22 L66 22 C66 40 62 58 58 70 C56 78 53 84 50 86
           C47 84 44 78 42 70 C38 58 34 40 34 22 Z"
        fill="url(#uelGold)"
        stroke="#7a5c18"
        strokeWidth="1.3"
      />
      {/* gân dọc thân */}
      <path d="M44 24 C43 44 45 64 49 82" stroke="#9c7421" strokeWidth="1" fill="none" opacity="0.7" />
      <path d="M50 24 L50 84" stroke="#9c7421" strokeWidth="1" fill="none" opacity="0.7" />
      <path d="M56 24 C57 44 55 64 51 82" stroke="#9c7421" strokeWidth="1" fill="none" opacity="0.7" />
      {/* highlight bóng */}
      <path d="M38 24 C38 46 42 66 49 80 C42 66 38 46 38 24 Z" fill="url(#uelShine)" />

      {/* cổ + đế bát giác */}
      <rect x="45" y="86" width="10" height="10" rx="1.5" fill="url(#uelGold)" stroke="#7a5c18" strokeWidth="1.1" />
      <path d="M36 96 L64 96 L60 106 L40 106 Z" fill="url(#uelGold)" stroke="#7a5c18" strokeWidth="1.1" />
      {/* đế bát giác đen */}
      <path d="M34 106 L66 106 L70 113 L66 120 L34 120 L30 113 Z" fill="#1f2630" stroke="#0c1118" strokeWidth="1" />
      <rect x="28" y="120" width="44" height="8" rx="2" fill="#2b333f" stroke="#0c1118" strokeWidth="1" />
    </svg>
  );
}
