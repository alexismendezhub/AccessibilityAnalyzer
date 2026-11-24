import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AccessibilityReport } from '../../core/models/analysis.models';
@Component({ standalone: false, selector: 'aa-analysis-summary', templateUrl: './analysis-summary.component.html', styleUrl: './analysis-summary.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class AnalysisSummaryComponent { readonly report = input.required<AccessibilityReport>(); }
