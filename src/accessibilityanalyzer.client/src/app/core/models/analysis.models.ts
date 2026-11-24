export type FindingStatus = 'Error' | 'PotentialIssue' | 'ManualReview' | 'Passed' | 'Dynamic';
export interface SourceLocation { sourceName: string; line: number; column: number; startIndex: number; length: number; }
export interface AccessibilityFinding { ruleId: string; category: string; status: FindingStatus; severity: string; confidence: number; message: string; evidence: string; recommendation: string; wcagCriterion: string; wcagLevel: string; section508Applicability: string; location: SourceLocation; locations?: SourceLocation[]; occurrences: number; groupKey?: string; safeFixAvailable: boolean; safeFixDescription?: string; }
export interface DynamicFinding { ruleId: string; category: string; severity: string; confidence: number; message: string; evidence: string; recommendation: string; occurrences: number; locations?: SourceLocation[]; }
export interface AccessibilityReport { sourceName: string; analyzedAt: string; findings: AccessibilityFinding[]; dynamicFindings: DynamicFinding[]; errors: number; potentialIssues: number; manualReviews: number; dynamicCount: number; }
export interface SourceArtifact { path: string; kind: string; containsHtml: boolean; dynamicFindings: DynamicFinding[]; htmlComponent: boolean; }
export interface SourceAnalysisResult { artifact: SourceArtifact; report: AccessibilityReport; correctedContent: string; appliedFixes: string[]; }
export interface SourceInput { name: string; content: string; }
export interface ProjectAccessibilityReport { rootPath: string; sources: SourceAnalysisResult[]; totalFiles: number; htmlCandidates: number; }
export interface RuntimeAnalysisResult { url: string; browserExecutable: string; capturedAt: string; report: AccessibilityReport; originalDom: string; correctedDom: string; appliedFixes: string[]; }
export interface SafeFixResult { html: string; appliedFixes: string[]; }
export interface RuleDefinition { ruleId: string; title: string; category: string; wcagCriterion: string; wcagLevel: string; section508Applicability: string; defaultStatus: FindingStatus; automatic: boolean; runtimeRecommended: boolean; summary: string; }
