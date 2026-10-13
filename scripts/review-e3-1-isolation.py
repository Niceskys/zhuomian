"""Read-only branch metadata/source review; writes only the owned JSON receipt. Never merges or runs E2 code."""
import subprocess,re,pathlib,json,tempfile,hashlib
root=pathlib.Path(__file__).resolve().parents[1]
def git(*args):return subprocess.check_output(['git',*args],cwd=root,text=True)
def read(ref,path):return git('show',f'{ref}:{path}')
def paths(ref):return git('ls-tree','-r','--name-only',ref).splitlines()
def declarations(text):
 return re.findall(r'^Project\("\{[^}]+\}"\) = "([^"]+)", "([^"]+)", "\{([^}]+)\}"',text,re.M)
def projects(text):
 rows=declarations(text)
 assert len({x[2].upper() for x in rows})==len(rows),'Duplicate solution GUID'
 actual=[(name,path.replace('\\','/'),guid.upper()) for name,path,guid in rows if path.endswith('.csproj')]
 assert len({x[0].lower() for x in actual})==len(actual),'Duplicate project name'
 assert len({x[1].lower() for x in actual})==len(actual),'Duplicate project path'
 return actual
base='origin/main'
snapshot='b548c555a794d8972efd1ec00306f41350477a29'
ours=projects((root/'Zhuomian.sln').read_text(encoding='utf-8-sig'))
common=projects(read(base,'Zhuomian.sln'))
common_paths={x[1].lower() for x in common}
added=[x for x in ours if x[1].lower() not in common_paths]
our_adr_numbers=[pathlib.Path(p).name.split('-')[0] for p in git('ls-files','docs/ADR').splitlines() if re.match(r'docs/ADR/\d{4}-',p)]
assert len(our_adr_numbers)==len(set(our_adr_numbers)),'Duplicate local ADR number'
our_adrs=set(our_adr_numbers)
base_adrs={pathlib.Path(p).name.split('-')[0] for p in paths(base) if re.match(r'docs/ADR/\d{4}-',p)}
assert our_adrs-base_adrs=={'0008'},'Launch ADR allocation is not isolated'
report={'schemaVersion':1,'reviewedPr':34,'previousVerifiedHead':snapshot,'main':git('rev-parse',base).strip(),'e3AddedProjects':[dict(zip(['name','path','guid'],row)) for row in added],'e3AddedAdr':'0008-controlled-resource-launch.md','branches':[]}
for n in (31,32,33):
 ref=f'refs/review/e3-1/pr{n}'
 theirs=projects(read(ref,'Zhuomian.sln'))
 assert not ({x[0].lower() for x in added}&{x[0].lower() for x in theirs}),'Cross-branch name collision'
 assert not ({x[1].lower() for x in added}&{x[1].lower() for x in theirs}),'Cross-branch path collision'
 declared_ids={x[2].upper() for x in declarations(read(ref,'Zhuomian.sln'))}
 assert not ({x[2] for x in added}&declared_ids),'Cross-branch GUID collision (including solution folders)'
 adr_files=[p for p in paths(ref) if re.match(r'docs/ADR/\d{4}-',p)]
 nums=[pathlib.Path(p).name.split('-')[0] for p in adr_files]
 assert len(nums)==len(set(nums)),'E2 ADR number collision'
 assert not ((our_adrs-base_adrs)&set(nums)),'Cross-branch ADR collision'
 # Only workflow text is merged in private temporary files. Never execute E2 steps or apply a branch merge.
 with tempfile.TemporaryDirectory(prefix='e3-1-ci-review-') as directory:
  files=[pathlib.Path(directory)/x for x in ['e3.yml','base.yml','e2.yml']]
  for p,s in zip(files,[(root/'.github/workflows/ci.yml').read_text(),read(base,'.github/workflows/ci.yml'),read(ref,'.github/workflows/ci.yml')]):p.write_text(s)
  result=subprocess.run(['git','merge-file','-p',*map(str,files)],capture_output=True,text=True)
  assert result.returncode==0,f'CI three-way text conflict with PR #{n}'
  merged=result.stdout
  assert merged.count('- name: Restore and test .NET solution')==1
  assert merged.count('name: launch-platform')==1
  assert merged.count('name: test-results')==1
  assert 'Publish native engineering executable' in merged
  if n==33:assert 'Publish and exercise WPF native candidate' in merged
 report['branches'].append({'pr':n,'head':git('rev-parse',ref).strip(),'addedAdrs':[p for p in adr_files if pathlib.Path(p).name.split('-')[0] not in base_adrs],'projectNameCollisions':[],'projectPathCollisions':[],'projectGuidCollisions':[],'adrNumberCollisions':[],'workflowThreeWayTextMergeExitCode':result.returncode,'mergedWorkflowSha256':hashlib.sha256(merged.encode()).hexdigest()})
# Verify every tracked C# source/test at the last verified head survives, allowing only namespace relocation.
cs=[p for p in paths(snapshot) if p.endswith('.cs')]
count=0
for old in cs:
 new=old.replace('src/Zhuomian.Platform.Windows/','src/Zhuomian.Launching.Windows/').replace('tests/Zhuomian.Platform.Windows.Tests/','tests/Zhuomian.Launching.Windows.Tests/')
 expected=read(snapshot,old).replace('Zhuomian.Platform.Windows.Launching','Zhuomian.Launching.Windows').replace('Zhuomian.Platform.Windows.Tests','Zhuomian.Launching.Windows.Tests')
 assert (root/new).read_text(encoding='utf-8-sig')==expected,f'Non-namespace C# change: {old}'
 count+=1
assert (root/'SECURITY.md').read_text()==read(snapshot,'SECURITY.md')
report.update({'comparedCSharpFileCount':count,'allCSharpChangesLimitedToNamespaceRelocation':True,'securityBaselineUnchanged':True,'reviewScope':'Metadata and workflow text only; no E2 production code, execution or merge applied. Future solution/docs text integration still needs review; this is not E2 runtime acceptance.'})
output=root/'docs/evidence/e3-1-branch-isolation.json'
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
