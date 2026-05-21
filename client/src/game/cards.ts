export type Suit = 'spades' | 'hearts' | 'diamonds' | 'clubs';
export type Rank = 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15;

export interface Card {
  id: string;
  rank: Rank;
  suit: Suit;
}

export const SUIT_GLYPH: Record<Suit, string> = {
  spades: '♠',
  hearts: '♥',
  diamonds: '♦',
  clubs: '♣',
};

export const SUIT_COLOR: Record<Suit, 'red' | 'black'> = {
  spades: 'black',
  clubs: 'black',
  hearts: 'red',
  diamonds: 'red',
};

const RANK_LABEL: Record<Rank, string> = {
  3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A', 15: '2',
};

export const rankLabel = (r: Rank) => RANK_LABEL[r];

const SUIT_ORDER: Record<Suit, number> = { spades: 0, clubs: 1, diamonds: 2, hearts: 3 };

export function compareCard(a: Card, b: Card): number {
  if (a.rank !== b.rank) return a.rank - b.rank;
  return SUIT_ORDER[a.suit] - SUIT_ORDER[b.suit];
}

export function buildDeck(): Card[] {
  const ranks: Rank[] = [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
  const suits: Suit[] = ['spades', 'clubs', 'diamonds', 'hearts'];
  const deck: Card[] = [];
  for (const r of ranks) {
    for (const s of suits) {
      deck.push({ id: `${r}-${s}`, rank: r, suit: s });
    }
  }
  return deck;
}

export function shuffle<T>(arr: T[], seed = Date.now()): T[] {
  const out = arr.slice();
  let s = seed >>> 0;
  const rand = () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 0x100000000;
  };
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    [out[i], out[j]] = [out[j], out[i]];
  }
  return out;
}

export function dealFour(seed = Date.now()): Card[][] {
  const deck = shuffle(buildDeck(), seed);
  const hands: Card[][] = [[], [], [], []];
  for (let i = 0; i < 52; i++) hands[i % 4].push(deck[i]);
  return hands.map(h => h.sort(compareCard));
}

export type ComboKind = 'single' | 'pair' | 'triple' | 'four' | 'run' | 'runOfPairs';

export interface ComboInfo {
  kind: ComboKind;
  cards: Card[];
  topValue: number;
}

const SUIT_INDEX: Record<Suit, number> = { spades: 0, clubs: 1, diamonds: 2, hearts: 3 };

export function cardValue(c: Card): number {
  return c.rank * 4 + SUIT_INDEX[c.suit];
}

export function detectCombo(cards: Card[]): ComboInfo | null {
  if (cards.length === 0) return null;
  const sorted = [...cards].sort(compareCard);

  if (sorted.length === 1)
    return { kind: 'single', cards: sorted, topValue: cardValue(sorted[0]) };

  const allSame = sorted.every(c => c.rank === sorted[0].rank);
  if (allSame) {
    if (sorted.length === 2) return { kind: 'pair', cards: sorted, topValue: cardValue(sorted[sorted.length - 1]) };
    if (sorted.length === 3) return { kind: 'triple', cards: sorted, topValue: cardValue(sorted[sorted.length - 1]) };
    if (sorted.length === 4) return { kind: 'four', cards: sorted, topValue: cardValue(sorted[sorted.length - 1]) };
    return null;
  }

  if (isRun(sorted)) return { kind: 'run', cards: sorted, topValue: cardValue(sorted[sorted.length - 1]) };
  if (isRunOfPairs(sorted)) return { kind: 'runOfPairs', cards: sorted, topValue: cardValue(sorted[sorted.length - 1]) };
  return null;
}

function isRun(sorted: Card[]): boolean {
  if (sorted.length < 3) return false;
  if (sorted.some(c => c.rank === 15)) return false;
  for (let i = 1; i < sorted.length; i++) {
    if (sorted[i].rank !== sorted[i - 1].rank + 1) return false;
  }
  return true;
}

function isRunOfPairs(sorted: Card[]): boolean {
  if (sorted.length < 6 || sorted.length % 2 !== 0) return false;
  if (sorted.some(c => c.rank === 15)) return false;
  const groups = new Map<number, Card[]>();
  for (const c of sorted) {
    const arr = groups.get(c.rank) ?? [];
    arr.push(c);
    groups.set(c.rank, arr);
  }
  if (groups.size * 2 !== sorted.length) return false;
  const ranks = [...groups.keys()].sort((a, b) => a - b);
  for (let i = 0; i < ranks.length; i++) {
    if (groups.get(ranks[i])!.length !== 2) return false;
    if (i > 0 && ranks[i] !== ranks[i - 1] + 1) return false;
  }
  return true;
}

export function comboBeats(current: ComboInfo, next: ComboInfo): boolean {
  // 4-pair-run beats everything
  if (next.kind === 'runOfPairs' && next.cards.length === 8) return true;

  // Tứ quý beats: 1 con 2, đôi 2, 3 đôi thông
  if (next.kind === 'four') {
    if (current.kind === 'single' && current.cards[0].rank === 15) return true;
    if (current.kind === 'pair' && current.cards[0].rank === 15) return true;
    if (current.kind === 'runOfPairs' && current.cards.length === 6) return true;
  }

  // 3 đôi thông beats: 1 con 2
  if (next.kind === 'runOfPairs' && next.cards.length === 6) {
    if (current.kind === 'single' && current.cards[0].rank === 15) return true;
  }

  // Same kind + length + higher top value
  return next.kind === current.kind && next.cards.length === current.cards.length && next.topValue > current.topValue;
}

export function isFourPairRun(combo: ComboInfo): boolean {
  return combo.kind === 'runOfPairs' && combo.cards.length === 8;
}

export function cardFromDto(d: { rank: number; suit: number }): Card {
  const suits: Suit[] = ['spades', 'clubs', 'diamonds', 'hearts'];
  return { id: `${d.rank}-${suits[d.suit]}`, rank: d.rank as Rank, suit: suits[d.suit] };
}

export function cardToDto(c: Card): { rank: number; suit: number } {
  return { rank: c.rank, suit: SUIT_INDEX[c.suit] };
}
