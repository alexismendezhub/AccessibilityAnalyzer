import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { AccessibilityFinding, SourceLocation } from '../../core/models/analysis.models';
@Component({ standalone: false, selector: 'aa-findings-table', templateUrl: './findings-table.component.html', styleUrl: './findings-table.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class FindingsTableComponent {
  readonly findings = input.required<readonly AccessibilityFinding[]>();
  readonly filter = signal('');
  readonly visibleFindings = computed(() => { const q = this.filter().trim().toLowerCase(); return q ? this.findings().filter(item => `${item.ruleId} ${item.category} ${item.message} ${item.recommendation}`.toLowerCase().includes(q)) : [...this.findings()]; });
  updateFilter(event: Event): void { this.filter.set((event.target as HTMLInputElement).value); }
  locations(item: AccessibilityFinding): readonly SourceLocation[] { return item.locations?.length ? item.locations : [item.location]; }
  lineText(item: AccessibilityFinding): string {
    const lines = [...new Set(this.locations(item).map(location => location.line))].sort((a,b)=>a-b);
    if (lines.length === 0) return 'Unknown line';
    if (lines.length === 1) return `Line ${lines[0]}`;
    return `Lines ${lines.join(', ')}`;
  }
}
