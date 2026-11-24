export interface ProjectArchiveEntry {
  path: string;
  source: Blob | string;
  lastModified?: number;
}

const crcTable = createCrcTable();
const encoder = new TextEncoder();

export async function createProjectZip(entries: ReadonlyArray<ProjectArchiveEntry>): Promise<Blob> {
  if (entries.length > 0xffff) throw new Error('A ZIP download can contain at most 65,535 files.');

  const localParts: BlobPart[] = [];
  const centralParts: BlobPart[] = [];
  let offset = 0;
  let entryCount = 0;

  for (const entry of entries) {
    const path = normalizeArchivePath(entry.path);
    if (!path) continue;

    const nameBytes = encoder.encode(path);
    const data = typeof entry.source === 'string'
      ? encoder.encode(entry.source)
      : new Uint8Array(await entry.source.arrayBuffer());
    if (data.byteLength > 0xffffffff) throw new Error(`File is too large for the browser ZIP download format: ${path}`);
    if (offset > 0xffffffff) throw new Error('The selected project is too large for the browser ZIP download format.');
    const crc = crc32(data);
    const { date, time } = toDosDateTime(entry.lastModified);

    const localHeader = new Uint8Array(30 + nameBytes.length);
    const localView = new DataView(localHeader.buffer);
    localView.setUint32(0, 0x04034b50, true);
    localView.setUint16(4, 20, true);
    localView.setUint16(6, 0x0800, true);
    localView.setUint16(8, 0, true);
    localView.setUint16(10, time, true);
    localView.setUint16(12, date, true);
    localView.setUint32(14, crc, true);
    localView.setUint32(18, data.byteLength, true);
    localView.setUint32(22, data.byteLength, true);
    localView.setUint16(26, nameBytes.length, true);
    localView.setUint16(28, 0, true);
    localHeader.set(nameBytes, 30);

    const centralHeader = new Uint8Array(46 + nameBytes.length);
    const centralView = new DataView(centralHeader.buffer);
    centralView.setUint32(0, 0x02014b50, true);
    centralView.setUint16(4, 20, true);
    centralView.setUint16(6, 20, true);
    centralView.setUint16(8, 0x0800, true);
    centralView.setUint16(10, 0, true);
    centralView.setUint16(12, time, true);
    centralView.setUint16(14, date, true);
    centralView.setUint32(16, crc, true);
    centralView.setUint32(20, data.byteLength, true);
    centralView.setUint32(24, data.byteLength, true);
    centralView.setUint16(28, nameBytes.length, true);
    centralView.setUint16(30, 0, true);
    centralView.setUint16(32, 0, true);
    centralView.setUint16(34, 0, true);
    centralView.setUint16(36, 0, true);
    centralView.setUint32(38, 0, true);
    centralView.setUint32(42, offset, true);
    centralHeader.set(nameBytes, 46);

    localParts.push(localHeader, data);
    centralParts.push(centralHeader);
    offset += localHeader.byteLength + data.byteLength;
    entryCount++;
  }

  const centralOffset = offset;
  const centralSize = centralParts.reduce((sum, part) => sum + blobPartSize(part), 0);
  if (centralOffset > 0xffffffff || centralSize > 0xffffffff) throw new Error('The selected project is too large for the browser ZIP download format.');

  const end = new Uint8Array(22);
  const endView = new DataView(end.buffer);
  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(4, 0, true);
  endView.setUint16(6, 0, true);
  endView.setUint16(8, entryCount, true);
  endView.setUint16(10, entryCount, true);
  endView.setUint32(12, centralSize, true);
  endView.setUint32(16, centralOffset, true);
  endView.setUint16(20, 0, true);

  return new Blob([...localParts, ...centralParts, end], { type: 'application/zip' });
}

export function normalizeArchivePath(value: string): string {
  const segments = value
    .replaceAll('\\', '/')
    .split('/')
    .filter(segment => segment.length > 0 && segment !== '.')
    .map(segment => segment === '..' ? '_' : segment);
  return segments.join('/');
}

function createCrcTable(): Uint32Array {
  const table = new Uint32Array(256);
  for (let index = 0; index < table.length; index++) {
    let value = index;
    for (let bit = 0; bit < 8; bit++) value = (value & 1) !== 0 ? (0xedb88320 ^ (value >>> 1)) : (value >>> 1);
    table[index] = value >>> 0;
  }
  return table;
}

function crc32(bytes: Uint8Array): number {
  let value = 0xffffffff;
  for (const byte of bytes) value = crcTable[(value ^ byte) & 0xff] ^ (value >>> 8);
  return (value ^ 0xffffffff) >>> 0;
}

function toDosDateTime(lastModified?: number): { date: number; time: number } {
  const value = new Date(lastModified && Number.isFinite(lastModified) ? lastModified : Date.now());
  const year = Math.min(2107, Math.max(1980, value.getFullYear()));
  const date = ((year - 1980) << 9) | ((value.getMonth() + 1) << 5) | value.getDate();
  const time = (value.getHours() << 11) | (value.getMinutes() << 5) | Math.floor(value.getSeconds() / 2);
  return { date, time };
}

function blobPartSize(part: BlobPart): number {
  if (typeof part === 'string') return encoder.encode(part).byteLength;
  if (part instanceof Blob) return part.size;
  if (part instanceof ArrayBuffer) return part.byteLength;
  return part.byteLength;
}
