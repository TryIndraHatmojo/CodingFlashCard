"""Prepare official NuGet runtime packages via Python TLS for offline publish."""
import base64,hashlib,pathlib,urllib.request,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
sdk=next((root/'.tools/dotnet/sdk').glob('10.*'))
props=ET.parse(sdk/'Microsoft.NETCoreSdk.BundledVersions.props')
feed=root/'.tools/feed';feed.mkdir(exist_ok=True)
for framework,package in [('Microsoft.NETCore.App','microsoft.netcore.app.runtime.win-x64'),('Microsoft.WindowsDesktop.App','microsoft.windowsdesktop.app.runtime.win-x64'),('Microsoft.AspNetCore.App','microsoft.aspnetcore.app.runtime.win-x64')]:
    ref=next(x for x in props.iter('KnownFrameworkReference') if x.get('Include')==framework and x.get('TargetFramework')=='net10.0')
    version=ref.get('LatestRuntimeFrameworkVersion')
    name=f'{package}.{version}.nupkg';path=feed/name
    if not path.exists():
        print('Downloading runtime package',package,version,flush=True)
        url=f'https://api.nuget.org/v3-flatcontainer/{package}/{version}/{name}'
        urllib.request.urlretrieve(url,path)
        # HTTPS uses Python's verified certificate store; no certificate bypass.
print('Runtime feed ready',flush=True)
