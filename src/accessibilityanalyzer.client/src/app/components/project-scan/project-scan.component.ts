import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AccessibilityApiService } from '../../core/api/accessibility-api.service';
import { createProjectZip } from '../../core/files/project-archive';
import { readProjectZip } from '../../core/files/zip-reader';
import { defaultSkippedFolders, filterSkippedFolders, parseCustomFolders, skippableFolders, skippedFolderSet } from '../../core/files/folder-filter';
import { classifyByName, isInIgnoredFolder, isTextFile, maxAnalysisFiles, maxSourceBytes, maxSourceCharacters } from '../../core/files/project-file-classifier';
import { AccessibilityReport, ProjectAccessibilityReport, SourceAnalysisResult, SourceInput } from '../../core/models/analysis.models';

const skipPreferencesKey = 'accessibility-analyzer.skipped-folders';
const maxBatchFiles = 40;
const maxBatchCharacters = 5 * 1024 * 1024;

interface SelectedProjectFile {
  path: string;
  file: File;
}

interface LoadedProjectFile extends SelectedProjectFile {
  analysisIgnored: boolean;
  textPreviewable: boolean;
  analysis: SourceAnalysisResult | null;
}

export type ProjectFileState = 'modified' | 'unchanged-html' | 'warnings-only' | 'not-analyzed' | 'binary';

export interface ProjectTreeFile {
  kind: 'file';
  name: string;
  path: string;
  index: number;
  state: ProjectFileState;
  warnings: number;
}

export interface ProjectTreeFolder {
  kind: 'folder';
  name: string;
  path: string;
  children: ProjectTreeNode[];
  totalFiles: number;
  modifiedFiles: number;
}

export type ProjectTreeNode = ProjectTreeFolder | ProjectTreeFile;

