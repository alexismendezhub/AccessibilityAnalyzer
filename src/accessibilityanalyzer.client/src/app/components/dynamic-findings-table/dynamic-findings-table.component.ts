import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DynamicFinding, SourceLocation } from '../../core/models/analysis.models';
@Component({ standalone: false, selector: 'aa-dynamic-findings-table', templateUrl: './dynamic-findings-table.component.html', styleUrl: './dynamic-findings-table.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class DynamicFindingsTableComponent {
  readonly findings = input.required<readonly DynamicFinding[]>();
  locations(item: DynamicFinding): readonly SourceLocation[] { return item.locations ?? []; }
  lineText(item: DynamicFinding): string {
    const lines = [...new Set(this.locations(item).map(location => location.line))].sort((a,b)=>a-b);
    if (!lines.length) return 'Line unavailable';
    if (lines.length === 1) return `Line ${lines[0]}`;
    return `Lines ${lines.join(', ')}`;
  }
}
