"""Manual release helper, READ-ONLY: compare live containers with the frozen pre-release `docker inspect` dump.

Checks that env, mounts, security/resource settings are identical to the baseline for the replaced services, that
the replaced services run the expected images, and that every other container is untouched (same container Id).
Secrets are never printed; only container names and the names of differing keys.

Usage (run with `python3 -I`): verify-containers.py INSPECT_JSON API_IMAGE_ID WEB_IMAGE_ID [NAME_PREFIX]
Exit code 0 = all good, 1 = at least one difference.
"""
import json, pathlib, subprocess, sys

inspect_file, api_image, web_image = sys.argv[1:4]
prefix = sys.argv[4] if len(sys.argv) > 4 else 'shelfguard_'
baseline = {x['Name'].lstrip('/'): x for x in json.loads(pathlib.Path(inspect_file).read_text())}
APIS = [prefix + s for s in ['api_core', 'api_consumer', 'api_provider']]
WEB = prefix + 'web'
KEYS = ['Privileged', 'ReadonlyRootfs', 'CapAdd', 'CapDrop', 'SecurityOpt', 'PidsLimit', 'Ulimits', 'LogConfig', 'Memory',
        'MemoryReservation', 'MemorySwap', 'NanoCpus', 'ExtraHosts', 'Dns', 'DnsOptions', 'DnsSearch', 'ShmSize', 'Init',
        'RestartPolicy', 'PortBindings', 'NetworkMode']
bad = []


def live(name):
    return json.loads(subprocess.check_output(['docker', 'inspect', name], text=True))[0]


def env(c):
    return dict(x.split('=', 1) for x in c['Config']['Env'])


def mounts(c):
    return {(m['Destination'], m.get('Name', m.get('Source')), m['RW']) for m in c['Mounts']}


for name, b in baseline.items():
    c = live(name)
    before = len(bad)
    if name in APIS + [WEB]:
        want = api_image if name in APIS else web_image
        if c['Image'] != want:
            bad.append(name + ': image is ' + c['Image'][:19] + ', expected ' + want[:19])
        if env(c) != env(b):
            diff = sorted(k for k in set(env(b)) | set(env(c)) if env(b).get(k) != env(c).get(k))
            bad.append(name + ': environment differs in keys ' + ','.join(diff))
        if mounts(c) != mounts(b):
            bad.append(name + ': mounts differ')
        for k in KEYS:
            if b['HostConfig'].get(k) != c['HostConfig'].get(k):
                bad.append(name + ': HostConfig.' + k + ' differs')
        if b['Config'].get('User') != c['Config'].get('User'):
            bad.append(name + ': User differs')
        if set(b['NetworkSettings']['Networks']) != set(c['NetworkSettings']['Networks']):
            bad.append(name + ': networks differ')
    elif c['Id'] != b['Id'] or c['Image'] != b['Image']:
        bad.append(name + ': untargeted container changed (Id or image)')
    print(('OK   ' if len(bad) == before else 'FAIL ') + name + '  image=' + c['Image'][:19]
          + '  running=' + str(c['State']['Running']) + '  health=' + str(c['State'].get('Health', {}).get('Status')))
for line in bad:
    print('DIFF ' + line)
sys.exit(1 if bad else 0)
