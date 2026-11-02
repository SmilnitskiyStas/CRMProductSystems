"""Production API and Web rollout for category receiving. Never activates inventory or pricing.

Execute on the server from its isolated release folder after copy acceptance. Physical printing remains unavailable.
Private environment is frozen from container inspection, never printed.
"""
import pathlib,json,subprocess,os,sys,time,hashlib,tarfile,urllib.request,urllib.error,datetime
R=pathlib.Path(os.environ.get('SG_RELEASE_DIR','/home/administrator/shelfguard-releases/receipts-20261102'));os.umask(0o077)
mode=sys.argv[1];statefile=R/'state.json';state=json.loads(statefile.read_text()) if statefile.exists() else {}
baseline={x['Name'].lstrip('/'):x for x in json.loads((R/'before-private.json').read_text())}
APIS=['api_core','api_consumer','api_provider'];TARGETS=APIS+['web'];tag='shelfguard-api:receipts-20261102'
def save():statefile.write_text(json.dumps(state,indent=2))
def run(a,**kw):return subprocess.run(a,check=True,**kw)
def out(a):return subprocess.check_output(a,text=True,stderr=subprocess.PIPE).strip()
def inspect(n):return json.loads(out(['docker','inspect',n]))[0]
def sql(q):return out(['docker','exec','shelfguard_postgres','psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard','-c',q])
def unchanged():
 for name,b in baseline.items():assert inspect(name)['Id']==b['Id'],'Concurrent production container change: '+name
def compose(which,services):
 with (R/(which+'-compose.log')).open('a') as log:run(['docker','compose','-p','shelfguard','--project-directory',str(R),'-f',str(R/(which+'.private.json')),'up','-d','--no-build','--no-deps',*services],stdout=log,stderr=subprocess.STDOUT)
def healthy(name):
 for _ in range(50):
  i=inspect(name)
  if i['State'].get('Health',{}).get('Status')=='healthy':return
  if i['State']['Status']=='exited':break
  time.sleep(2)
 raise RuntimeError('Health failed: '+name)
def facts():
 result={}
 for table in ['product_stock','pos_transactions','pos_transaction_items','stock_receipts','stock_receipt_items']:
  query='SELECT md5((to_jsonb(t)-\'PricingArchiveRequired\'-\'ReceivingWorkJson\')::text) FROM '+table+' t ORDER BY "Id"'
  with (R/'fingerprint-errors-private.log').open('w') as log:
   p=subprocess.Popen(['docker','exec','shelfguard_postgres','psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard','-c',query],stdout=subprocess.PIPE,stderr=log);h=hashlib.sha256();rows=0
   while True:
    data=p.stdout.read(65536)
    if not data:break
    h.update(data);rows+=data.count(b'\n')
   assert p.wait()==0
  result[table]={'rows':rows,'sha256':h.hexdigest()}
 return result
def request(path,want=200,role=None):
 try:
  with urllib.request.urlopen('https://api.agrusystems.pp.ua'+path,timeout=15) as res:status=res.status;headers=res.headers
 except urllib.error.HTTPError as e:status=e.code;headers=e.headers
 assert status==want,(path,status,want)
 if role:assert headers.get('X-Served-By')==role,(path,headers.get('X-Served-By'))
 return status
def worker_probe():
 e=dict(x.split('=',1) for x in baseline['shelfguard_worker']['Config']['Env']);needed=['WORKER_API_EMAIL','WORKER_API_PASSWORD','INTERNAL_API_BASE_URL'];env=os.environ.copy()
 for key in needed:assert e.get(key);env[key]=e[key]
 script="const {getWorkerGrant}=require('/app/dist/services/worker-auth.js');(async()=>{const g=await getWorkerGrant();const root=process.env.INTERNAL_API_BASE_URL;const a=await fetch(root+'/api/pos/sales/pending-fiscalization',{headers:{Authorization:'Bearer '+g.accessToken}});const b=await fetch(root+'/api/auth/me',{headers:{Authorization:'Bearer '+g.accessToken}});if(a.status!==200||b.status!==401)throw Error('Worker boundary failed');console.log(JSON.stringify({grant:true,jobReadStatus:a.status,staffStatus:b.status,noJobExecuted:true}));})().catch(e=>{console.error(e.message);process.exit(1)});"
 command=['docker','run','--rm','--label','com.agru.p4f-production-probe=20261101','--network','host','--entrypoint','node']
 for key in needed:command+=['-e',key]
 result=json.loads(subprocess.check_output(command+[baseline['shelfguard_worker']['Image'],'-e',script],env=env,text=True,stderr=subprocess.PIPE));(R/'worker-acceptance.json').write_text(json.dumps(result,indent=2));return result
