type IconName =
  | 'plus'
  | 'edit'
  | 'trash'
  | 'check'
  | 'alert'
  | 'info'
  | 'play'
  | 'flag'
  | 'trophy'
  | 'cards'
  | 'users'
  | 'arrow-right'
  | 'pig'
  | 'star'
  | 'clock'
  | 'chevron-right';

export function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const props = {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 2,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true
  };
  switch (name) {
    case 'plus':
      return <svg {...props}><path d="M12 5v14M5 12h14" /></svg>;
    case 'edit':
      return <svg {...props}><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" /><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" /></svg>;
    case 'trash':
      return <svg {...props}><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6h14z" /></svg>;
    case 'check':
      return <svg {...props}><path d="M20 6L9 17l-5-5" /></svg>;
    case 'alert':
      return <svg {...props}><circle cx="12" cy="12" r="10" /><path d="M12 8v4M12 16h.01" /></svg>;
    case 'info':
      return <svg {...props}><circle cx="12" cy="12" r="10" /><path d="M12 16v-4M12 8h.01" /></svg>;
    case 'play':
      return <svg {...props}><polygon points="5 3 19 12 5 21 5 3" fill="currentColor" stroke="none" /></svg>;
    case 'flag':
      return <svg {...props}><path d="M4 22V4M4 14h11l-2 4h7V6h-9l-2-2H4" /></svg>;
    case 'trophy':
      return <svg {...props}><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4z" /><path d="M17 4h3v3a3 3 0 0 1-3 3M7 4H4v3a3 3 0 0 0 3 3" /></svg>;
    case 'cards':
      return <svg {...props}><rect x="3" y="6" width="13" height="15" rx="2" /><path d="M8 3h11a2 2 0 0 1 2 2v13" /></svg>;
    case 'users':
      return <svg {...props}><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg>;
    case 'arrow-right':
      return <svg {...props}><path d="M5 12h14M12 5l7 7-7 7" /></svg>;
    case 'chevron-right':
      return <svg {...props}><path d="M9 18l6-6-6-6" /></svg>;
    case 'pig':
      return <svg {...props}><path d="M19 11c0-3-2.5-5-6-5h-1L9 3l-1 4c-3 .5-5 2.5-5 5 0 1.5.7 2.8 2 3.7V19h3v-1.3c.6.2 1.3.3 2 .3h3c.7 0 1.4-.1 2-.3V19h3v-3.3c1.3-.9 2-2.2 2-3.7z" /><circle cx="15.5" cy="11" r=".7" fill="currentColor" /></svg>;
    case 'star':
      return <svg {...props}><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" /></svg>;
    case 'clock':
      return <svg {...props}><circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" /></svg>;
  }
}
