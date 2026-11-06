"""Manual release helper: derive candidate/rollback compose specs from a frozen `docker inspect` dump.

Logic is taken from scripts/deploy/receipts-production.py (mode `prepare`), which already rolled out production
once, with release-specific asserts removed. It only WRITES two JSON files; it never runs docker compose up/stop.
Environment, mounts, networks, ports, healthchecks and resource limits are copied 1:1 from the running containers.
The files contain secrets (container env): they are written with mode 0600 and nothing from env is ever printed.

Usage (run with `python3 -I`, from any directory):
  make-compose-spec.py INSPECT_JSON OUT_DIR API_IMAGE_ID WEB_IMAGE_ID [NAME_PREFIX]
"""
import json, os, pathlib, subprocess, sys

os.umask(0o077)
inspect_file, out_dir, api_image, web_image = sys.argv[1:5]
prefix = sys.argv[5] if len(sys.argv) > 5 else 'shelfguard_'
out_dir = pathlib.Path(out_dir)
APIS = ['api_core', 'api_consumer', 'api_provider']
TARGETS = APIS + ['web']
baseline = {x['Name'].lstrip('/'): x for x in json.loads(pathlib.Path(inspect_file).read_text())}
for s in TARGETS:
    assert prefix + s in baseline, 'missing in inspect dump: ' + prefix + s


def envdict(b):
    return dict(x.split('=', 1) for x in b['Config']['Env'])


for new, which in [(True, 'candidate'), (False, 'rollback')]:
    spec = {'name': 'shelfguard', 'services': {}, 'networks': {}, 'volumes': {}}
    for service in TARGETS:
        b = baseline[prefix + service]
        e = envdict(b)
        image = (web_image if service == 'web' else api_image) if new else b['Image']
        s = {
            'container_name': prefix + service,
            'image': image,
            'environment': e,
            'restart': b['HostConfig']['RestartPolicy']['Name'] or 'no',
            'entrypoint': b['Config'].get('Entrypoint') or [],
            'command': b['Config'].get('Cmd') or [],
            'networks': {},
        }
        if service != 'web':
            # API image entrypoint is fixed by Dockerfile.api; keep it explicit like the original rollout did.
            s['entrypoint'] = ['dotnet', 'ShelfGuard.Api.dll']
            s['command'] = []
        for network in b['NetworkSettings']['Networks']:
            spec['networks'][network] = {'external': True, 'name': network}
            s['networks'][network] = {}
        mounts = []
        for m in b['Mounts']:
            if m['Type'] == 'volume':
                key = 'v' + str(len(spec['volumes']))
                spec['volumes'][key] = {'external': True, 'name': m['Name']}
                mounts.append({'type': 'volume', 'source': key, 'target': m['Destination'], 'read_only': not m['RW']})
            else:
                mounts.append({'type': 'bind', 'source': m['Source'], 'target': m['Destination'], 'read_only': not m['RW']})
        if mounts:
            s['volumes'] = mounts
        ports = [{'target': int(port.split('/')[0]), 'protocol': port.split('/')[1], 'host_ip': p['HostIp'], 'published': p['HostPort']}
                 for port, ps in (b['HostConfig'].get('PortBindings') or {}).items() for p in ps]
        if ports:
            s['ports'] = ports
        if b['Config'].get('Healthcheck'):
            s['healthcheck'] = {('start_period' if k == 'StartPeriod' else k.lower()): str(v) + 'ns' if k in ['Interval', 'Timeout', 'StartPeriod'] else v
                                for k, v in b['Config']['Healthcheck'].items()}
        host = b['HostConfig']
        if host.get('ExtraHosts'):
            s['extra_hosts'] = host['ExtraHosts']
        if host.get('Memory'):
            s['mem_limit'] = host['Memory']
        mappings = {'ReadonlyRootfs': 'read_only', 'Privileged': 'privileged', 'CapAdd': 'cap_add', 'CapDrop': 'cap_drop',
                    'SecurityOpt': 'security_opt', 'PidsLimit': 'pids_limit', 'MemoryReservation': 'mem_reservation',
                    'MemorySwap': 'memswap_limit', 'Dns': 'dns', 'DnsOptions': 'dns_opt', 'DnsSearch': 'dns_search',
                    'ShmSize': 'shm_size', 'Init': 'init'}
        for source, target in mappings.items():
            if host.get(source) is not None and host[source] not in [[], 0]:
                s[target] = host[source]
        if host.get('NanoCpus'):
            s['cpus'] = host['NanoCpus'] / 1_000_000_000
        if host.get('MemorySwappiness') is not None:
            s['mem_swappiness'] = host['MemorySwappiness']
        if b['Config'].get('User'):
            s['user'] = b['Config']['User']
        if host.get('Ulimits'):
            s['ulimits'] = {u['Name']: {'soft': u['Soft'], 'hard': u['Hard']} for u in host['Ulimits']}
        if host.get('LogConfig', {}).get('Type'):
            s['logging'] = {'driver': host['LogConfig']['Type'], 'options': host['LogConfig'].get('Config', {})}
        if host.get('Tmpfs'):
            s['tmpfs'] = [k + (':' + v if v else '') for k, v in host['Tmpfs'].items()]
        spec['services'][service] = s
    path = out_dir / (which + '.private.json')
    path.write_text(json.dumps(spec, indent=2).replace('$', '$$'))
    os.chmod(path, 0o600)
    resolved = json.loads(subprocess.check_output(['docker', 'compose', '-p', 'shelfguard', '-f', str(path), 'config', '--format', 'json'], text=True))
    for service in TARGETS:
        assert resolved['services'][service]['environment'] == spec['services'][service]['environment'], \
            'Environment changed by Compose interpolation: ' + service
    print(which + ': written ' + str(path) + ' (images: ' + ', '.join(spec['services'][t]['image'][:19] for t in TARGETS) + ')')
for service in APIS:
    v = envdict(baseline[prefix + service]).get('Migrations__RunOnStartup')
    print('NOTE ' + prefix + service + ': Migrations__RunOnStartup=' + str(v) + ('' if v == 'false' else '  <-- not "false": the API would run EF migrations itself on start'))