if mode=='prepare':
 unchanged();assert not state.get('prepared')
 with tarfile.open(R/'payload.tar.gz') as tar:
  assert all(not p.issym() and not p.islnk() and not pathlib.PurePosixPath(p.name).is_absolute() and '..' not in pathlib.PurePosixPath(p.name).parts for p in tar.getmembers());tar.extractall(R,filter='data')
 manifest=json.loads((R/'manifest.json').read_text())
 for name,want in manifest['files'].items():assert hashlib.sha256((R/name).read_bytes()).hexdigest()==want,name
 assert out(['docker','image','inspect','shelfguard-api:p4f-20261101','--format','{{.Id}}'])==baseline['shelfguard_api_core']['Image']
 (R/'Dockerfile.api').write_text('FROM shelfguard-api:p4f-20261101\nWORKDIR /app\nCOPY api/ /app/\nRUN rm -f /app/appsettings.Production.json /app/appsettings.Development.json /app/appsettings.Staging.json\nENTRYPOINT ["dotnet", "ShelfGuard.Api.dll"]\n')
 (R/'.dockerignore').write_text('*\n!api/\n!api/**\n!web/\n!web/**\n!Dockerfile.api\n!Dockerfile.web\n')
 with (R/'image-build.log').open('w') as log:run(['docker','build','--network','none','--pull=false','-f',str(R/'Dockerfile.api'),'-t',tag,str(R)],stdout=log,stderr=subprocess.STDOUT)
 image=out(['docker','image','inspect',tag,'--format','{{.Id}}']);state['imageId']=image
 webbase=baseline['shelfguard_web']['Config']['Image'];assert out(['docker','image','inspect',webbase,'--format','{{.Id}}'])==baseline['shelfguard_web']['Image']
 (R/'Dockerfile.web').write_text('FROM '+webbase+'\nWORKDIR /app\nRUN rm -rf /app/.next\nCOPY web/.next/ /app/.next/\nCOPY web/public/ /app/public/\n')
 with (R/'web-image-build.log').open('w')as log:run(['docker','build','--network','none','--pull=false','-f',str(R/'Dockerfile.web'),'-t','shelfguard-web:receipts-20261102',str(R)],stdout=log,stderr=subprocess.STDOUT)
 state['webImageId']=out(['docker','image','inspect','shelfguard-web:receipts-20261102','--format','{{.Id}}']);state['webBuildId']=(R/'web/.next/BUILD_ID').read_text().strip()
 for new,which in [(True,'candidate'),(False,'rollback')]:
  spec={'name':'shelfguard','services':{},'networks':{},'volumes':{}}
  for service in TARGETS:
   b=baseline['shelfguard_'+service];e=dict(x.split('=',1) for x in b['Config']['Env']);s={'container_name':'shelfguard_'+service,'image':(state['webImageId'] if service=='web'else image)if new else b['Image'],'environment':e,'restart':b['HostConfig']['RestartPolicy']['Name'] or 'no','entrypoint':['dotnet','ShelfGuard.Api.dll'],'command':[],'networks':{}}
   if service=='web':s['entrypoint']=b['Config'].get('Entrypoint')or [];s['command']=b['Config'].get('Cmd')or []
   if new and service in APIS:
    assert all(e.get('Erp__InventoryPosting__'+f,'false').lower()=='false' for f in ['Enabled','ReceiptsEnabled','TransfersEnabled','WriteOffsEnabled','PosEnabled','PosReturnsEnabled','ProductionEnabled'])
    assert all(e.get(k,'false').lower()=='false' for k in ['Pricing__PosAuthority__OnlineEnabled','Pricing__PosAuthority__OfflineEnabled','Pricing__Shadow__Enabled'])
    assert e.get('Migrations__RunOnStartup','false').lower()=='false' and e.get('SEED_ON_START','false').lower()=='false'
    assert not any(k.startswith(('Erp__InventoryPosting__Scopes__','Pricing__PosAuthority__Scopes__')) for k in e)
   for network in b['NetworkSettings']['Networks']:spec['networks'][network]={'external':True,'name':network};s['networks'][network]={}
   mounts=[]
   for m in b['Mounts']:
    if m['Type']=='volume':
     key='v'+str(len(spec['volumes']));spec['volumes'][key]={'external':True,'name':m['Name']};mounts.append({'type':'volume','source':key,'target':m['Destination'],'read_only':not m['RW']})
    else:mounts.append({'type':'bind','source':m['Source'],'target':m['Destination'],'read_only':not m['RW']})
   if mounts:s['volumes']=mounts
   ports=[{'target':int(port.split('/')[0]),'protocol':port.split('/')[1],'host_ip':p['HostIp'],'published':p['HostPort']} for port,ps in (b['HostConfig'].get('PortBindings') or {}).items() for p in ps]
   if ports:s['ports']=ports
   if b['Config'].get('Healthcheck'):s['healthcheck']={('start_period' if k=='StartPeriod' else k.lower()):str(v)+'ns' if k in ['Interval','Timeout','StartPeriod'] else v for k,v in b['Config']['Healthcheck'].items()}
   if b['HostConfig'].get('ExtraHosts'):s['extra_hosts']=b['HostConfig']['ExtraHosts']
   if b['HostConfig'].get('Memory'):s['mem_limit']=b['HostConfig']['Memory']
   host=b['HostConfig']
   mappings={'ReadonlyRootfs':'read_only','Privileged':'privileged','CapAdd':'cap_add','CapDrop':'cap_drop','SecurityOpt':'security_opt','PidsLimit':'pids_limit','MemoryReservation':'mem_reservation','MemorySwap':'memswap_limit','Dns':'dns','DnsOptions':'dns_opt','DnsSearch':'dns_search','ShmSize':'shm_size','Init':'init'}
   for source,target in mappings.items():
    if host.get(source) is not None and host[source] not in [[],0]:s[target]=host[source]
   if host.get('NanoCpus'):s['cpus']=host['NanoCpus']/1_000_000_000
   if host.get('MemorySwappiness') is not None:s['mem_swappiness']=host['MemorySwappiness']
   if b['Config'].get('User'):s['user']=b['Config']['User']
   if host.get('Ulimits'):s['ulimits']={u['Name']:{'soft':u['Soft'],'hard':u['Hard']} for u in host['Ulimits']}
   if host.get('LogConfig',{}).get('Type'):s['logging']={'driver':host['LogConfig']['Type'],'options':host['LogConfig'].get('Config',{})}
   if host.get('Tmpfs'):s['tmpfs']=[k+(':'+v if v else '') for k,v in host['Tmpfs'].items()]
   spec['services'][service]=s
  path=R/(which+'.private.json');path.write_text(json.dumps(spec,indent=2).replace('$','$$'))
  resolved=json.loads(out(['docker','compose','-p','shelfguard','-f',str(path),'config','--format','json']))
  for service in TARGETS:assert resolved['services'][service]['environment']==spec['services'][service]['environment'],'Credentials changed by Compose interpolation'
 state['prepared']=True;state['manifestSha256']=hashlib.sha256((R/'manifest.json').read_bytes()).hexdigest();save();print('Production API and Web candidate prepared; no cutover or authority activation',flush=True)
