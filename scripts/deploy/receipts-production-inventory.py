import pathlib,json,subprocess,os,shutil
R=pathlib.Path(os.environ['SG_RELEASE_DIR']);assert R.parent==pathlib.Path('/home/administrator/shelfguard-releases')and R.name.startswith('receipts-');os.umask(0o077)
assert not R.exists(), 'Release path already exists; inspect rather than overwrite'
R.mkdir(parents=True)
def out(a):return subprocess.check_output(a,text=True,stderr=subprocess.PIPE).strip()
names=['shelfguard_'+x for x in ['api_core','api_consumer','api_provider','web','worker','gateway','postgres','redis','mosquitto']]
rows=json.loads(out(['docker','inspect',*names]));(R/'before-private.json').write_text(json.dumps(rows))
for c in rows:assert c['State']['Running'],c['Name']
assert {c['Image'] for c in rows if c['Name'].startswith('/shelfguard_api_')}=={'sha256:06e4a8ecd5a8b1e8fac28750707859180e3cb3b20c62e31265aaf73d061e39ef'}
web=next(c for c in rows if c['Name']=='/shelfguard_web');env=dict(x.split('=',1)for x in web['Config']['Env'])
# Only public build-time settings can leave the server.
public={k:env[k] for k in ['NEXT_PUBLIC_API_URL','NEXT_PUBLIC_GA_ID'] if k in env}
root=pathlib.Path('/home/administrator/shelfguard')
if (root/'.env').exists():
 for line in (root/'.env').read_text().splitlines():
  if '=' in line:
   k,v=line.split('=',1)
   if k in ['NEXT_PUBLIC_API_URL','NEXT_PUBLIC_GA_ID']:public[k]=v.strip().strip('"').strip("'")
public.setdefault('NEXT_PUBLIC_API_URL','https://api.agrusystems.pp.ua')
mounts=next(c for c in rows if c['Name']=='/shelfguard_gateway')['Mounts']
nginx=out(['docker','exec','shelfguard_gateway','nginx','-T']);(R/'gateway-config-before-private.txt').write_text(nginx)
summary={'release':str(R),'diskFreeBytes':shutil.disk_usage(R).free,'publicBuild':public,'services':[{'name':c['Name'],'id':c['Id'],'imageId':c['Image'],'image':c['Config']['Image'],'healthy':c['State'].get('Health',{}).get('Status')}for c in rows],
'latestMigration':out(['docker','exec','shelfguard_postgres','psql','-X','-qAt','-U','shelfguard','-d','shelfguard','-c','SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1']),
'gatewayReadTimeoutLines':[l.strip()for l in nginx.splitlines()if 'proxy_read_timeout' in l]}
assert summary['diskFreeBytes']>1024**3
(R/'inventory.json').write_text(json.dumps(summary,indent=2));print(json.dumps(summary))
