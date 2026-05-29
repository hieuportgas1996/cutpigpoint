// Sound effect registry + helpers. Vite resolves these imports to hashed URLs in the final bundle.
import backgroundLobbyUrl from './background_lobby.mp3';
import notifyTurnUrl from './notify_turn.mp3';
import victoryUrl from './victory.mp3';
import sosUrl from './sos.mp3';
import siuuUrl from './siuu.mp3';
import beggingUrl from './begging.mp3';
import uhhhhUrl from './uhhhh.mp3';
import ahhUrl from './ahh.mp3';

export const SOUND_URLS = {
  backgroundLobby: backgroundLobbyUrl,
  notifyTurn: notifyTurnUrl,
  victory: victoryUrl,
  sos: sosUrl,
  siuu: siuuUrl,
  begging: beggingUrl,
  uhhhh: uhhhhUrl,
  ahh: ahhUrl,
} as const;

export type SoundKey = keyof typeof SOUND_URLS;

// Cache one HTMLAudioElement per key so we don't re-decode the file on every play.
const cache = new Map<SoundKey, HTMLAudioElement>();

function get(key: SoundKey): HTMLAudioElement {
  let el = cache.get(key);
  if (!el) {
    el = new Audio(SOUND_URLS[key]);
    el.preload = 'auto';
    cache.set(key, el);
  }
  return el;
}

/** Play a one-shot sound. Restarts from the beginning if it's already playing. */
export function playSound(key: SoundKey, volume = 1) {
  try {
    const el = get(key);
    el.loop = false;
    el.volume = volume;
    el.currentTime = 0;
    void el.play().catch(() => undefined); // ignore autoplay-blocked rejections
  } catch {
    /* no-op */
  }
}

/** Start (or restart) a looping sound. Returns a stop function. */
export function playLoop(key: SoundKey, volume = 0.5): () => void {
  const el = get(key);
  el.loop = true;
  el.volume = volume;
  el.currentTime = 0;
  void el.play().catch(() => undefined);
  return () => {
    el.pause();
    el.currentTime = 0;
  };
}
