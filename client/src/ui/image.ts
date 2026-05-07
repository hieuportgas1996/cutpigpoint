const TARGET_SIZE = 256;
const QUALITY = 0.82;

export async function fileToAvatarDataUrl(file: File): Promise<string> {
  if (!file.type.startsWith('image/')) {
    throw new Error('File phải là ảnh');
  }
  if (file.size > 10 * 1024 * 1024) {
    throw new Error('Ảnh quá lớn (tối đa 10MB)');
  }

  const bitmap = await loadImage(file);
  const canvas = document.createElement('canvas');
  canvas.width = TARGET_SIZE;
  canvas.height = TARGET_SIZE;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Không tạo được canvas');

  const { sx, sy, sSize } = centerCropSquare(bitmap.width, bitmap.height);
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(bitmap, sx, sy, sSize, sSize, 0, 0, TARGET_SIZE, TARGET_SIZE);

  return canvas.toDataURL('image/jpeg', QUALITY);
}

async function loadImage(file: File): Promise<HTMLImageElement | ImageBitmap> {
  if (typeof createImageBitmap === 'function') {
    try {
      return await createImageBitmap(file);
    } catch {
      // fall through to <img>
    }
  }
  const url = URL.createObjectURL(file);
  try {
    return await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('Không đọc được ảnh'));
      img.src = url;
    });
  } finally {
    URL.revokeObjectURL(url);
  }
}

function centerCropSquare(w: number, h: number) {
  const sSize = Math.min(w, h);
  const sx = Math.round((w - sSize) / 2);
  const sy = Math.round((h - sSize) / 2);
  return { sx, sy, sSize };
}
