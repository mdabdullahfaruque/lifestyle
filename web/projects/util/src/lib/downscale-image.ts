/**
 * Longest edge we send to the server. Larger than the biggest derivative the API generates
 * (`large`, 1600px), so nothing visible is lost by shrinking here first.
 */
const MAX_EDGE = 2000;

/** JPEG quality for the re-encode. High enough that a product photo is indistinguishable. */
const QUALITY = 0.86;

/** Below this, a re-encode costs quality and saves nothing worth having. */
const SKIP_BELOW_BYTES = 1.5 * 1024 * 1024;

/**
 * Never touched. A PDF is a KYC document, not a photo; SVG is vector and would be rasterised; a
 * GIF would lose its animation.
 */
const PASSTHROUGH_TYPES = ['application/pdf', 'image/svg+xml', 'image/gif'];

/**
 * Shrinks a photo in the browser before it is uploaded.
 *
 * Two problems, one fix. The API caps uploads at 10 MB and a modern phone JPEG is 8–12 MB, so
 * sellers hit a wall on the first file. And a default-settings iPhone shoots **HEIC**, which the
 * API does not accept at all — but the browser can decode what the server cannot, so drawing the
 * image to a canvas and re-encoding converts it as a side effect.
 *
 * It also matters for what it costs the seller: a 400-photo upload over a Bangladeshi mobile
 * connection goes from gigabytes to a few hundred megabytes.
 *
 * Failure is never fatal. If the browser cannot decode the file — Chrome on Android cannot read
 * HEIC — the original is returned unchanged and the server rejects it with a message the seller
 * can act on, which is better than the upload silently doing nothing.
 */
export async function downscaleImage(file: File): Promise<File> {
  if (PASSTHROUGH_TYPES.includes(file.type)) return file;

  // A small JPEG or PNG is already fine; re-encoding would only degrade it.
  const looksSmall = file.size < SKIP_BELOW_BYTES;
  const isHeic = /hei[cf]/i.test(file.type) || /\.hei[cf]$/i.test(file.name);

  if (looksSmall && !isHeic) return file;

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    return file;
  }

  try {
    const scale = Math.min(1, MAX_EDGE / Math.max(bitmap.width, bitmap.height));

    // An HEIC at any size still has to be re-encoded, because the server cannot read the format.
    if (scale === 1 && !isHeic) return file;

    const width = Math.round(bitmap.width * scale);
    const height = Math.round(bitmap.height * scale);

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext('2d');
    if (!context) return file;

    context.drawImage(bitmap, 0, 0, width, height);

    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, 'image/jpeg', QUALITY),
    );

    if (!blob) return file;

    // Never hand back something bigger than what we were given.
    if (blob.size >= file.size && !isHeic) return file;

    return new File([blob], renameToJpeg(file.name), {
      type: 'image/jpeg',
      lastModified: file.lastModified,
    });
  } finally {
    bitmap.close();
  }
}

/**
 * The name is what the bulk-import matcher reads, so only the extension may change — the stem
 * carries the product code and must survive intact.
 */
function renameToJpeg(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot > 0 ? `${fileName.slice(0, dot)}.jpg` : `${fileName}.jpg`;
}
