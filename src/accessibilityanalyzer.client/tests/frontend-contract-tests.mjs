import test from 'node:test'; import assert from 'node:assert/strict'; import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL } from 'node:url'; import os from 'node:os';
const repo=path.resolve(import.meta.dirname,'../..');
const root=path.join(repo,'accessibilityanalyzer.client/src/app');
const files=[];function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const p=path.join(dir,e.name);if(e.isDirectory())walk(p);else files.push(p);}}walk(root);

test('Migrated Angular components use external templates/styles and clean-template NgModule integration',()=>{
  const components=files.filter(f=>f.endsWith('.component.ts'));assert.ok(components.length>=7);
  for(const file of components){const text=fs.readFileSync(file,'utf8');assert.match(text,/standalone:\s*false/);assert.match(text,/templateUrl\s*:/);assert.match(text,/styleUrl\s*:/);assert.ok(fs.existsSync(file.replace(/\.ts$/,'.html')));assert.ok(fs.existsSync(file.replace(/\.ts$/,'.scss')));}
  const module=fs.readFileSync(path.join(root,'app-module.ts'),'utf8');for(const name of ['AnalyzerShellComponent','PasteHtmlComponent','ProjectScanComponent','RuntimeScanComponent','AnalysisSummaryComponent','DynamicFindingsTableComponent','FindingsTableComponent'])assert.match(module,new RegExp(name));
  const main=fs.readFileSync(path.join(repo,'accessibilityanalyzer.client/src/main.ts'),'utf8');assert.match(main,/bootstrapModule\(AppModule/);
});

test('Project input retains all browser-selected files, sequential preview, and complete-project ZIP',()=>{
  const ts=fs.readFileSync(path.join(root,'components/project-scan/project-scan.component.ts'),'utf8');
  const html=fs.readFileSync(path.join(root,'components/project-scan/project-scan.component.html'),'utf8');
  const api=fs.readFileSync(path.join(root,'core/api/accessibility-api.service.ts'),'utf8');
  for(const token of [/file\.webkitRelativePath \|\| file\.name/,/loadedFiles/,/loaded\.filter\(item => item\.textPreviewable && !item\.analysisIgnored\)/,/createProjectZip/,/createBatches/,/firstValueFrom/])assert.match(ts,token);
  for(const token of [/hidden type="file"/,/Choose project folder/,/Choose project files/,/Choose project ZIP/,/Project structure/,/Show only modified files/,/Before and after/,/<aa-source-diff/,/Download complete project ZIP/,/Download modified files only/,/>Previous modified</,/>Next modified</])assert.match(html,token);
  for(const token of [/readProjectZip/,/buildProjectTree/,/isModified\(item\) && item\.analysis \? item\.analysis\.correctedContent : item\.file/])assert.match(ts,token);
  assert.doesNotMatch(html,/type="text"[^>]*(folder|directory|path)/i);assert.doesNotMatch(html,/C:\\/i);assert.doesNotMatch(api,/scan-path|sarif-path|scanPath|sarifPath/);
});

test('Complete project ZIP preserves paths and original blobs for unchanged files',()=>{
  const archive=fs.readFileSync(path.join(root,'core/files/project-archive.ts'),'utf8');
  for(const token of [/createProjectZip/,/normalizeArchivePath/,/0x04034b50/,/0x02014b50/,/0x06054b50/,/entry\.source\.arrayBuffer\(\)/])assert.match(archive,token);
});

test('Project ZIP reader inflates in the browser and refuses encrypted entries',()=>{
  const reader=fs.readFileSync(path.join(root,'core/files/zip-reader.ts'),'utf8');
  for(const token of [/DecompressionStream\('deflate-raw'\)/,/0x06054b50/,/0x06064b50/,/0x02014b50/,/password protected/])assert.match(reader,token);
});

test('Before and after view is a line diff with marked changes',()=>{
  const diff=fs.readFileSync(path.join(root,'core/diff/line-diff.ts'),'utf8');for(const token of [/buildSideBySideDiff/,/collapseUnchanged/,/function myers/])assert.match(diff,token);
  const view=fs.readFileSync(path.join(root,'components/source-diff/source-diff.component.html'),'utf8');for(const token of [/<del>/,/<ins>/,/<caption/,/scope="col"/,/Show whole file/])assert.match(view,token);
});

test('Paste HTML and runtime flows expose corrected outputs',()=>{
  const pasteTs=fs.readFileSync(path.join(root,'components/paste-html/paste-html.component.ts'),'utf8');const pasteHtml=fs.readFileSync(path.join(root,'components/paste-html/paste-html.component.html'),'utf8');
  assert.match(pasteTs,/analyzeAndFixHtml/);assert.match(pasteHtml,/Corrected HTML/);assert.match(pasteHtml,/readonly/);
  const runtimeTs=fs.readFileSync(path.join(root,'components/runtime-scan/runtime-scan.component.ts'),'utf8');const runtimeHtml=fs.readFileSync(path.join(root,'components/runtime-scan/runtime-scan.component.html'),'utf8');
  assert.match(runtimeTs,/correctedDom/);assert.match(runtimeHtml,/Corrected rendered DOM snapshot/);
});

test('Accessibility shell exposes keyboard tab behavior and warning line detail',()=>{
  const shell=fs.readFileSync(path.join(root,'components/analyzer-shell/analyzer-shell.component.ts'),'utf8');for(const key of ['ArrowRight','ArrowLeft','Home','End'])assert.match(shell,new RegExp(key));
  const findings=fs.readFileSync(path.join(root,'components/findings-table/findings-table.component.html'),'utf8');assert.match(findings,/Source line\(s\)/);assert.match(findings,/All positions/);
});

test('Uploaded .NET 10 Angular template remains the startup authority',()=>{
  const pkg=JSON.parse(fs.readFileSync(path.join(repo,'accessibilityanalyzer.client/package.json'),'utf8'));assert.equal(pkg.name,'accessibilityanalyzer.client');assert.equal(pkg.scripts.start,'ng serve --ssl --host=127.0.0.1');assert.equal(pkg.scripts.prestart,'node aspnetcore-https');assert.equal(pkg.dependencies['run-script-os'],undefined);
  const angular=JSON.parse(fs.readFileSync(path.join(repo,'accessibilityanalyzer.client/angular.json'),'utf8'));const serve=angular.projects['accessibilityanalyzer.client'].architect.serve.options;assert.equal(serve.port,49230);assert.equal(serve.proxyConfig,'src/proxy.conf.js');assert.equal(serve.sslCert,'.aspnetcore-https/localhost.pem');assert.equal(serve.sslKey,'.aspnetcore-https/localhost.key');
  const esproj=fs.readFileSync(path.join(repo,'accessibilityanalyzer.client/accessibilityanalyzer.client.esproj'),'utf8');assert.match(esproj,/Microsoft\.VisualStudio\.JavaScript\.Sdk\/1\.0\.6165494/);assert.match(esproj,/<StartupCommand>npm start<\/StartupCommand>/);
  const server=fs.readFileSync(path.join(repo,'AccessibilityAnalyzer.Server/AccessibilityAnalyzer.Server.csproj'),'utf8');assert.ok(server.includes('<SpaRoot>..\\accessibilityanalyzer.client</SpaRoot>'));assert.match(server,/<SpaProxyServerUrl>https:\/\/localhost:49230<\/SpaProxyServerUrl>/);assert.ok(server.includes('..\\accessibilityanalyzer.client\\accessibilityanalyzer.client.esproj'));
  const launch=JSON.parse(fs.readFileSync(path.join(repo,'AccessibilityAnalyzer.Server/Properties/launchSettings.json'),'utf8'));assert.equal(launch.profiles.http.applicationUrl,'http://localhost:5206');assert.equal(launch.profiles.https.applicationUrl,'https://localhost:7037;http://localhost:5206');
  const proxy=fs.readFileSync(path.join(repo,'accessibilityanalyzer.client/src/proxy.conf.js'),'utf8');assert.match(proxy,/ASPNETCORE_HTTPS_PORT/);assert.match(proxy,/ASPNETCORE_URLS/);assert.match(proxy,/"\/api"/);assert.match(proxy,/"\/health"/);assert.doesNotMatch(proxy,/5088|4200/);
  assert.equal(fs.existsSync(path.join(repo,'AccessibilityAnalyzer.slnLaunch')),false);
});

// Behavior of the file classifier itself (Node strips the TypeScript types when importing it).
// Imported as a .mts copy: the client package is not "type": "module", and an ES module in a .ts file there makes Node warn.
const tsCopies=fs.mkdtempSync(path.join(os.tmpdir(),'aa-client-modules-'));
async function importClientModule(relative){const copy=path.join(tsCopies,path.basename(relative).replace(/\.ts$/,'.mts'));fs.copyFileSync(path.join(root,relative),copy);return import(pathToFileURL(copy).href);}
const classifier=await importClientModule('core/files/project-file-classifier.ts');
const folderFilter=await importClientModule('core/files/folder-filter.ts');

test('Binary files with text-like or no extensions are never read as text',async()=>{
  const {isTextFile,classifyByName,bytesLookLikeText}=classifier;
  assert.equal(classifyByName('encoder_model_fp16.onnx',''),'binary');
  assert.equal(classifyByName('app.dll',''),'binary');
  assert.equal(classifyByName('logo.svg','image/svg+xml'),'text');
  assert.equal(classifyByName('index.html','text/html'),'text');
  assert.equal(classifyByName('terraform-provider-azurerm_v4.81.0_x5',''),'unknown');
  assert.equal(bytesLookLikeText(new Uint8Array([0x3c,0x70,0x3e,0x0a])),true);
  assert.equal(bytesLookLikeText(new Uint8Array([0x30,0x82,0x01,0x00,0x0a])),false);
  const elf=new File([new Uint8Array([0x7f,0x45,0x4c,0x46,2,1,1,0,0,0,0,0,0,0,0,0])],'terraform-provider-azurerm_v4.81.0_x5');
  assert.equal(await isTextFile(elf,elf.name,''),false);
  const der=new File([new Uint8Array([0x30,0x82,0x03,0x0d,0x30,0x82,0x01,0xf5,0xa0,0x03,0x02,0x01,0x02,0x02,0x00])],'root.cer');
  assert.equal(await isTextFile(der,der.name,''),false);
  const pem=new File(['-----BEGIN CERTIFICATE-----\nMIIBszCCAVmgAwIBAgIU\n-----END CERTIFICATE-----\n'],'root.cer');
  assert.equal(await isTextFile(pem,pem.name,''),true);
  const page=new File(['<img src="a.png">'],'index.html',{type:'text/html'});
  assert.equal(await isTextFile(page,page.name,page.type),true);
  const tsVideo=new File([new Uint8Array([0x47,0x40,0x11,0x10,0x00,0x42,0xf0,0x25])],'clip.ts');
  assert.equal(await isTextFile(tsVideo,tsVideo.name,''),false);
  const empty=new File([],'empty.html');
  assert.equal(await isTextFile(empty,empty.name,''),true);
});

test('Generated and tool folders are not analyzed, by folder name only',()=>{
  const {isInIgnoredFolder}=classifier;
  assert.equal(isInIgnoredFolder('app/azure/terraform/dev/.terraform/providers/x'),true);
  assert.equal(isInIgnoredFolder('app/node_modules/pkg/index.js'),true);
  assert.equal(isInIgnoredFolder('app/src/Bin/readme.md'),true);
  assert.equal(isInIgnoredFolder('app/src/build'),false);
  assert.equal(isInIgnoredFolder('app/src/app/page.html'),false);
});

test('Large projects and large files are bounded before anything is read in full',()=>{
  const {maxSourceBytes,maxSourceCharacters,maxAnalysisFiles,sniffBytes}=classifier;
  assert.ok(maxSourceBytes>=maxSourceCharacters);assert.ok(maxAnalysisFiles>=10000);assert.ok(sniffBytes<=64*1024);
  const ts=fs.readFileSync(path.join(root,'components/project-scan/project-scan.component.ts'),'utf8');
  for(const token of [/await Promise\.all\(kept\.map\(item => this\.describeFile\(item\)\)\)/,/isTextFile\(selected\.file/,/item\.file\.size <= maxSourceBytes/,/current\.file\.size > maxSourceBytes/,/candidates\.length > maxAnalysisFiles/])assert.match(ts,token);
  assert.doesNotMatch(ts,/candidates\.length > 2000/);
});

test('Skipped folders are never loaded, matched by folder name at any level and ignoring case',()=>{
  const {defaultSkippedFolders,skippableFolders,parseCustomFolders,skippedFolderSet,isInSkippedFolder,filterSkippedFolders}=folderFilter;
  for(const name of ['node_modules','bin','obj','.git','.vs','dist','.angular'])assert.ok(defaultSkippedFolders.includes(name),name);
  for(const name of ['packages','out'])assert.ok(!defaultSkippedFolders.includes(name)&&skippableFolders.some(f=>f.name===name),name);
  const skip=skippedFolderSet(defaultSkippedFolders,parseCustomFolders('App_Data'));
  assert.equal(isInSkippedFolder('SampleApp/src/SampleApp.Server/BIN/Debug/app.dll',skip),true);
  assert.equal(isInSkippedFolder('SampleApp/src/SampleApp.client/node_modules/x/index.js',skip),true);
  assert.equal(isInSkippedFolder('SampleApp/src/SampleApp.Server/App_Data/speech-models/model.onnx',skip),true);
  assert.equal(isInSkippedFolder('SampleApp/src/app/obj.html',skip),false);
  assert.equal(isInSkippedFolder('SampleApp/src/app/bin',skip),false);
  assert.equal(isInSkippedFolder('SampleApp/src/app/page.html',new Set()),false);
  const {kept,skippedFiles,byFolder}=filterSkippedFolders([{path:'p/node_modules/a.js'},{path:'p/node_modules/b.js'},{path:'p/src/bin/x.dll'},{path:'p/src/index.html'}],skip);
  assert.deepEqual(kept.map(k=>k.path),['p/src/index.html']);assert.equal(skippedFiles,3);assert.equal(byFolder.get('node_modules'),2);assert.equal(byFolder.get('bin'),1);
});

test('Custom folder names are plain names only',()=>{
  const {parseCustomFolders,maxCustomFolders}=folderFilter;
  assert.deepEqual(parseCustomFolders(' App_Data, logs;temp\nlogs , LOGS '),['App_Data','logs','temp']);
  assert.deepEqual(parseCustomFolders('C:\\Users\\me, ../secret, a/b, *, ., .., ok'),['ok']);
  assert.equal(parseCustomFolders(Array.from({length:80},(_,i)=>'f'+i).join(',')).length,maxCustomFolders);
});

test('Project input offers the folder-skip controls and applies them before loading',()=>{
  const ts=fs.readFileSync(path.join(root,'components/project-scan/project-scan.component.ts'),'utf8');
  const html=fs.readFileSync(path.join(root,'components/project-scan/project-scan.component.html'),'utf8');
  for(const token of [/filterSkippedFolders\(selection, this\.skipSet\(\)\)/,/kept\.map\(item => this\.describeFile\(item\)\)/,/localStorage\.setItem/,/resetSkipped/])assert.match(ts,token);
  for(const token of [/<fieldset/,/<legend[^>]*>Folders to skip<\/legend>/,/type="checkbox"/,/\[for\]="'skip-' \+ folder\.name"/,/for="skip-custom"/,/Restore defaults/,/skipSummary\(\)/])assert.match(html,token);
});

test('Shell has no logo or personal name and only points aria-controls at the rendered panel',()=>{
  const html=fs.readFileSync(path.join(root,'components/analyzer-shell/analyzer-shell.component.html'),'utf8');
  assert.doesNotMatch(html,/Browser-upload/i);
  assert.doesNotMatch(html,/<img|app-owner/);
  assert.match(html,/\[attr\.aria-controls\]="activeTab\(\)===tab\.id\?'panel-'\+tab\.id:null"/);
  const pub=path.join(root,'../../public');
  for(const file of ['brand','favicon.ico'])assert.ok(!fs.existsSync(path.join(pub,file)),file);
  const index=fs.readFileSync(path.join(root,'../index.html'),'utf8');assert.doesNotMatch(index,/rel="icon"|brand\//);
  const styles=fs.readFileSync(path.join(root,'../styles.css'),'utf8');for(const token of [/--brand-teal: #0f7f88/,/--brand-navy: #0b1b33/,/--bs-primary: var\(--brand-teal\)/])assert.match(styles,token);
});

test('Runtime URL shows the captured DOM before and after the safe corrections',()=>{
  const ts=fs.readFileSync(path.join(root,'components/runtime-scan/runtime-scan.component.ts'),'utf8');
  const html=fs.readFileSync(path.join(root,'components/runtime-scan/runtime-scan.component.html'),'utf8');
  const models=fs.readFileSync(path.join(root,'core/models/analysis.models.ts'),'utf8');
  assert.match(models,/originalDom: string; correctedDom: string;/);
  assert.match(ts,/this\.originalDom\.set\(r\.originalDom\)/);
  assert.match(html,/<aa-source-diff \[before\]="originalDom\(\)" \[after\]="correctedDom\(\)"/);
});
