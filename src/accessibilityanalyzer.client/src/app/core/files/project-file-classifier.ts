// Decides, before anything is read in full, which browser-selected files are text that can be analyzed.
// Pure functions over Blob/File so the frontend tests can run them in Node.

/** The API refuses a source longer than this (characters). */
export const maxSourceCharacters = 2 * 1024 * 1024;
/** A file larger than this (bytes) is never read: it cannot be a source within the limit above. */
export const maxSourceBytes = maxSourceCharacters * 3;
/** Above this number of text sources the project is too large to analyze in one go in a browser tab. */
export const maxAnalysisFiles = 10_000;
/** Bytes looked at to tell text from binary. */
export const sniffBytes = 8 * 1024;

export const analysisCandidateExtensions: ReadonlySet<string> = new Set(['.html', '.htm', '.xhtml', '.cshtml', '.vbhtml', '.razor', '.aspx', '.ascx', '.master', '.js', '.mjs', '.cjs', '.ts', '.jsx', '.tsx', '.vue', '.svelte', '.cs']);

export const textExtensions: ReadonlySet<string> = new Set([
  ...analysisCandidateExtensions,
  '.css', '.scss', '.sass', '.less', '.json', '.jsonc', '.xml', '.xaml', '.svg', '.md', '.txt', '.yml', '.yaml', '.toml', '.ini', '.config',
  '.csproj', '.fsproj', '.vbproj', '.esproj', '.sln', '.slnx', '.props', '.targets', '.editorconfig', '.gitignore', '.gitattributes', '.ps1', '.psm1',
  '.cmd', '.bat', '.sh', '.sql', '.graphql', '.gql', '.csv', '.tsv', '.env.example', '.py', '.java', '.php', '.rb', '.go', '.kt', '.swift', '.vb'
]);

export const binaryExtensions: ReadonlySet<string> = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.ico', '.avif', '.tif', '.tiff', '.heic', '.psd',
  '.pdf', '.zip', '.7z', '.rar', '.gz', '.tgz', '.tar', '.bz2', '.xz', '.nupkg', '.iso', '.msi', '.apk', '.aab', '.ipa',
  '.exe', '.dll', '.so', '.dylib', '.pdb', '.class', '.jar', '.war', '.o', '.a', '.lib', '.wasm', '.pyc',
  '.woff', '.woff2', '.ttf', '.otf', '.eot',
  '.mp3', '.wav', '.ogg', '.flac', '.aac', '.m4a', '.mp4', '.webm', '.mov', '.avi', '.mkv',
  '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
  '.onnx', '.bin', '.dat', '.db', '.sqlite', '.mdf', '.ldf', '.pfx', '.p12', '.der', '.snk', '.keystore', '.jks'
]);

/** Generated, dependency and tool folders: their files stay in the project download but are never analyzed. */
export const ignoredAnalysisFolders: ReadonlySet<string> = new Set([
  'node_modules', '.git', '.vs', 'bin', 'obj', 'dist', 'build', 'coverage', '.angular', '__macosx',
  '.terraform', 'testresults', '.next', '.nuxt', '.svelte-kit', '.parcel-cache', '.turbo', '__pycache__', '.venv', '.gradle'
]);

const textFileNames: ReadonlySet<string> = new Set(['dockerfile', 'makefile', 'license', 'readme', 'changelog', 'notice']);

export function extensionOf(name: string): string {
  const lower = name.toLowerCase();
  if (lower.endsWith('.env.example')) return '.env.example';
  const dot = lower.lastIndexOf('.');
  return dot >= 0 ? lower.slice(dot) : '';
}

export function isInIgnoredFolder(path: string): boolean {
  return path.split('/').filter(Boolean).slice(0, -1).some(segment => ignoredAnalysisFolders.has(segment.toLowerCase()));
}

/** What the name and MIME type alone say: 'text', 'binary', or 'unknown' (the content decides). */
export function classifyByName(name: string, type: string): 'text' | 'binary' | 'unknown' {
  const extension = extensionOf(name);
  if (binaryExtensions.has(extension)) return 'binary';
  if (type.startsWith('image/') && extension !== '.svg') return 'binary';
  if (type.startsWith('audio/') || type.startsWith('video/') || type.startsWith('font/')) return 'binary';
  if (textExtensions.has(extension) || textFileNames.has(name.toLowerCase())) return 'text';
  return 'unknown';
}

/** Text has no NUL byte and few control characters in its first bytes (the check git and editors use). */
export function bytesLookLikeText(bytes: Uint8Array): boolean {
  if (bytes.length === 0) return true;
  let control = 0;
  for (const byte of bytes) {
    if (byte === 0) return false;
    if (byte < 0x09 || (byte > 0x0d && byte < 0x20)) control++;
  }
  return control / bytes.length < 0.1;
}

export async function looksLikeText(file: Blob): Promise<boolean> {
  return bytesLookLikeText(new Uint8Array(await file.slice(0, sniffBytes).arrayBuffer()));
}

/**
 * True when the file is text. Known binary types are decided by name; everything else is confirmed by its first bytes,
 * so a binary with a text-like or no extension (certificates, model weights, tool executables) is never read as text.
 */
export async function isTextFile(file: Blob, name: string, type: string): Promise<boolean> {
  return classifyByName(name, type) !== 'binary' && await looksLikeText(file);
}
