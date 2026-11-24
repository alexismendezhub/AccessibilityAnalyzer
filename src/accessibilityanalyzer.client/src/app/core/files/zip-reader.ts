export interface ZipProjectFile {
  path: string;
  file: File;
}

const endOfCentralDirectory = 0x06054b50;
const zip64EndLocator = 0x07064b50;
const zip64EndRecord = 0x06064b50;
const centralDirectoryHeader = 0x02014b50;
const localFileHeader = 0x04034b50;
const utf8 = new TextDecoder('utf-8');

// Reads a project ZIP entirely in the browser; nothing is uploaded. Supports stored and deflated entries, including ZIP64.
export async function readProjectZip(archive: Blob): Promise<ZipProjectFile[]> {
  const buffer = await archive.arrayBuffer();
  const view = new DataView(buffer);
  const bytes = new Uint8Array(buffer);
  const directory = locateCentralDirectory(view);

  const files: ZipProjectFile[] = [];
  let cursor = directory.offset;
  for (let index = 0; index < directory.entries; index++) {
    if (cursor + 46 > view.byteLength || view.getUint32(cursor, true) !== centralDirectoryHeader) throw new Error('The ZIP central directory is damaged.');
    const flags = view.getUint16(cursor + 8, true);
    const method = view.getUint16(cursor + 10, true);
    const modifiedTime = view.getUint16(cursor + 12, true);
    const modifiedDate = view.getUint16(cursor + 14, true);
    let compressedSize = view.getUint32(cursor + 20, true);
    const nameLength = view.getUint16(cursor + 28, true);
    const extraLength = view.getUint16(cursor + 30, true);
    const commentLength = view.getUint16(cursor + 32, true);
    let localOffset = view.getUint32(cursor + 42, true);
    const name = utf8.decode(bytes.subarray(cursor + 46, cursor + 46 + nameLength)).replaceAll('\\', '/');

    if (compressedSize === 0xffffffff || localOffset === 0xffffffff) {
      const zip64 = readZip64Extra(view, cursor + 46 + nameLength, extraLength, view.getUint32(cursor + 24, true) === 0xffffffff, compressedSize === 0xffffffff, localOffset === 0xffffffff);
      compressedSize = zip64.compressedSize ?? compressedSize;
      localOffset = zip64.localOffset ?? localOffset;
    }
    cursor += 46 + nameLength + extraLength + commentLength;

    if (name.endsWith('/') || name.startsWith('__MACOSX/')) continue;
    if ((flags & 0x1) !== 0) throw new Error(`The ZIP is password protected (${name}). Extract it and choose the project folder instead.`);
    if (view.getUint32(localOffset, true) !== localFileHeader) throw new Error(`The ZIP entry ${name} is damaged.`);

    const dataStart = localOffset + 30 + view.getUint16(localOffset + 26, true) + view.getUint16(localOffset + 28, true);
    const data = new Uint8Array(buffer, dataStart, compressedSize);
    const content = await decompress(data, method, name);
    const fileName = name.split('/').at(-1) ?? name;
    files.push({ path: name, file: new File([content], fileName, { lastModified: fromDosDateTime(modifiedDate, modifiedTime) }) });
  }
  return files;
}

function locateCentralDirectory(view: DataView): { offset: number; entries: number } {
  const lowest = Math.max(0, view.byteLength - 22 - 0xffff);
  for (let position = view.byteLength - 22; position >= lowest; position--) {
    if (view.getUint32(position, true) !== endOfCentralDirectory) continue;
    const entries = view.getUint16(position + 10, true);
    const offset = view.getUint32(position + 16, true);
    if (entries !== 0xffff && offset !== 0xffffffff) return { offset, entries };

    const locator = position - 20;
    if (locator < 0 || view.getUint32(locator, true) !== zip64EndLocator) throw new Error('The ZIP64 directory could not be found.');
    const record = Number(view.getBigUint64(locator + 8, true));
    if (view.getUint32(record, true) !== zip64EndRecord) throw new Error('The ZIP64 directory is damaged.');
    return { entries: Number(view.getBigUint64(record + 32, true)), offset: Number(view.getBigUint64(record + 48, true)) };
  }
  throw new Error('The selected file is not a ZIP archive.');
}

function readZip64Extra(view: DataView, start: number, length: number, hasUncompressed: boolean, hasCompressed: boolean, hasOffset: boolean): { compressedSize?: number; localOffset?: number } {
  let cursor = start;
  while (cursor + 4 <= start + length) {
    const id = view.getUint16(cursor, true);
    const size = view.getUint16(cursor + 2, true);
    if (id === 0x0001) {
      let field = cursor + 4;
      const result: { compressedSize?: number; localOffset?: number } = {};
      if (hasUncompressed) field += 8;
      if (hasCompressed) { result.compressedSize = Number(view.getBigUint64(field, true)); field += 8; }
      if (hasOffset) result.localOffset = Number(view.getBigUint64(field, true));
      return result;
    }
    cursor += 4 + size;
  }
  return {};
}

async function decompress(data: Uint8Array<ArrayBuffer>, method: number, name: string): Promise<Blob> {
  if (method === 0) return new Blob([data]);
  if (method !== 8) throw new Error(`The ZIP entry ${name} uses an unsupported compression method (${method}).`);
  const stream = new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  return new Response(stream).blob();
}

function fromDosDateTime(date: number, time: number): number {
  const year = 1980 + (date >> 9);
  const month = ((date >> 5) & 0x0f) - 1;
  const day = date & 0x1f;
  return new Date(year, month, day, time >> 11, (time >> 5) & 0x3f, (time & 0x1f) * 2).getTime();
}
