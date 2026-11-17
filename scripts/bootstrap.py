"""Download an isolated .NET 10 SDK; never changes the machine installation."""
import hashlib, json, pathlib, urllib.request, zipfile
root = pathlib.Path(__file__).resolve().parents[1]
target = root / '.tools' / 'dotnet'
if (target / 'dotnet.exe').exists():
    print('Local SDK already installed')
    raise SystemExit
with urllib.request.urlopen('https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json', timeout=30) as response:
    releases = json.load(response)
sdk = releases['releases'][0]['sdk']
asset = next(f for f in sdk['files'] if f['rid'] == 'win-x64' and f['name'].endswith('.zip'))
target.mkdir(parents=True, exist_ok=True)
archive = target.parent / 'sdk.zip'
print('Downloading SDK', sdk['version'], flush=True)
urllib.request.urlretrieve(asset['url'], archive)
assert hashlib.sha512(archive.read_bytes()).hexdigest().lower() == asset['hash'].lower(), 'SDK hash mismatch'
with zipfile.ZipFile(archive) as bundle:
    bundle.extractall(target)
archive.unlink()
print('Installed isolated SDK:', target, flush=True)
