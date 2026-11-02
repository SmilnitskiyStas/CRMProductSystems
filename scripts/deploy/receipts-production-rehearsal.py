import pathlib,subprocess,json,time,hashlib,os
R=pathlib.Path(os.environ.get('SG_RELEASE_DIR','/home/administrator/shelfguard-releases/receipts-20261102'))
os.umask(0o077)
DB='sg-receipts-rehearsal-db-20261101';API='sg-receipts-rehearsal-api-20261101';NET='sg-receipts-rehearsal-20261101'
def run(a,**kw):return subprocess.run(a,check=True,**kw)
def out(a):return subprocess.check_output(a,text=True,stderr=subprocess.PIPE).strip()
def sql(s):return out(['docker','exec',DB,'psql','-X','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard','-Atc',s])
def streamed_fingerprint(table,db):
 query='SELECT md5((to_jsonb(t)-\'PricingArchiveRequired\')::text) FROM '+table+' t ORDER BY "Id";'
 with (R/'fingerprint-errors.log').open('w') as errors:
  p=subprocess.Popen(['docker','exec',db,'psql','-X','-qAt','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard','-c',query],stdout=subprocess.PIPE,stderr=errors)
  digest=hashlib.sha256();rows=0
  while True:
   chunk=p.stdout.read(65536)
   if not chunk:break
   digest.update(chunk);rows+=chunk.count(b'\n')
  assert p.wait()==0,'Fingerprint query failed; see private fingerprint-errors.log'
 return {'rows':rows,'digest':digest.hexdigest()}

def fingerprints():
 return {t:streamed_fingerprint(t,DB) for t in ['product_stock','pos_transactions','pos_transaction_items']}
