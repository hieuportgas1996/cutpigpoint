// Cúp C1 ("Big Ears" UCL trophy) silhouette vẽ inline SVG — không dùng logo bản quyền,
// chỉ tái hiện dáng cúp đặc trưng: thân tròn, 2 quai tai to cong, đế đen.
export function ChampionTrophy({ size = 120 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size * 1.4}
      viewBox="0 0 100 140"
      fill="none"
      aria-hidden="true"
      className="champion-trophy-svg"
    >
      <defs>
        <linearGradient id="cupSilver" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#f6f8fb" />
          <stop offset="35%" stopColor="#d7dde6" />
          <stop offset="70%" stopColor="#aab4c2" />
          <stop offset="100%" stopColor="#828d9e" />
        </linearGradient>
        <linearGradient id="cupShine" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#ffffff" stopOpacity="0.9" />
          <stop offset="55%" stopColor="#ffffff" stopOpacity="0" />
        </linearGradient>
        <radialGradient id="cupGlow" cx="50%" cy="40%" r="60%">
          <stop offset="0%" stopColor="#fff3c4" stopOpacity="0.85" />
          <stop offset="100%" stopColor="#fff3c4" stopOpacity="0" />
        </radialGradient>
      </defs>

      {/* hào quang phía sau */}
      <ellipse cx="50" cy="52" rx="48" ry="50" fill="url(#cupGlow)" />

      {/* quai tai trái */}
      <path
        d="M28 30 C2 26 -2 64 26 80 L30 70 C14 60 16 40 30 42 Z"
        fill="url(#cupSilver)"
        stroke="#6b7585"
        strokeWidth="1.2"
      />
      {/* quai tai phải */}
      <path
        d="M72 30 C98 26 102 64 74 80 L70 70 C86 60 84 40 70 42 Z"
        fill="url(#cupSilver)"
        stroke="#6b7585"
        strokeWidth="1.2"
      />

      {/* thân cúp */}
      <path
        d="M24 16 C24 12 26 10 30 10 L70 10 C74 10 76 12 76 16
           C76 50 70 78 50 86 C30 78 24 50 24 16 Z"
        fill="url(#cupSilver)"
        stroke="#6b7585"
        strokeWidth="1.4"
      />
      {/* highlight bóng trên thân */}
      <path
        d="M34 16 C34 46 38 70 49 80 C40 70 34 48 34 16 Z"
        fill="url(#cupShine)"
      />

      {/* cổ + đế */}
      <rect x="44" y="84" width="12" height="12" rx="2" fill="url(#cupSilver)" stroke="#6b7585" strokeWidth="1.2" />
      <path d="M34 96 L66 96 L62 108 L38 108 Z" fill="url(#cupSilver)" stroke="#6b7585" strokeWidth="1.2" />
      {/* đế đen */}
      <rect x="30" y="108" width="40" height="10" rx="2" fill="#1f2630" stroke="#0c1118" strokeWidth="1" />
      <rect x="26" y="118" width="48" height="9" rx="2.5" fill="#2b333f" stroke="#0c1118" strokeWidth="1" />

      {/* ngôi sao nhỏ trang trí */}
      <text x="50" y="48" fontSize="20" textAnchor="middle" fill="#ffd76a" stroke="#c79a2e" strokeWidth="0.5">★</text>
    </svg>
  );
}
