// Folders the user chooses not to load at all: their files never enter the project, the analysis or the download.

export interface SkippableFolder {
  name: string;
  description: string;
  /** Unchecked by default when the name is also common for source folders (monorepo packages/, out/). */
  checkedByDefault: boolean;
}

/** Offered as checkboxes. */
export const skippableFolders: ReadonlyArray<SkippableFolder> = [
  { name: 'node_modules', description: 'npm packages', checkedByDefault: true },
  { name: 'bin', description: '.NET build output', checkedByDefault: true },
  { name: 'obj', description: '.NET intermediate output', checkedByDefault: true },
  { name: '.git', description: 'git history', checkedByDefault: true },
  { name: '.vs', description: 'Visual Studio state', checkedByDefault: true },
  { name: '.vscode', description: 'VS Code settings', checkedByDefault: true },
  { name: '.idea', description: 'JetBrains settings', checkedByDefault: true },
  { name: 'dist', description: 'build output', checkedByDefault: true },
  { name: 'build', description: 'build output', checkedByDefault: true },
  { name: 'out', description: 'build output', checkedByDefault: false },
  { name: '.angular', description: 'Angular cache', checkedByDefault: true },
  { name: 'coverage', description: 'test coverage', checkedByDefault: true },
  { name: 'TestResults', description: 'test results', checkedByDefault: true },
  { name: 'packages', description: 'NuGet packages (also monorepo sources)', checkedByDefault: false },
  { name: '.terraform', description: 'Terraform providers', checkedByDefault: true },
  { name: '.next', description: 'Next.js output', checkedByDefault: true },
  { name: '__pycache__', description: 'Python cache', checkedByDefault: true },
  { name: '.venv', description: 'Python environment', checkedByDefault: true }
];

export const defaultSkippedFolders: ReadonlyArray<string> = skippableFolders.filter(folder => folder.checkedByDefault).map(folder => folder.name);

/** At most this many custom folder names are accepted. */
export const maxCustomFolders = 50;

/**
 * "App_Data, logs; temp" → ['App_Data', 'logs', 'temp']. Only plain folder names: anything with a path separator or
 * wildcard is dropped, as are "." and "..".
 */
export function parseCustomFolders(text: string): string[] {
  const names = text.split(/[,;\n]/).map(name => name.trim()).filter(name =>
    name.length > 0 && name.length <= 100 && name !== '.' && name !== '..' && !/[\\/*?:"<>|]/.test(name));
  const seen = new Set<string>();
  return names.filter(name => !seen.has(name.toLowerCase()) && seen.add(name.toLowerCase())).slice(0, maxCustomFolders);
}

/** Lower-cased set of every folder name to skip. */
export function skippedFolderSet(checked: Iterable<string>, custom: Iterable<string>): ReadonlySet<string> {
  return new Set([...checked, ...custom].map(name => name.toLowerCase()));
}

/** True when any folder on the file's path (not the file name itself) is skipped. Case-insensitive. */
export function isInSkippedFolder(path: string, skipped: ReadonlySet<string>): boolean {
  if (skipped.size === 0) return false;
  const segments = path.replaceAll('\\', '/').split('/').filter(Boolean);
  return segments.slice(0, -1).some(segment => skipped.has(segment.toLowerCase()));
}

/** Splits a selection into the files to load and the number left out, with the count per skipped folder name. */
export function filterSkippedFolders<T extends { path: string }>(items: ReadonlyArray<T>, skipped: ReadonlySet<string>): { kept: T[]; skippedFiles: number; byFolder: ReadonlyMap<string, number> } {
  const kept: T[] = [];
  const byFolder = new Map<string, number>();
  for (const item of items) {
    if (!isInSkippedFolder(item.path, skipped)) { kept.push(item); continue; }
    const folder = item.path.replaceAll('\\', '/').split('/').filter(Boolean).slice(0, -1).find(segment => skipped.has(segment.toLowerCase()))!;
    byFolder.set(folder, (byFolder.get(folder) ?? 0) + 1);
  }
  return { kept, skippedFiles: items.length - kept.length, byFolder };
}
