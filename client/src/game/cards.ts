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
