import { HttpClientModule } from '@angular/common/http';
import { NgModule, provideBrowserGlobalErrorListeners } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { AppRoutingModule } from './app-routing-module';
import { App } from './app';
import { AnalyzerShellComponent } from './components/analyzer-shell/analyzer-shell.component';
import { PasteHtmlComponent } from './components/paste-html/paste-html.component';
import { ProjectScanComponent } from './components/project-scan/project-scan.component';
import { RuntimeScanComponent } from './components/runtime-scan/runtime-scan.component';
import { AnalysisSummaryComponent } from './components/analysis-summary/analysis-summary.component';
import { DynamicFindingsTableComponent } from './components/dynamic-findings-table/dynamic-findings-table.component';
import { FindingsTableComponent } from './components/findings-table/findings-table.component';
import { SourceDiffComponent } from './components/source-diff/source-diff.component';

@NgModule({
  declarations: [
    App,
    AnalyzerShellComponent,
    PasteHtmlComponent,
    ProjectScanComponent,
    RuntimeScanComponent,
    AnalysisSummaryComponent,
    DynamicFindingsTableComponent,
    FindingsTableComponent,
    SourceDiffComponent,
  ],
  imports: [BrowserModule, HttpClientModule, AppRoutingModule],
  providers: [provideBrowserGlobalErrorListeners()],
  bootstrap: [App],
})
export class AppModule {}