elif mode=='deploy':
 assert state.get('prepared') and not state.get('deployed');unchanged()
 acceptance=json.loads((R/'copy-acceptance.json').read_text());assert acceptance['passed'] and acceptance['backendPassed']>=151 and acceptance['migrationRuns']==2
 assert acceptance['manifestSha256']==state['manifestSha256'] and acceptance['imageId']==state['imageId']
 assert sql('SELECT count(*) FROM inventory_posting_scopes')=='0','Unexpected existing ERP activation'
 assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1')=='20261101104537_AddLabelPrinterProfiles'
 published=False
 try:
  run(['docker','stop','shelfguard_gateway','shelfguard_worker',*['shelfguard_'+s for s in TARGETS]],stdout=subprocess.DEVNULL)
  target=R/'database-quiesced-before.dump';assert not target.exists()
  with target.open('wb') as f:run(['docker','exec','shelfguard_postgres','pg_dump','-U','shelfguard','-d','shelfguard','-Fc'],stdout=f)
  with target.open('rb') as f:run(['docker','exec','-i','shelfguard_postgres','pg_restore','--file=/dev/null'],stdin=f,stdout=subprocess.DEVNULL)
  state['backup']={'path':str(target),'bytes':target.stat().st_size,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()};state['before']=facts();save()
  for file in ['upgrade.sql']:
   with (R/file).open('rb') as source,(R/(file+'.apply.log')).open('w') as log:
    run(['docker','exec','-i','shelfguard_postgres','psql','-X','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard'],stdin=source,stdout=log,stderr=subprocess.STDOUT)
  assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1')=='20261102102841_AddReceiptCategoryWork'
  assert sql("SELECT has_column_privilege('shelfguard_app','stock_receipts','ReceivingWorkJson','SELECT')AND has_column_privilege('shelfguard_app','stock_receipts','ReceivingWorkJson','UPDATE')")=='t'
  state['schemaApplied']=True;save()
  compose('candidate',TARGETS)
  for service in TARGETS:healthy('shelfguard_'+service)
  state['after']=facts();assert state['before']==state['after'],'Production facts changed during API-only deployment';worker_probe()
  published=True;state['publicProtocolExposed']=True;save()
  run(['docker','start','shelfguard_gateway','shelfguard_worker'],stdout=subprocess.DEVNULL)
  healthy('shelfguard_gateway');healthy('shelfguard_worker')
  state['deployed']=True;state['deployedAt']=datetime.datetime.now(datetime.timezone.utc).isoformat();save();print('API and Web cutover complete; all ERP and pricing authority flags off',flush=True)
 except BaseException:
  state['cutoverFailed']=True;state['publicProtocolExposed']=published;save()
  if published and sql('SELECT count(*) FROM stock_receipts WHERE "ReceivingWorkJson" IS NOT NULL')!='0':
   state['forwardRecoveryRequired']=True;save();run(['docker','start','shelfguard_gateway','shelfguard_worker'],stdout=subprocess.DEVNULL);raise
  compose('rollback',TARGETS);run(['docker','start','shelfguard_gateway','shelfguard_worker'],stdout=subprocess.DEVNULL)
  raise
