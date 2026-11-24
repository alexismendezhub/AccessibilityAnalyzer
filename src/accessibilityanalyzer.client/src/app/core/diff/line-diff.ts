export interface DiffSegment { text: string; changed: boolean; }
export interface DiffCell { line: number; segments: DiffSegment[]; }
export type DiffRowKind = 'equal' | 'changed' | 'removed' | 'added';
export interface DiffRow { kind: DiffRowKind; before: DiffCell | null; after: DiffCell | null; }
export type DiffBlock = { kind: 'rows'; key: string; rows: DiffRow[] } | { kind: 'gap'; key: string; hidden: number };
export interface SideBySideDiff { rows: DiffRow[]; changedRows: number; removedLines: number; addedLines: number; }

type Op = { type: 'equal' | 'delete' | 'insert'; line: string };

// Above this many edits the files are treated as fully replaced; accessibility fixes touch only a few lines.
const maxEditDistance = 2000;

export function splitLines(text: string): string[] {
  return text.length === 0 ? [] : text.split(/\r\n|\n|\r/);
}

export function buildSideBySideDiff(before: string, after: string): SideBySideDiff {
  const ops = diffLines(splitLines(before), splitLines(after));
  const rows: DiffRow[] = [];
  let beforeLine = 0;
  let afterLine = 0;
  let removed: string[] = [];
  let added: string[] = [];

  const flush = () => {
    const paired = Math.min(removed.length, added.length);
    for (let index = 0; index < Math.max(removed.length, added.length); index++) {
      const oldText = index < removed.length ? removed[index] : null;
      const newText = index < added.length ? added[index] : null;
      if (index < paired && oldText !== null && newText !== null) {
        const [oldSegments, newSegments] = highlightChange(oldText, newText);
        rows.push({ kind: 'changed', before: { line: ++beforeLine, segments: oldSegments }, after: { line: ++afterLine, segments: newSegments } });
      } else if (oldText !== null) {
        rows.push({ kind: 'removed', before: { line: ++beforeLine, segments: [{ text: oldText, changed: true }] }, after: null });
      } else if (newText !== null) {
        rows.push({ kind: 'added', before: null, after: { line: ++afterLine, segments: [{ text: newText, changed: true }] } });
      }
    }
    removed = [];
    added = [];
  };

  for (const op of ops) {
    if (op.type === 'delete') { removed.push(op.line); continue; }
    if (op.type === 'insert') { added.push(op.line); continue; }
    flush();
    const segments = [{ text: op.line, changed: false }];
    rows.push({ kind: 'equal', before: { line: ++beforeLine, segments }, after: { line: ++afterLine, segments } });
  }
  flush();

  return {
    rows,
    changedRows: rows.filter(row => row.kind !== 'equal').length,
    removedLines: ops.filter(op => op.type === 'delete').length,
    addedLines: ops.filter(op => op.type === 'insert').length
  };
}

export function collapseUnchanged(rows: ReadonlyArray<DiffRow>, context = 3): DiffBlock[] {
  const keep = new Array<boolean>(rows.length).fill(false);
  rows.forEach((row, index) => {
    if (row.kind === 'equal') return;
    for (let near = Math.max(0, index - context); near <= Math.min(rows.length - 1, index + context); near++) keep[near] = true;
  });

  const blocks: DiffBlock[] = [];
  let index = 0;
  while (index < rows.length) {
    const start = index;
    const kept = keep[index];
    while (index < rows.length && keep[index] === kept) index++;
    blocks.push(kept
      ? { kind: 'rows', key: `rows-${start}`, rows: rows.slice(start, index) }
      : { kind: 'gap', key: `gap-${start}`, hidden: index - start });
  }
  return blocks;
}

function diffLines(before: string[], after: string[]): Op[] {
  let start = 0;
  while (start < before.length && start < after.length && before[start] === after[start]) start++;
  let endBefore = before.length;
  let endAfter = after.length;
  while (endBefore > start && endAfter > start && before[endBefore - 1] === after[endAfter - 1]) { endBefore--; endAfter--; }

  const equal = (line: string): Op => ({ type: 'equal', line });
  return [
    ...before.slice(0, start).map(equal),
    ...myers(before.slice(start, endBefore), after.slice(start, endAfter)),
    ...before.slice(endBefore).map(equal)
  ];
}

// Myers O(ND) shortest edit script; each trace step keeps only the diagonals -d..d it can reach.
function myers(a: string[], b: string[]): Op[] {
  const n = a.length;
  const m = b.length;
  const replaceAll = (): Op[] => [...a.map(line => ({ type: 'delete' as const, line })), ...b.map(line => ({ type: 'insert' as const, line }))];
  if (n === 0 || m === 0) return replaceAll();

  const max = n + m;
  const offset = max;
  const v = new Int32Array(2 * max + 2);
  const trace: Int32Array[] = [];
  const limit = Math.min(max, maxEditDistance);

  for (let d = 0; d <= limit; d++) {
    trace.push(v.slice(offset - d, offset + d + 1));
    for (let k = -d; k <= d; k += 2) {
      let x = k === -d || (k !== d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
      let y = x - k;
      while (x < n && y < m && a[x] === b[y]) { x++; y++; }
      v[offset + k] = x;
      if (x >= n && y >= m) return backtrack(trace, a, b);
    }
  }
  return replaceAll();
}

function backtrack(trace: ReadonlyArray<Int32Array>, a: string[], b: string[]): Op[] {
  const ops: Op[] = [];
  let x = a.length;
  let y = b.length;
  for (let d = trace.length - 1; d >= 0; d--) {
    const v = trace[d];
    const at = (k: number) => v[k + d];
    const k = x - y;
    const previousK = k === -d || (k !== d && at(k - 1) < at(k + 1)) ? k + 1 : k - 1;
    const previousX = at(previousK);
    const previousY = previousX - previousK;
    while (x > previousX && y > previousY) { ops.push({ type: 'equal', line: a[x - 1] }); x--; y--; }
    if (d > 0) ops.push(x === previousX ? { type: 'insert', line: b[y - 1] } : { type: 'delete', line: a[x - 1] });
    x = previousX;
    y = previousY;
  }
  return ops.reverse();
}

function highlightChange(before: string, after: string): [DiffSegment[], DiffSegment[]] {
  let prefix = 0;
  while (prefix < before.length && prefix < after.length && before[prefix] === after[prefix]) prefix++;
  let suffix = 0;
  while (suffix < before.length - prefix && suffix < after.length - prefix && before[before.length - 1 - suffix] === after[after.length - 1 - suffix]) suffix++;

  const segments = (text: string): DiffSegment[] => [
    { text: text.slice(0, prefix), changed: false },
    { text: text.slice(prefix, text.length - suffix), changed: true },
    { text: text.slice(text.length - suffix), changed: false }
  ].filter(segment => segment.text.length > 0);
  return [segments(before), segments(after)];
}