@Component({ standalone: false, selector: 'aa-project-scan', templateUrl: './project-scan.component.html', styleUrl: './project-scan.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class ProjectScanComponent {
  private readonly api = inject(AccessibilityApiService);
  readonly analyzed = output<AccessibilityReport | null>();
  readonly projectReport = output<ProjectAccessibilityReport>();
  readonly sourceBatch = output<ReadonlyArray<SourceInput>>();
  readonly busy = signal(false);
  readonly status = signal('');
  readonly error = signal('');
  readonly sourceLabel = signal('');
  readonly loadedFiles = signal<ReadonlyArray<LoadedProjectFile>>([]);
  readonly analysisComplete = signal(false);
  readonly selectedIndex = signal(0);
  readonly originalPreview = signal('');
  readonly downloadPreview = signal('');
  readonly previewStatus = signal('');
  readonly previewReady = signal(false);
  readonly onlyModified = signal(false);
  readonly expandedFolders = signal<ReadonlySet<string>>(new Set());
  readonly skippableFolders = skippableFolders;
  readonly skippedChecked = signal<ReadonlySet<string>>(new Set(defaultSkippedFolders));
  readonly customSkipped = signal('');
  readonly customSkippedNames = computed(() => parseCustomFolders(this.customSkipped()));
  readonly skipSet = computed(() => skippedFolderSet(this.skippedChecked(), this.customSkippedNames()));
  readonly skipSummary = signal('');

  constructor() {
    this.restoreSkipPreferences();
  }
  readonly selectedFile = computed(() => this.loadedFiles()[this.selectedIndex()] ?? null);
  readonly selectedAnalysis = computed(() => this.selectedFile()?.analysis ?? null);
  readonly selectedChanged = computed(() => (this.selectedAnalysis()?.appliedFixes.length ?? 0) > 0);
  readonly modifiedIndexes = computed(() => this.loadedFiles().flatMap((item, index) => isModified(item) ? [index] : []));
  readonly selectedModifiedPosition = computed(() => this.modifiedIndexes().indexOf(this.selectedIndex()) + 1);
  readonly canSelectPreviousModified = computed(() => this.modifiedIndexes().some(index => index < this.selectedIndex()));
  readonly canSelectNextModified = computed(() => this.modifiedIndexes().some(index => index > this.selectedIndex()));
  readonly tree = computed(() => buildProjectTree(this.loadedFiles(), this.onlyModified()));
  readonly counts = computed(() => {
    const files = this.loadedFiles();
    return {
      total: files.length,
      htmlComponents: files.filter(item => item.analysis?.artifact.htmlComponent === true).length,
      modified: this.modifiedIndexes().length,
      warningsOnly: files.filter(item => fileState(item) === 'warnings-only').length
    };
  });

  filesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    void this.loadSelection(files.map(file => ({ path: this.pathOf(file), file })), 'Selected files');
  }

  folderSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    void this.loadSelection(files.map(file => ({ path: this.pathOf(file), file })), 'Selected folder');
  }

  async zipSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const archive = input.files?.[0];
    input.value = '';
    if (!archive) return;

    this.error.set('');
    this.busy.set(true);
    this.status.set(`Reading ${archive.name} in the browser…`);
    let entries: SelectedProjectFile[];
    try {
      entries = await readProjectZip(archive);
    } catch (exception) {
      this.error.set(exception instanceof Error ? exception.message : 'The ZIP file could not be read.');
      this.status.set('');
      this.busy.set(false);
      return;
    }
    this.busy.set(false);
    await this.loadSelection(entries, archive.name.replace(/\.zip$/i, ''));
  }

  toggleOnlyModified(event: Event): void {
    this.onlyModified.set((event.target as HTMLInputElement).checked);
  }

  isSkipped(name: string): boolean { return this.skippedChecked().has(name); }

  toggleSkipped(name: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.skippedChecked.update(current => {
      const next = new Set(current);
      if (checked) next.add(name); else next.delete(name);
      return next;
    });
    this.saveSkipPreferences();
  }

  customSkippedChanged(event: Event): void {
    this.customSkipped.set((event.target as HTMLInputElement).value);
    this.saveSkipPreferences();
  }

  resetSkipped(): void {
    this.skippedChecked.set(new Set(defaultSkippedFolders));
    this.customSkipped.set('');
    this.saveSkipPreferences();
  }

  isExpanded(path: string): boolean { return this.expandedFolders().has(path); }

  toggleFolder(path: string): void {
    this.expandedFolders.update(current => {
      const next = new Set(current);
      if (next.has(path)) next.delete(path); else next.add(path);
      return next;
    });
  }

  expandAll(): void {
    const folders = new Set<string>();
    const visit = (nodes: ReadonlyArray<ProjectTreeNode>) => nodes.forEach(node => { if (node.kind === 'folder') { folders.add(node.path); visit(node.children); } });
    visit(this.tree());
    this.expandedFolders.set(folders);
  }

  collapseAll(): void { this.expandedFolders.set(new Set()); }

  selectFileAt(index: number): void {
    if (!Number.isInteger(index) || index < 0 || index >= this.loadedFiles().length) return;
    this.selectedIndex.set(index);
    void this.preparePreview(index);
  }

  selectPreviousModified(): void {
    const previous = this.modifiedIndexes().filter(index => index < this.selectedIndex()).at(-1);
    if (previous !== undefined) this.selectFileAt(previous);
  }

  selectNextModified(): void {
    const next = this.modifiedIndexes().find(index => index > this.selectedIndex());
    if (next !== undefined) this.selectFileAt(next);
  }

  async copySelectedDownloadVersion(): Promise<void> {
    const current = this.selectedFile();
    if (!current?.textPreviewable) return;
    try {
      await navigator.clipboard.writeText(this.downloadPreview());
      this.status.set(`Corrected version copied for ${current.path}.`);
    } catch {
      this.status.set('The browser did not allow clipboard access. Select the corrected text and copy it manually.');
    }
  }

  downloadCompleteProject(): Promise<void> { return this.downloadArchive(false); }

  downloadModifiedFiles(): Promise<void> { return this.downloadArchive(true); }

  private async downloadArchive(modifiedOnly: boolean): Promise<void> {
    const files = modifiedOnly ? this.loadedFiles().filter(isModified) : this.loadedFiles();
    if (!files.length || this.busy()) return;

    this.busy.set(true);
    this.error.set('');
    this.status.set(modifiedOnly ? `Preparing a ZIP with the ${files.length} modified file(s)…` : `Preparing a complete project download with all ${files.length} loaded file(s)…`);
    try {
      const entries = files.map(item => ({
        path: item.path,
        source: isModified(item) && item.analysis ? item.analysis.correctedContent : item.file,
        lastModified: item.file.lastModified
      }));
      const archive = await createProjectZip(entries);
      const url = URL.createObjectURL(archive);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${this.projectArchiveName(this.loadedFiles())}-accessibility-${modifiedOnly ? 'modified-files' : 'updated'}.zip`;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      const changed = files.filter(isModified).length;
      this.status.set(modifiedOnly
        ? `Download created with the ${changed} modified file(s), in their project folders.`
        : `Complete project download created: ${files.length} file(s), ${changed} corrected, ${files.length - changed} preserved unchanged.`);
    } catch (exception) {
      this.error.set(exception instanceof Error ? exception.message : 'The project download could not be created.');
      this.status.set('');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadSelection(selection: ReadonlyArray<SelectedProjectFile>, label: string): Promise<void> {
    this.error.set('');
    this.loadedFiles.set([]);
    this.analysisComplete.set(false);
    this.selectedIndex.set(0);
    this.originalPreview.set('');
    this.downloadPreview.set('');
    this.previewStatus.set('');
    this.previewReady.set(false);
    this.expandedFolders.set(new Set());
    this.analyzed.emit(null);
    this.skipSummary.set('');
    if (!selection.length) return;

    // Files inside the skipped folders (node_modules, bin, obj…) are dropped before anything else happens.
    const { kept, skippedFiles, byFolder } = filterSkippedFolders(selection, this.skipSet());
    if (skippedFiles > 0) {
      const detail = [...byFolder].sort((left, right) => right[1] - left[1]).map(([folder, count]) => `${folder}: ${count.toLocaleString()}`).join(', ');
      this.skipSummary.set(`${skippedFiles.toLocaleString()} file(s) in skipped folders were not loaded (${detail}).`);
    }
    if (!kept.length) {
      this.error.set('Every selected file is inside a skipped folder. Uncheck that folder under "Folders to skip" and load again.');
      this.status.set('');
      return;
    }

    this.busy.set(true);
    this.status.set(`Loading ${kept.length.toLocaleString()} project file(s)…`);
    // Text or binary is decided from each file's first bytes, never by reading whole files.
    const loaded = (await Promise.all(kept.map(item => this.describeFile(item)))).sort((left, right) => left.path.localeCompare(right.path));
    this.sourceLabel.set(this.projectRootLabel(loaded, label));
    this.loadedFiles.set(loaded);
    this.sourceBatch.emit([]);
    this.projectReport.emit({ rootPath: this.sourceLabel(), sources: [], totalFiles: loaded.length, htmlCandidates: 0 });

    const textFiles = loaded.filter(item => item.textPreviewable && !item.analysisIgnored);
    const candidates = textFiles.filter(item => item.file.size <= maxSourceBytes);
    if (candidates.length > maxAnalysisFiles) {
      this.error.set(`The selected project contains ${candidates.length.toLocaleString()} text source files outside generated and dependency folders. Select a project, or a folder of it, with ${maxAnalysisFiles.toLocaleString()} or fewer.`);
      this.status.set(`${loaded.length} file(s) were loaded for the project structure and complete-project download; analysis was not started.`);
      this.busy.set(false);
      this.expandFoldersOf([]);
      await this.preparePreview(0);
      return;
    }

    try {
      let sources: SourceInput[] = [];
      let allResults: SourceAnalysisResult[] = [];
      let skippedOversize = textFiles.length - candidates.length;
      if (candidates.length > 0) {
        this.status.set(`Loaded ${loaded.length} project file(s). Reading ${candidates.length} project text source file(s)…`);
        const candidateSources = await Promise.all(candidates.map(async item => ({ name: item.path, content: await item.file.text() })));
        sources = candidateSources.filter(source => source.content.length <= maxSourceCharacters);
        skippedOversize += candidateSources.length - sources.length;
        const batches = this.createBatches(sources);

        for (let index = 0; index < batches.length; index++) {
          this.status.set(`Checking Section 508 accessibility, batch ${index + 1} of ${batches.length}…`);
          const batchResults = await firstValueFrom(this.api.analyzeSources(batches[index]));
          allResults.push(...batchResults);
        }
      }

      const byPath = new Map(allResults.map(result => [result.artifact.path, result] as const));
      const withAnalysis = loaded.map(item => ({ ...item, analysis: byPath.get(item.path) ?? null }));
      this.loadedFiles.set(withAnalysis);
      this.analysisComplete.set(true);

      const report: ProjectAccessibilityReport = {
        rootPath: this.sourceLabel(),
        sources: allResults,
        totalFiles: withAnalysis.length,
        htmlCandidates: allResults.filter(result => result.artifact.containsHtml).length
      };
      this.sourceBatch.emit(sources);
      this.projectReport.emit(report);

      const modified = this.modifiedIndexes();
      this.expandFoldersOf(modified.map(index => withAnalysis[index].path));
      const analyzedIndex = withAnalysis.findIndex(item => item.analysis !== null);
      const initialIndex = modified.length ? modified[0] : analyzedIndex >= 0 ? analyzedIndex : 0;
      this.selectedIndex.set(initialIndex);
      await this.preparePreview(initialIndex);

      const counts = this.counts();
      const skippedText = skippedOversize > 0 ? ` ${skippedOversize} oversized source file(s) were kept unchanged and not analyzed.` : '';
      this.status.set(`Analysis complete: ${counts.total} file(s) loaded, ${counts.htmlComponents} HTML component(s) checked, ${counts.modified} modified, ${counts.warningsOnly} other file(s) with warnings left unchanged.${skippedText}`);
    } catch (response: any) {
      this.error.set(response?.error?.error ?? (response instanceof Error ? `Browser-selected file analysis failed: ${response.message}` : 'Browser-selected file analysis failed.'));
      this.status.set(`${loaded.length} file(s) remain loaded for the project structure and download, but accessibility analysis did not complete.`);
      this.expandFoldersOf([]);
      await this.preparePreview(0);
    } finally {
      this.busy.set(false);
    }
  }

  // Opens the top level plus every folder on the way to a modified file.
  private expandFoldersOf(paths: ReadonlyArray<string>): void {
    const folders = new Set<string>();
    for (const node of this.tree()) if (node.kind === 'folder') folders.add(node.path);
    for (const path of paths) {
      const segments = path.split('/').filter(Boolean);
      for (let depth = 1; depth < segments.length; depth++) folders.add(segments.slice(0, depth).join('/'));
    }
    this.expandedFolders.set(folders);
  }

  private async preparePreview(index: number): Promise<void> {
    const current = this.loadedFiles()[index];
    if (!current) return;

    const analysis = current.analysis;
    this.analyzed.emit(analysis?.report ?? null);
    this.previewReady.set(false);

    if (!current.textPreviewable) {
      this.originalPreview.set('');
      this.downloadPreview.set('');
      this.previewStatus.set(`Binary or non-text file. ${current.file.size.toLocaleString()} bytes are kept byte-for-byte in the project download.`);
      return;
    }

    if (current.file.size > maxSourceBytes) {
      this.originalPreview.set('');
      this.downloadPreview.set('');
      this.previewStatus.set(`Too large to preview (${current.file.size.toLocaleString()} bytes). It is kept byte-for-byte in the project download.`);
      return;
    }

    this.previewStatus.set(`Loading ${current.path}…`);
    const original = await current.file.text();
    if (this.selectedIndex() !== index) return;
    this.previewReady.set(true);
    this.originalPreview.set(original);
    this.downloadPreview.set(analysis?.appliedFixes.length ? analysis.correctedContent : original);
    switch (fileState(current)) {
      case 'modified': this.previewStatus.set(current.analysis?.artifact.htmlComponent
        ? 'HTML component corrected. The before and after below show only what changed.'
        : 'Markup inside this script corrected (component template or JSX); the code around it is unchanged. The before and after below show only what changed.'); break;
      case 'unchanged-html': this.previewStatus.set('HTML component checked. No deterministic correction applied, so the file is unchanged.'); break;
      case 'warnings-only': this.previewStatus.set('No safe correction applies to this file (only HTML components, component templates and JSX are corrected). Its accessibility warnings are listed below for the developer.'); break;
      default: this.previewStatus.set(current.analysisIgnored
        ? 'Generated or dependency folder: kept unchanged in the download and not analyzed.'
        : 'Not an HTML component: kept unchanged in the download.');
    }
  }

  private createBatches(sources: ReadonlyArray<SourceInput>): SourceInput[][] {
    const batches: SourceInput[][] = [];
    let current: SourceInput[] = [];
    let characters = 0;
    for (const source of sources) {
      const sourceCharacters = source.content.length;
      if (current.length > 0 && (current.length >= maxBatchFiles || characters + sourceCharacters > maxBatchCharacters)) {
        batches.push(current);
        current = [];
        characters = 0;
      }
      current.push(source);
      characters += sourceCharacters;
    }
    if (current.length > 0) batches.push(current);
    return batches;
  }

  private pathOf(file: File): string {
    return file.webkitRelativePath || file.name;
  }

  private async describeFile(selected: SelectedProjectFile): Promise<LoadedProjectFile> {
    const path = selected.path.replaceAll('\\', '/');
    const name = path.split('/').filter(Boolean).at(-1) ?? selected.file.name;
    const analysisIgnored = isInIgnoredFolder(path);
    let textPreviewable: boolean;
    try {
      // Dependency and generated folders (node_modules…) can hold tens of thousands of files: decide by name only there.
      textPreviewable = analysisIgnored
        ? classifyByName(name, selected.file.type) === 'text'
        : await isTextFile(selected.file, name, selected.file.type);
    } catch {
      textPreviewable = false; // unreadable (moved or locked since it was selected): kept as is, never analyzed
    }
    return { path, file: selected.file, analysisIgnored, textPreviewable, analysis: null };
  }

  private restoreSkipPreferences(): void {
    try {
      const saved = JSON.parse(localStorage.getItem(skipPreferencesKey) ?? 'null') as { checked?: unknown; custom?: unknown } | null;
      if (!saved) return;
      const known = new Set(skippableFolders.map(folder => folder.name));
      if (Array.isArray(saved.checked)) this.skippedChecked.set(new Set(saved.checked.filter((name): name is string => typeof name === 'string' && known.has(name))));
      if (typeof saved.custom === 'string') this.customSkipped.set(saved.custom.slice(0, 2000));
    } catch {
      // Storage unavailable or unreadable: keep the defaults.
    }
  }

  private saveSkipPreferences(): void {
    try {
      localStorage.setItem(skipPreferencesKey, JSON.stringify({ checked: [...this.skippedChecked()], custom: this.customSkipped() }));
    } catch {
      // Storage unavailable (private mode, policy): the choice still applies to this page.
    }
  }

  private projectRootLabel(files: ReadonlyArray<LoadedProjectFile>, fallback: string): string {
    if (!files.length) return fallback;
    const roots = new Set(files.map(item => item.path.split('/')[0]).filter(Boolean));
    return roots.size === 1 && files.every(item => item.path.includes('/')) ? [...roots][0] : fallback;
  }

  private projectArchiveName(files: ReadonlyArray<LoadedProjectFile>): string {
    const label = this.projectRootLabel(files, this.sourceLabel() || 'accessibility-project');
    return label.replace(/[^a-z0-9._-]+/gi, '-').replace(/^-+|-+$/g, '') || 'accessibility-project';
  }
}

function isModified(item: LoadedProjectFile): boolean {
  return (item.analysis?.appliedFixes.length ?? 0) > 0;
}

function warningCount(item: LoadedProjectFile): number {
  return (item.analysis?.report.findings.length ?? 0) + (item.analysis?.report.dynamicFindings.length ?? 0);
}

function fileState(item: LoadedProjectFile): ProjectFileState {
  if (!item.textPreviewable) return 'binary';
  if (isModified(item)) return 'modified';
  if (item.analysis?.artifact.htmlComponent) return 'unchanged-html';
  if (item.analysis && warningCount(item) > 0) return 'warnings-only';
  return 'not-analyzed';
}

function buildProjectTree(files: ReadonlyArray<LoadedProjectFile>, onlyModified: boolean): ProjectTreeNode[] {
  const root: ProjectTreeFolder = { kind: 'folder', name: '', path: '', children: [], totalFiles: 0, modifiedFiles: 0 };
  const folders = new Map<string, ProjectTreeFolder>([['', root]]);

  files.forEach((item, index) => {
    const modified = isModified(item);
    if (onlyModified && !modified) return;
    const segments = item.path.split('/').filter(Boolean);
    const chain = [root];
    for (let depth = 1; depth < segments.length; depth++) {
      const path = segments.slice(0, depth).join('/');
      let folder = folders.get(path);
      if (!folder) {
        folder = { kind: 'folder', name: segments[depth - 1], path, children: [], totalFiles: 0, modifiedFiles: 0 };
        folders.set(path, folder);
        chain[chain.length - 1].children.push(folder);
      }
      chain.push(folder);
    }
    for (const folder of chain) {
      folder.totalFiles++;
      if (modified) folder.modifiedFiles++;
    }
    chain[chain.length - 1].children.push({ kind: 'file', name: segments.at(-1) ?? item.path, path: item.path, index, state: fileState(item), warnings: warningCount(item) });
  });

  const sort = (nodes: ProjectTreeNode[]) => {
    nodes.sort((left, right) => left.kind !== right.kind ? (left.kind === 'folder' ? -1 : 1) : left.name.localeCompare(right.name));
    for (const node of nodes) if (node.kind === 'folder') sort(node.children);
  };
  sort(root.children);
  return root.children;
}
