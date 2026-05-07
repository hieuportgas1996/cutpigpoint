export function initials(name: string): string {
  if (!name) return '?';
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

export function formatScore(n: number): string {
  if (n > 0) return `+${n}`;
  return String(n);
}

export function scoreClass(n: number): string {
  if (n > 0) return 'pos';
  if (n < 0) return 'neg';
  return '';
}

export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

export function relativeTime(iso: string): string {
  const d = new Date(iso).getTime();
  const diffSec = Math.round((Date.now() - d) / 1000);
  if (diffSec < 60) return 'vừa xong';
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)} phút trước`;
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)} giờ trước`;
  return `${Math.floor(diffSec / 86400)} ngày trước`;
}
