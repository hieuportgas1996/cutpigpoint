// Sound effect registry + helpers. Vite resolves these imports to hashed URLs in the final bundle.
import backgroundLobbyUrl from './background_lobby.mp3';
import notifyTurnUrl from './notify_turn.mp3';
import clapHandUrl from './clap-hand.mp3';
import sosUrl from './sos.mp3';
import siuuUrl from './siuu.mp3';
import beggingUrl from './begging.mp3';
import uhhhhUrl from './uhhhh.mp3';
import ahhUrl from './ahh.mp3';
import sorryUrl from './sorry.mp3';
import countdownUrl from './countdown.mp3';
import ronaldoSiuuuuUrl from './ronaldo-siuuuu.mp3';
import chiuroiUrl from './chiuroi.mp3';
import lotteryUrl from './lottery.mp3';
import fireworkNewUrl from './firework-new.mp3';
import soQuaUrl from './so-qua.mp3';
import niceSoundUrl from './nice-sound.mp3';
import saoMaDoUrl from './sao-ma-do.mp3';
import quenChaNaUrl from './quen-cha-na.mp3';
import chatChetMeUrl from './chat-chet-me.mp3';
import khongGietUrl from './khong-giet.mp3';
import boDiNhoUrl from './bo-di-nho.mp3';
import muVoDichUrl from './mu-vo-dich.mp3';
import dcmm1Url from './dcmm1.mp3';
import siuuuuuUrl from './siuuuuu.mp3';
import lozBaoUrl from './loz-bao.mp3';
import lozThienUrl from './loz-thien.mp3';
import lozDuyUrl from './loz-duy.mp3';
import danDoUrl from './dan-do.mp3';
import lozHieuUrl from './loz-hieu.mp3';

export const SOUND_URLS = {
  backgroundLobby: backgroundLobbyUrl,
  notifyTurn: notifyTurnUrl,
  clapHand: clapHandUrl,
  sos: sosUrl,
  siuu: siuuUrl,
  begging: beggingUrl,
  uhhhh: uhhhhUrl,
  ahh: ahhUrl,
  sorry: sorryUrl,
  countdown: countdownUrl,
  ronaldoSiuuuu: ronaldoSiuuuuUrl,
  chiuroi: chiuroiUrl,
  lottery: lotteryUrl,
  fireworkNew: fireworkNewUrl,
  soQua: soQuaUrl,
  niceSound: niceSoundUrl,
  saoMaDo: saoMaDoUrl,
  quenChaNa: quenChaNaUrl,
  chatChetMe: chatChetMeUrl,
  khongGiet: khongGietUrl,
  boDiNho: boDiNhoUrl,
  muVoDich: muVoDichUrl,
  dcmm: dcmm1Url,
  siuiii: siuuuuuUrl,
  lozBao: lozBaoUrl,
  lozThien: lozThienUrl,
  lozDuy: lozDuyUrl,
  danDo: danDoUrl,
  lozHieu: lozHieuUrl,
} as const;

export type SoundKey = keyof typeof SOUND_URLS;

/** Sounds that should start at a non-zero offset (seconds) instead of position 0. */
const SOUND_START_OFFSET: Partial<Record<SoundKey, number>> = {
  ronaldoSiuuuu: 4,
};

/** Sounds that should stop after a max duration (seconds) instead of playing to the end. */
const SOUND_MAX_DURATION: Partial<Record<SoundKey, number>> = {
  quenChaNa: 7,
};

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

// Pending max-duration cutoff timers keyed by sound, so re-plays don't stack timers.
const cutoffTimers = new Map<SoundKey, ReturnType<typeof setTimeout>>();

/** Play a one-shot sound. Restarts from the configured start offset if it's already playing. */
export function playSound(key: SoundKey, volume = 1) {
  try {
    const el = get(key);
    el.loop = false;
    el.volume = volume;
    el.currentTime = SOUND_START_OFFSET[key] ?? 0;
    void el.play().catch(() => undefined); // ignore autoplay-blocked rejections

    const existing = cutoffTimers.get(key);
    if (existing) { clearTimeout(existing); cutoffTimers.delete(key); }
    const maxDuration = SOUND_MAX_DURATION[key];
    if (maxDuration != null) {
      const timer = setTimeout(() => {
        el.pause();
        el.currentTime = 0;
        cutoffTimers.delete(key);
      }, maxDuration * 1000);
      cutoffTimers.set(key, timer);
    }
  } catch {
    /* no-op */
  }
}

/** Stop a sound if it's currently playing (and rewind to start). No-op if never played. */
export function stopSound(key: SoundKey) {
  const el = cache.get(key);
  if (!el) return;
  el.pause();
  el.currentTime = 0;
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
