import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AccessibilityApiService } from '../../core/api/accessibility-api.service';
import { AccessibilityReport, ProjectAccessibilityReport, RuleDefinition, SourceInput } from '../../core/models/analysis.models';

type TabId='paste'|'project'|'runtime'|'rules';
@Component({ standalone: false, selector:'aa-analyzer-shell', templateUrl:'./analyzer-shell.component.html', styleUrl:'./analyzer-shell.component.scss', changeDetection:ChangeDetectionStrategy.OnPush })
export class AnalyzerShellComponent {
  private readonly api=inject(AccessibilityApiService);
  readonly activeTab=signal<TabId>('paste');
  readonly report=signal<AccessibilityReport|null>(null);
  readonly projectReport=signal<ProjectAccessibilityReport|null>(null);
  readonly projectSources=signal<ReadonlyArray<SourceInput>>([]);
  readonly rules=signal<RuleDefinition[]>([]);
  readonly globalStatus=signal('Ready.');
  readonly tabs:ReadonlyArray<{id:TabId;label:string}>=[{id:'paste',label:'Paste HTML'},{id:'project',label:'Project / files'},{id:'runtime',label:'Runtime URL'},{id:'rules',label:'Rule catalog'}];

  constructor(){this.api.rules().subscribe({next:r=>this.rules.set(r),error:()=>this.globalStatus.set('Rule catalog could not be loaded.')});}
  activate(id:TabId):void{this.activeTab.set(id);}
  onTabKeydown(event:KeyboardEvent,index:number):void{let next=index;if(event.key==='ArrowRight')next=(index+1)%this.tabs.length;else if(event.key==='ArrowLeft')next=(index-1+this.tabs.length)%this.tabs.length;else if(event.key==='Home')next=0;else if(event.key==='End')next=this.tabs.length-1;else return;event.preventDefault();const tab=this.tabs[next];this.activeTab.set(tab.id);queueMicrotask(()=>document.getElementById(`tab-${tab.id}`)?.focus());}
  showReport(report:AccessibilityReport|null):void{this.report.set(report);this.globalStatus.set(report?`Analysis complete for ${report.sourceName}.`:'Selected project file has no accessibility analysis report.');}
  showProject(report:ProjectAccessibilityReport):void{this.projectReport.set(report);}
  showSourceBatch(sources:ReadonlyArray<SourceInput>):void{this.projectSources.set(sources);}
  downloadJson():void{const value=this.activeTab()==='project'?(this.projectReport()??this.report()):this.report();if(!value)return;this.download(JSON.stringify(value,null,2),'accessibility-analysis.json','application/json');}
  downloadProjectSarif():void{const sources=this.projectSources();if(!sources.length)return;this.api.sarifSources(sources).subscribe({next:text=>this.download(text,'accessibility-analysis.sarif','application/sarif+json'),error:e=>this.globalStatus.set(e?.error?.error??'SARIF export failed.')});}
  private download(text:string,name:string,type:string):void{const url=URL.createObjectURL(new Blob([text],{type}));const link=document.createElement('a');link.href=url;link.download=name;link.click();URL.revokeObjectURL(url);}
}
