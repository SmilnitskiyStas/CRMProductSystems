"""Execute the verified GitHub artifact on the server, with backup and restored-copy rehearsal."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,tarfile

p=argparse.ArgumentParser();p.add_argument('--sha',required=True);p.add_argument('--payload-sha',required=True);a=p.parse_args()
R=Path(os.environ['SG_RELEASE_DIR']);assert R.parent==Path('/home/administrator/shelfguard-releases')and R.name.startswith('receipts-')
os.umask(0o077)
def run(args,log):
 with (R/log).open('w')as f:subprocess.run(args,check=True,stdout=f,stderr=subprocess.STDOUT)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
assert sha(R/'payload.tar.gz')==a.payload_sha
with tarfile.open(R/'payload.tar.gz')as tar:
 m=json.load(tar.extractfile('manifest.json'));assert m['sourceSha']==a.sha
 # Bootstrap only the already verified deployment script; prepare checks all other hashes.
 script=tar.extractfile('receipts-production.py').read();assert hashlib.sha256(script).hexdigest()==m['files']['receipts-production.py']
 (R/'receipts-production.py').write_bytes(script)
run(['python3',str(R/'receipts-production.py'),'prepare'],'ci-prepare.log')
backup=R/'before-rehearsal.dump';assert not backup.exists()
with backup.open('wb')as f:subprocess.run(['docker','exec','shelfguard_postgres','pg_dump','-U','shelfguard','-d','shelfguard','-Fc'],check=True,stdout=f)
with backup.open('rb')as f:subprocess.run(['docker','exec','-i','shelfguard_postgres','pg_restore','--file=/dev/null'],check=True,stdin=f,stdout=subprocess.DEVNULL)
run(['python3',str(R/'receipts-production-rehearsal.py')],'ci-rehearsal.log')
rehearsal=json.loads((R/'rehearsal-result.json').read_text());assert rehearsal['passed']and rehearsal['migrationRuns']==2
backend=json.loads((R/'backend-acceptance.json').read_text());assert backend['passed']and backend['backendPassed']>=4133
state=json.loads((R/'state.json').read_text())
acceptance={'passed':True,'sourceSha':a.sha,'backendPassed':backend['backendPassed'],'migrationRuns':2,'imageId':state['imageId'],'webImageId':state['webImageId'],'manifestSha256':sha(R/'manifest.json'),'rehearsal':rehearsal}
(R/'copy-acceptance.json').write_text(json.dumps(acceptance,indent=2))
run(['python3',str(R/'receipts-production.py'),'deploy'],'ci-deploy.log')
run(['python3',str(R/'receipts-production.py'),'verify'],'ci-verify.log')
report=json.loads((R/'deployment-report.json').read_text());assert report['passed']
print(json.dumps({'passed':True,'sourceSha':a.sha,'releaseDirectory':str(R),'deployedAt':report['deployedAt'],'webBuildId':report['webBuildId'],'latestMigration':report['latestMigration'],'backupVerified':True,'physicalPrintingPerformed':False}))
