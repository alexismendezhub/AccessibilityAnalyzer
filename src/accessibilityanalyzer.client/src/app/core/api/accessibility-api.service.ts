import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AccessibilityReport, RuleDefinition, RuntimeAnalysisResult, SafeFixResult, SourceAnalysisResult, SourceInput } from '../models/analysis.models';

@Injectable({ providedIn: 'root' })
export class AccessibilityApiService {
  private readonly http = inject(HttpClient);
  analyzeHtml(html: string, sourceName = 'pasted-html'): Observable<AccessibilityReport> { return this.http.post<AccessibilityReport>('/api/analyze-html', { html, sourceName }); }
  analyzeAndFixHtml(html: string, sourceName = 'pasted-html', preferredLanguage?: string): Observable<SourceAnalysisResult> { return this.http.post<SourceAnalysisResult>('/api/analyze-and-fix-html', { html, sourceName, preferredLanguage }); }
  analyzeSources(sources: ReadonlyArray<SourceInput>): Observable<SourceAnalysisResult[]> { return this.http.post<SourceAnalysisResult[]>('/api/analyze-sources', { sources }); }
  runtime(url: string): Observable<RuntimeAnalysisResult> { return this.http.post<RuntimeAnalysisResult>('/api/runtime', { url }); }
  applySafeFixes(html: string, preferredLanguage?: string): Observable<SafeFixResult> { return this.http.post<SafeFixResult>('/api/apply-safe-fixes', { html, preferredLanguage }); }
  rules(): Observable<RuleDefinition[]> { return this.http.get<RuleDefinition[]>('/api/rules'); }
  sarifSources(sources: ReadonlyArray<SourceInput>): Observable<string> { return this.http.post('/api/sarif-sources', { sources }, { responseType: 'text' }); }
}
