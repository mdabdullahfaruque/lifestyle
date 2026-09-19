/** Only the header is read; EXIF lives at the front of the file. */
const HEADER_BYTES = 128 * 1024;

const JPEG_SOI = 0xffd8;
const APP1 = 0xffe1;
const EXIF_HEADER = 0x45786966; // "Exif"
const TAG_DATE_TIME_ORIGINAL = 0x9003;
const TAG_DATE_TIME_DIGITIZED = 0x9004;
const TAG_EXIF_IFD_POINTER = 0x8769;

/**
 * Reads EXIF `DateTimeOriginal` out of a JPEG, as an ISO-8601 UTC string.
 *
 * This exists because {@link downscaleImage} has to destroy it. Re-encoding through a canvas
 * drops all metadata — which is right for GPS, and fatal for bulk import: capture time is how
 * unnamed phone photos are grouped into shoots (docs/08 §4.3), and the photos that need
 * downscaling are precisely the large ones straight off a camera. Read it before the re-encode
 * and send it alongside the file, and the grouping survives without the coordinates coming with
 * it.
 *
 * Deliberately minimal: one tag, JPEG only, no dependency. Returns null for anything it does not
 * understand, and the server falls back to the file's own EXIF (which a ZIP upload still has).
 */
export async function readExifCaptureTime(file: File): Promise<string | null> {
  try {
    const buffer = await file.slice(0, HEADER_BYTES).arrayBuffer();
    const view = new DataView(buffer);

    if (view.byteLength < 4 || view.getUint16(0) !== JPEG_SOI) return null;

    let offset = 2;

    // Walk the JPEG marker segments looking for APP1.
    while (offset + 4 <= view.byteLength) {
      const marker = view.getUint16(offset);
      const length = view.getUint16(offset + 2);

      if (marker === APP1) {
        const start = offset + 4;
        if (start + 6 > view.byteLength || view.getUint32(start) !== EXIF_HEADER) return null;

        return readFromTiff(view, start + 6);
      }

      // Not a segment we understand the length of; stop rather than guess.
      if ((marker & 0xff00) !== 0xff00 || length < 2) return null;
      offset += 2 + length;
    }

    return null;
  } catch {
    return null;
  }
}

/** The TIFF block inside APP1: a header, then IFD0, which points at the EXIF sub-IFD. */
function readFromTiff(view: DataView, tiffStart: number): string | null {
  if (tiffStart + 8 > view.byteLength) return null;

  const endian = view.getUint16(tiffStart);
  if (endian !== 0x4949 && endian !== 0x4d4d) return null;

  const little = endian === 0x4949;
  const ifd0 = tiffStart + view.getUint32(tiffStart + 4, little);

  const direct = readDateFrom(view, ifd0, tiffStart, little);
  if (direct) return direct;

  // Cameras normally put DateTimeOriginal in the EXIF sub-IFD, not IFD0.
  const pointer = findTag(view, ifd0, tiffStart, little, TAG_EXIF_IFD_POINTER);
  if (pointer === null) return null;

  return readDateFrom(view, tiffStart + pointer, tiffStart, little);
}

function readDateFrom(
  view: DataView,
  ifdStart: number,
  tiffStart: number,
  little: boolean,
): string | null {
  for (const tag of [TAG_DATE_TIME_ORIGINAL, TAG_DATE_TIME_DIGITIZED]) {
    const valueOffset = findTag(view, ifdStart, tiffStart, little, tag);
    if (valueOffset === null) continue;

    const at = tiffStart + valueOffset;
    if (at + 19 > view.byteLength) continue;

    let text = '';
    for (let i = 0; i < 19; i++) text += String.fromCharCode(view.getUint8(at + i));

    const parsed = parseExifDate(text);
    if (parsed) return parsed;
  }

  return null;
}

/** Returns the tag's value offset (relative to the TIFF start), or null. */
function findTag(
  view: DataView,
  ifdStart: number,
  tiffStart: number,
  little: boolean,
  wanted: number,
): number | null {
  if (ifdStart + 2 > view.byteLength) return null;

  const count = view.getUint16(ifdStart, little);

  for (let i = 0; i < count; i++) {
    const entry = ifdStart + 2 + i * 12;
    if (entry + 12 > view.byteLength) return null;

    if (view.getUint16(entry, little) === wanted) {
      return view.getUint32(entry + 8, little);
    }
  }

  return null;
}

/**
 * EXIF writes "yyyy:MM:dd HH:mm:ss" with no zone. Treated as UTC, matching the server: the value
 * is only ever compared with other photos from the same shoot to find the gaps between them, so a
 * consistent offset is all that matters and guessing the phone's zone would be worse.
 */
function parseExifDate(text: string): string | null {
  const match = /^(\d{4}):(\d{2}):(\d{2}) (\d{2}):(\d{2}):(\d{2})$/.exec(text);
  if (!match) return null;

  const [, year, month, day, hour, minute, second] = match;
  const date = new Date(
    Date.UTC(+year, +month - 1, +day, +hour, +minute, +second),
  );

  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
