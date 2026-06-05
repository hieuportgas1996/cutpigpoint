// Map slug CLB → URL logo (bundled bởi Vite từ src/img/club/<slug>.png).
// Slug khớp MemoryGameEngine.Clubs ở server.
const modules = import.meta.glob('../img/club/*.png', { eager: true, import: 'default' }) as Record<string, string>;

export const CLUB_LOGO: Record<string, string> = {};
for (const [path, url] of Object.entries(modules)) {
  const slug = path.split('/').pop()!.replace('.png', '');
  CLUB_LOGO[slug] = url;
}

// Tên hiển thị CLB (mirror server MemoryGameEngine.Clubs).
export const CLUB_NAME: Record<string, string> = {
  ajax: 'Ajax',
  alt: 'Atlético Madrid',
  arsenal: 'Arsenal',
  aston: 'Aston Villa',
  barca: 'Barcelona',
  bayern: 'Bayern Munich',
  bour: 'Bournemouth',
  brigton: 'Brighton',
  chelsea: 'Chelsea',
  dortmund: 'Dortmund',
  liv: 'Liverpool',
  mc: 'Man City',
  mu: 'Man United',
  new: 'Newcastle',
  psg: 'PSG',
  real: 'Real Madrid',
  tot: 'Tottenham',
  westham: 'West Ham',
};

export function clubName(slug: string): string {
  return CLUB_NAME[slug] ?? slug;
}
