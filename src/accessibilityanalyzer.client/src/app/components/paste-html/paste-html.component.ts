import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { AccessibilityApiService } from '../../core/api/accessibility-api.service';
import { AccessibilityReport } from '../../core/models/analysis.models';

@Component({ standalone: false, selector: 'aa-paste-html', templateUrl: './paste-html.component.html', styleUrl: './paste-html.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class PasteHtmlComponent {
  private readonly api = inject(AccessibilityApiService);
  readonly analyzed = output<AccessibilityReport>();
  readonly html = signal('<html lang="en"><head><title>Example</title></head><body><button title="Save"></button></body></html>');
  readonly preferredLanguage = signal('');
  readonly correctedHtml = signal('');
  readonly analyzedHtml = signal('');
  readonly appliedFixes = signal<ReadonlyArray<string>>([]);
  readonly correctionReady = signal(false);
  readonly busy = signal(false);
  readonly status = signal('');

  updateHtml(event: Event): void {
    this.html.set((event.target as HTMLTextAreaElement).value);
    this.correctionReady.set(false);
    this.correctedHtml.set('');
    this.appliedFixes.set([]);
  }

  updateLanguage(event: Event): void {
    this.preferredLanguage.set((event.target as HTMLInputElement).value);
    this.correctionReady.set(false);
  }

  analyze(): void {
    const original = this.html();
    this.busy.set(true);
    this.status.set('Analyzing markup, applying safe corrections, and locating remaining warnings…');
    this.api.analyzeAndFixHtml(original, 'pasted-html', this.preferredLanguage().trim() || undefined)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: result => {
          this.analyzedHtml.set(original);
          this.correctedHtml.set(result.correctedContent);
          this.appliedFixes.set(result.appliedFixes);
          this.correctionReady.set(true);
          this.analyzed.emit(result.report);
          const fixText = result.appliedFixes.length === 1 ? '1 automatic correction applied' : `${result.appliedFixes.length} automatic corrections applied`;
          const warnings = result.report.findings.reduce((sum, finding) => sum + Math.max(1, finding.occurrences), 0) + result.report.dynamicFindings.reduce((sum, finding) => sum + Math.max(1, finding.occurrences), 0);
          this.status.set(`Analysis complete. ${fixText}; ${warnings} warning occurrence(s) remain for validation or developer input.`);
        },
        error: error => this.status.set(error?.error?.error ?? 'Analysis failed.')
      });
  }

  replaceSourceWithCorrected(): void {
    if (!this.correctionReady()) return;
    this.html.set(this.correctedHtml());
    this.status.set('The source textarea now contains the corrected HTML.');
  }

  async copyCorrected(): Promise<void> {
    if (!this.correctionReady()) return;
    try {
      await navigator.clipboard.writeText(this.correctedHtml());
      this.status.set('Corrected HTML copied to the clipboard.');
    } catch {
      this.status.set('The browser did not allow clipboard access. Select the corrected HTML and copy it manually.');
    }
  }
}
