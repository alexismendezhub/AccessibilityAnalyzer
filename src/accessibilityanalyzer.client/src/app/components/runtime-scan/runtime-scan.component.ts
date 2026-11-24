import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { AccessibilityApiService } from '../../core/api/accessibility-api.service';
import { AccessibilityReport } from '../../core/models/analysis.models';
@Component({ standalone: false, selector: 'aa-runtime-scan', templateUrl: './runtime-scan.component.html', styleUrl: './runtime-scan.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class RuntimeScanComponent {
  private readonly api=inject(AccessibilityApiService);
  readonly analyzed=output<AccessibilityReport>();
  readonly url=signal('http://127.0.0.1:4200');
  readonly busy=signal(false);
  readonly status=signal('');
  readonly error=signal('');
  readonly originalDom=signal('');
  readonly correctedDom=signal('');
  readonly appliedFixes=signal<readonly string[]>([]);
  readonly resultReady=signal(false);

  updateUrl(e:Event):void{this.url.set((e.target as HTMLInputElement).value);this.error.set('');this.resultReady.set(false);}
  analyze():void{
    const value=this.url().trim();
    if(!/^https?:\/\//i.test(value)){this.error.set('Enter an absolute http or https URL.');return;}
    this.busy.set(true);this.error.set('');this.status.set('Capturing rendered DOM, applying safe corrections to the snapshot, and locating runtime warnings…');
    this.api.runtime(value).pipe(finalize(()=>this.busy.set(false))).subscribe({
      next:r=>{
        this.originalDom.set(r.originalDom);
        this.correctedDom.set(r.correctedDom);
        this.appliedFixes.set(r.appliedFixes);
        this.resultReady.set(true);
        this.analyzed.emit(r.report);
        this.status.set(`Runtime DOM captured with ${r.browserExecutable}. ${r.appliedFixes.length} automatic correction(s) applied to the displayed snapshot.`);
      },
      error:e=>{this.error.set(e?.error?.error??'Runtime analysis failed.');this.status.set('');this.resultReady.set(false);}
    });
  }
  async copyCorrected():Promise<void>{
    if(!this.resultReady())return;
    try{await navigator.clipboard.writeText(this.correctedDom());this.status.set('Corrected runtime DOM copied to the clipboard.');}
    catch{this.status.set('The browser did not allow clipboard access. Select the corrected DOM and copy it manually.');}
  }
}