created=[]
try:
 run(['docker','network','create','--internal','--label','agru.release=receipts-20261102',NET],stdout=subprocess.DEVNULL);created.append(('network',NET))
 run(['docker','run','-d','--label','agru.release=receipts-20261102','--name',DB,'--network',NET,'--memory','384m','--memory-swap','768m','-e','POSTGRES_USER=shelfguard','-e','POSTGRES_PASSWORD=synthetic-receipts-rehearsal-only','-e','POSTGRES_DB=shelfguard','postgres:16-alpine','-c','shared_buffers=64MB','-c','max_connections=25'],stdout=subprocess.DEVNULL);created.append(('container',DB))
 for n in range(60):
  if subprocess.run(['docker','exec',DB,'pg_isready','-h','127.0.0.1','-U','shelfguard'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0:break
  time.sleep(1)
 with (R/'before-rehearsal.dump').open('rb') as f,(R/'restore-rehearsal.log').open('w') as log:
  run(['docker','exec','-i',DB,'pg_restore','--exit-on-error','--no-owner','--no-privileges','-U','shelfguard','-d','shelfguard'],stdin=f,stdout=log,stderr=subprocess.STDOUT)
 before=fingerprints();assert sql('SELECT count(*) FROM inventory_posting_scopes;')=='0'
 sql("CREATE ROLE shelfguard_app LOGIN PASSWORD 'synthetic-receipts-runtime-only' NOSUPERUSER NOBYPASSRLS; GRANT CONNECT ON DATABASE shelfguard TO shelfguard_app; GRANT USAGE ON SCHEMA public TO shelfguard_app; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO shelfguard_app; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO shelfguard_app;")
 for n in [1,2]:
  with (R/'upgrade.sql').open('rb') as f,(R/f'migration-rehearsal-{n}.log').open('w') as log:
   run(['docker','exec','-i',DB,'psql','-X','-v','ON_ERROR_STOP=1','-U','shelfguard','-d','shelfguard'],stdin=f,stdout=log,stderr=subprocess.STDOUT)
 assert before==fingerprints(),'Existing stock or POS data changed'
 latest=sql('SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1;');assert latest=='20261102102841_AddReceiptCategoryWork'
 assert sql('SELECT count(*) FROM pos_pricing_snapshots;')=='0'
 assert sql('SELECT count(*) FROM inventory_ledger_entries;')=='0'
 assert sql('SELECT count(*) FROM pos_transactions WHERE "PricingArchiveRequired";')=='0'
 assert sql("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='pos_pricing_snapshots'::regclass;")=='t'
 assert sql("SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname='shelfguard_app';")=='t'
 import secrets
 secret=secrets.token_urlsafe(48)
 env={'ASPNETCORE_ENVIRONMENT':'Production','ASPNETCORE_URLS':'http://+:5100','API_ROLE':'core','Migrations__RunOnStartup':'false','SEED_ON_START':'false','ConnectionStrings__DefaultConnection':f'Host={DB};Database=shelfguard;Username=shelfguard_app;Password=synthetic-receipts-runtime-only','Jwt__Secret':secret,'Jwt__Issuer':'rehearsal','Jwt__Audience':'rehearsal','Pricing__Shadow__Enabled':'false','Pricing__PosAuthority__OnlineEnabled':'false','Pricing__PosAuthority__OfflineEnabled':'false','Logging__LogLevel__Default':'Warning'}
 for flag in ['Enabled','PosEnabled','ReceiptsEnabled','TransfersEnabled','WriteOffsEnabled','PosReturnsEnabled','ProductionEnabled']:env['Erp__InventoryPosting__'+flag]='false'
 cmd=['docker','run','-d','--label','agru.release=receipts-20261102','--name',API,'--network',NET,'--memory','512m','--memory-swap','1g']
 for k,v in env.items():cmd+=['-e',k+'='+v]
 run(cmd+['shelfguard-api:receipts-20261102'],stdout=subprocess.DEVNULL);created.append(('container',API))
 checks={}
 for n in range(75):
  p=subprocess.run(['docker','exec',API,'curl','-s','-o','/dev/null','-w','%{http_code}','http://127.0.0.1:5100/health/ready'],capture_output=True,text=True)
  if p.returncode==0 and p.stdout=='200':break
  time.sleep(2)
 else:raise RuntimeError('Rehearsal API did not become ready')
 for path,expected in [('/health/live',200),('/health/ready',200),('/api/auth/me',401),('/api/production/orders',401),('/api/erp/pricing/history/channels',401),('/api/erp/labels/templates/capabilities',401),('/api/erp/labels/printers',401),('/api/erp/labels/jobs',401)]:
  status=int(out(['docker','exec',API,'curl','-s','-o','/dev/null','-w','%{http_code}','http://127.0.0.1:5100'+path]));assert status==expected,(path,status);checks[path]=status
 # Synthetic receiving staff and documents exist only in this isolated restored copy.
 import uuid,base64,hmac,concurrent.futures
 compact=lambda v:json.dumps(v,ensure_ascii=False,separators=(',',':'))
 tenant='388a3537-9d82-4281-9d7e-54eda6279776';location='95427b1a-e6eb-4d06-ab69-4f7f2f95c018'
 template=sql(f"SELECT \"Id\" FROM users WHERE \"TenantId\"='{tenant}' AND \"IsActive\" LIMIT 1");assert template
 tokens=[];uids=[]
 for index in range(2):
  uid=str(uuid.uuid4());uids.append(uid);sid=str(uuid.uuid4());now=int(time.time())
  data={'Id':uid,'TenantId':tenant,'Email':f'receiving-copy-{uid}@example.invalid','FullName':f'Synthetic receiving {index+1}','Phone':None,'PasswordHash':'synthetic-not-a-login-secret','AuthVersion':0,'Role':'store_manager','StoreId':location,'LegalEntityId':None,'TelegramChatId':None,'PushToken':None,'IsActive':True,'FailedLoginAttempts':0,'LockoutUntil':None,'PosPinHash':None,'PosPinSetAt':None,'PosPinLockoutUntil':None,'TempPasswordExpiresAt':None,'PasswordResetTokenHash':None,'PasswordResetExpiresAt':None,'PasswordResetRequestedAt':None,'TotpSecret':'JBSWY3DPEHPK3PXP','TotpEnabled':True,'TotpRecoveryCodes':None,'TotpLastTimestep':None,'Permissions':{'receipts':True,'stock':True},'ProviderRoleId':None,'SupplierRoleId':None,'TenantRoleId':None}
  sql(f"INSERT INTO users SELECT (jsonb_populate_record(NULL::users,to_jsonb(u)||'{compact(data)}'::jsonb)).* FROM users u WHERE u.\"Id\"='{template}'; INSERT INTO user_locations (\"Id\",\"TenantId\",\"UserId\",\"LocationId\",\"CreatedAt\") VALUES ('{uuid.uuid4()}','{tenant}','{uid}','{location}',now());")
  sql(f"INSERT INTO refresh_tokens (\"Id\",\"UserId\",\"SessionId\",\"AuthVersion\",\"MfaVerifiedAt\",\"TokenHash\",\"ExpiresAt\",\"CreatedAt\") VALUES ('{uuid.uuid4()}','{uid}','{sid}',0,to_timestamp({now}),'{hashlib.sha256(os.urandom(32)).hexdigest()}',now()+interval '1 hour',now());")
  claims={'sub':uid,'email':data['Email'],'role':'store_manager','tenant_id':tenant,'sid':sid,'auth_ver':'0','mfa_at':str(now),'iss':'rehearsal','aud':'rehearsal','exp':now+1800}
  b64=lambda b:base64.urlsafe_b64encode(b).rstrip(b'=')
  proof=b64(compact({'alg':'HS256','typ':'JWT'}).encode())+b'.'+b64(compact(claims).encode())
  tokens.append((proof+b'.'+b64(hmac.new(secret.encode(),proof,hashlib.sha256).digest())).decode())
 def http(path,body=None,want=200,actor=0,method=None):
  config='header = "Authorization: Bearer '+tokens[actor]+'"\n'
  command=['docker','exec','-i',API,'curl','--silent','--show-error','--config','-','-w','\n%{http_code}','http://127.0.0.1:5100'+path]
  if method:command+=['-X',method]
  if body is not None:config+='header = "Content-Type: application/json"\n'+'data = '+json.dumps(compact(body),ensure_ascii=False)+'\n'
  p=subprocess.run(command,input=config,text=True,capture_output=True,check=True)
  text,status=p.stdout.rsplit('\n',1);assert int(status)==want,(path,status,text[:250]);return json.loads(text)if text else None
 assert http('/api/auth/me')['id']==uids[0]
 categories=sql('SELECT "Id" FROM platform_categories WHERE "IsActive" ORDER BY "Id" LIMIT 2').splitlines();assert len(categories)==2
 product_template=sql(f"SELECT \"Id\" FROM items WHERE \"TenantId\"='{tenant}' AND \"IsActive\" LIMIT 1");assert product_template
 products=[]
 for index in range(3):
  pid=str(uuid.uuid4());products.append(pid)
  data={'Id':pid,'Name':f'Synthetic copy item {index}','CategoryId':categories[index]if index<2 else None,'SourceSupplierItemId':None,'Barcodes':['COPY-'+pid],'IsActive':True,'Unit':'шт','PricePurchase':12.50}
  sql(f"INSERT INTO items SELECT (jsonb_populate_record(NULL::items,to_jsonb(i)||'{compact(data)}'::jsonb)).* FROM items i WHERE i.\"Id\"='{product_template}'")
 rid=str(uuid.uuid4());large=str(uuid.uuid4())
 for receipt,count in [(rid,201),(large,2000)]:
  sql(f"INSERT INTO stock_receipts (\"Id\",\"TenantId\",\"DestinationLocationId\",\"Status\",\"ViaCentralStore\",\"CreatedBy\",\"Notes\") VALUES ('{receipt}','{tenant}','{location}','ordered',false,'{uids[0]}','synthetic release acceptance');")
  sql(f"INSERT INTO stock_receipt_items (\"Id\",\"ReceiptId\",\"ProductId\",\"QuantityOrdered\",\"PricePurchase\",\"ExpiryDate\",\"BatchNumber\") SELECT gen_random_uuid(),'{receipt}',CASE WHEN n<=151 THEN '{products[0]}'::uuid WHEN n<=197 THEN '{products[1]}'::uuid ELSE '{products[2]}'::uuid END,1.25,12.50,'2027-11-01'::date,'COPY-'||n FROM generate_series(1,{count})n;")
 source=http('/api/receipts/'+large);assert len(source['items'])==2000
 context=http('/api/erp/receipts/'+large+'/receiving');assert context['maxReceiptLines']==2000 and not context['configured']
 summaries=http('/api/erp/receipts?store_id='+location+'&search='+large)['data'];assert summaries['totalCount']==1 and summaries['items'][0]['itemCount']==2000 and 'items'not in summaries['items'][0]
 path='/api/receipts/'+rid+'/category-work'
 def change(action,key=None,save=None,actor=0,lines=None,want=200):return http(path,{'action':action,'key':key,'save':save,'lineIds':lines},want,actor)
 work=change('start');assert work['active']and len(work['groups'])==3 and sum(len(g['lineIds'])for g in work['groups'])==201
 keys=[g['key']for g in work['groups']if g['key']!='other'];assert len(keys)==2
 a=keys[0];b=keys[1]
 change('take',a);change('take',a,actor=1,want=409);change('take',b,actor=1)
 other=next(g for g in work['groups']if g['key']=='other')
 split=change('take','other',lines=other['lineIds'][:2]);assert len(split['groups'])==4
 change('take','other',actor=1)
 current=http('/api/receipts/'+rid);by_id={x['id']:x for x in current['items']}
 work=http(path)
 def save_for(group,complete=True):return {'requestId':str(uuid.uuid4()),'expectedFingerprint':group['fingerprint'],'complete':complete,'items':[{'itemId':key,'quantityReceived':1.25,'pricePurchase':by_id[key]['pricePurchase'],'expiryDate':by_id[key]['expiryDate'],'batchNumber':by_id[key]['batchNumber'],'discrepancyNotes':None}for key in group['lineIds']]}
 ga=next(g for g in work['groups']if g['key']==a);gb=next(g for g in work['groups']if g['key']==b)
 sa=save_for(ga);sb=save_for(gb)
 change('save',a,sa,actor=1,want=409)
 http('/api/receipts/'+rid+'/items',{'items':[]},400,method='PUT')
 with concurrent.futures.ThreadPoolExecutor(max_workers=2)as pool:
  one=pool.submit(change,'save',a,sa);two=pool.submit(change,'save',b,sb,1);one.result();two.result()
 assert next(g for g in change('save',a,sa)['groups']if g['key']==a)['ready']
 work=change('reopen',a);assert not next(g for g in work['groups']if g['key']==a)['ready']
 for group in work['groups']:
  if group['ready']:continue
  actor=uids.index(group['ownerId']);change('save',group['key'],save_for(group),actor=actor)
 work=http(path);assert all(g['ready']for g in work['groups'])
 reviewed=http('/api/receipts/'+rid);assert all(x['quantityReceived']==1.25 for x in reviewed['items'])
 http('/api/erp/receipts/'+rid+'/receive',{'expectedFingerprint':reviewed['receivingFingerprint']},409)
 assert before==fingerprints(),'Synthetic category edits changed real stock or POS rows'
 received=http('/api/receipts/'+rid+'/receive',method='PUT');assert received['status']=='received'
 assert sql(f"SELECT count(*) FROM product_stock WHERE \"SourceId\"='{rid}'")=='201'
 http('/api/receipts/'+rid+'/receive',want=400,method='PUT');change('take',a,want=409)
 # Ignore only the explicitly synthetic receipt's stock additions when comparing the original copy.
 query=f"SELECT md5((to_jsonb(t)-'PricingArchiveRequired')::text) FROM product_stock t WHERE \"SourceId\" IS DISTINCT FROM '{rid}'::uuid ORDER BY \"Id\""
 raw=out(['docker','exec',DB,'psql','-X','-qAt','-U','shelfguard','-d','shelfguard','-c',query]);digest=hashlib.sha256((raw+'\n').encode()).hexdigest()
 assert digest==before['product_stock']['digest']
 after=fingerprints();assert all(after[t]==before[t]for t in ['pos_transactions','pos_transaction_items'])
 assert sql('SELECT count(*) FROM inventory_posting_scopes')=='0'and sql('SELECT count(*) FROM inventory_ledger_entries')=='0'
 result={'passed':True,'migrationRuns':2,'latestMigration':latest,'authenticatedStoreManagers':2,'role':'store_manager','runtimeBypassRls':False,'categoryPartition201':True,'selectedOtherClaim':True,'concurrentIndependentSaves':True,'sameCategoryOwnershipConflict':True,'saveReplay':True,'completedCategoryReopen':True,'legacyWholeDocumentOverwriteBlocked':True,'legacyFinalReceiving201Once':True,'large2000DetailAndCompactSummary':True,'erpAuthorityRemainedDisabled':True,'originalStockAndPosUnchanged':True,'physicalPrintingPerformed':False,'http':checks,'upgradeSha256':hashlib.sha256((R/'upgrade.sql').read_bytes()).hexdigest()}
 (R/'rehearsal-result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True)
finally:
 for kind,name in reversed(created):
  if kind=='container':
   meta=json.loads(out(['docker','inspect',name]))[0];assert meta['Config']['Labels'].get('agru.release')=='receipts-20261102'
   run(['docker','rm','-f','-v',meta['Id']],stdout=subprocess.DEVNULL)
  else:run(['docker','network','rm',name],stdout=subprocess.DEVNULL)