elif mode=='verify':
 assert state.get('deployed');checks={}
 for path,want,role in [('/health/ready',200,None),('/api/pos/sync/catalog-v2',401,'core'),('/api/auth/me',401,'core'),('/api/erp/labels/templates/capabilities',401,'core'),('/api/erp/labels/printers',401,'core'),('/api/erp/labels/jobs',401,'core'),('/api/receipts/00000000-0000-0000-0000-000000000001/category-work',401,'core'),('/api/erp/receipts?store_id=00000000-0000-0000-0000-000000000001',401,'core'),('/api/consumer/profile',401,'consumer'),('/api/supplier-cabinet/profile',401,'provider'),('/api/internal/worker-session',404,None)]:checks[path]=request(path,want,role)
 services=[]
 for name,b in baseline.items():
  c=inspect(name)
  if name in ['shelfguard_'+s for s in APIS]:
   assert c['Image']==state['imageId'];e=dict(x.split('=',1) for x in c['Config']['Env'])
   assert all(e['Erp__InventoryPosting__'+f]=='false' for f in ['Enabled','ReceiptsEnabled','TransfersEnabled','WriteOffsEnabled','PosEnabled','PosReturnsEnabled','ProductionEnabled'])
   assert e['Pricing__PosAuthority__OnlineEnabled']==e['Pricing__PosAuthority__OfflineEnabled']==e['Pricing__Shadow__Enabled']=='false'
   assert not any(k.startswith(('Erp__InventoryPosting__Scopes__','Pricing__PosAuthority__Scopes__')) for k in e)
   assert {(m['Destination'],m.get('Name',m['Source'])) for m in b['Mounts']}=={(m['Destination'],m.get('Name',m['Source'])) for m in c['Mounts']}
   keys=['Privileged','ReadonlyRootfs','CapAdd','CapDrop','SecurityOpt','PidsLimit','Ulimits','LogConfig','Memory','MemoryReservation','MemorySwap','NanoCpus','ExtraHosts','Dns','DnsOptions','DnsSearch','ShmSize','Init']
   assert all(b['HostConfig'].get(k)==c['HostConfig'].get(k) for k in keys),'Runtime resource/security changed: '+name
   assert b['Config'].get('User')==c['Config'].get('User')
   assert dict(x.split('=',1) for x in b['Config']['Env'])==e,'Runtime environment changed: '+name
  elif name=='shelfguard_web':
   assert c['Image']==state['webImageId']
   assert dict(x.split('=',1)for x in c['Config']['Env'])==dict(x.split('=',1)for x in b['Config']['Env'])
   assert {(m['Destination'],m.get('Name',m['Source']))for m in b['Mounts']}=={(m['Destination'],m.get('Name',m['Source']))for m in c['Mounts']}
   assert all(b['HostConfig'].get(k)==c['HostConfig'].get(k)for k in ['Privileged','ReadonlyRootfs','CapAdd','CapDrop','SecurityOpt','PidsLimit','Ulimits','LogConfig','Memory','MemoryReservation','MemorySwap','NanoCpus','ExtraHosts','Dns','DnsOptions','DnsSearch','ShmSize','Init'])
   assert c['Config'].get('User')==b['Config'].get('User')
   assert out(['docker','exec',name,'cat','/app/.next/BUILD_ID'])==state['webBuildId']
  else:assert c['Id']==b['Id'] and c['Image']==b['Image'],'Untargeted service changed: '+name
  services.append({'name':name,'imageId':c['Image'],'image':c['Config']['Image'],'health':c['State'].get('Health',{}).get('Status'),'running':c['State']['Running']})
 assert sql('SELECT count(*) FROM inventory_posting_scopes')=='0';assert sql('SELECT count(*) FROM inventory_ledger_entries')=='0'
 assert sql("SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname='shelfguard_app'")=='t'
 assert sql("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='pos_pricing_snapshots'::regclass")=='t'
 assert sql("SELECT count(*) FROM pg_class WHERE relname IN ('label_template_revisions','label_store_assignments','label_test_jobs','label_test_job_events','label_test_job_receipts','label_test_job_uploads','label_printer_profile_revisions') AND relrowsecurity AND relforcerowsecurity")=='7'
 assert sql("SELECT bool_and(has_table_privilege('shelfguard_app', t,'SELECT') AND has_table_privilege('shelfguard_app', t,'INSERT')) FROM unnest(ARRAY['label_template_revisions','label_store_assignments','label_test_jobs','label_test_job_events','label_test_job_receipts','label_test_job_uploads','label_printer_profile_revisions']) t")=='t'
 assert sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1')=='20261102102841_AddReceiptCategoryWork'
 worker=worker_probe()
 for path in ['/login','/reset-password']:
  with urllib.request.urlopen('https://agrusystems.pp.ua'+path,timeout=20) as res:assert res.status==200
 result={'passed':True,'release':'receipts-20261102','deployedAt':state['deployedAt'],'api':'https://api.agrusystems.pp.ua','web':'https://agrusystems.pp.ua','webBuildId':state['webBuildId'],'schemaChanged':True,'latestMigration':'20261102102841_AddReceiptCategoryWork','physicalPrintingEnabled':False,'allPricingAndInventoryFlagsOff':True,'inventoryScopes':0,'ledgerEntries':0,'businessFactsUnchangedWhileQuiesced':state['before']==state['after'],'before':state['before'],'after':state['after'],'backup':state['backup'],'services':services,'http':checks,'worker':worker,'productionCanaryActivated':False,'manualClientInstallationRequired':True}
 (R/'deployment-report.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)
else:raise ValueError('Unknown mode')
