import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DiffBlock, buildSideBySideDiff, collapseUnchanged } from '../../core/diff/line-diff';

@Component({ standalone: false, selector: 'aa-source-diff', templateUrl: './source-diff.component.html', styleUrl: './source-diff.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class SourceDiffComponent {
  readonly before = input('');
  readonly after = input('');
  readonly label = input('source');
  readonly showWholeFile = signal(false);
  readonly diff = computed(() => buildSideBySideDiff(this.before(), this.after()));
  readonly blocks = computed<DiffBlock[]>(() => this.showWholeFile()
    ? [{ kind: 'rows', key: 'all', rows: this.diff().rows }]
    : collapseUnchanged(this.diff().rows));
  readonly summary = computed(() => {
    const diff = this.diff();
    if (diff.changedRows === 0) return 'Before and after are identical.';
    return `${diff.changedRows} line(s) differ: ${diff.removedLines} before, ${diff.addedLines} after.`;
  });

  toggleWholeFile(): void { this.showWholeFile.update(value => !value); }
}
