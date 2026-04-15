import os
import sys
from datetime import datetime
import platform
import plistlib

# appcast_ver = os.environ.get('APPVEYOR_BUILD_VERSION')
appcast_ver = "0.1.547.7"
if len(sys.argv) > 1:
    appcast_ver = sys.argv[1]

def write_appcast(appcast_os, appcast_rid, appcast_file):

    xml = '''<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
<channel>
    <title>OpenUtau</title>
    <language>en</language>
    <item>
    <title>OpenUtau %s</title>
    <pubDate>%s</pubDate>
    <enclosure url="https://github.com/JackZ2024/OpenUtau/releases/download/%s/%s"
                sparkle:version="%s"
                sparkle:shortVersionString="%s"
                sparkle:os="%s"
                type="application/octet-stream"
                sparkle:signature="" />
    </item>
</channel>
</rss>''' % (appcast_ver, datetime.now().strftime("%a, %d %b %Y %H:%M:%S %z"),
             appcast_ver, appcast_file, appcast_ver, appcast_ver, appcast_os)

    with open("appcast.%s.xml" % (appcast_rid), 'w') as f:
        f.write(xml)
        
def patch_info_plist(app_bundle_path):
    # 定位 Info.plist 的路径
    plist_path = os.path.join(app_bundle_path, "Contents", "Info.plist")
    
    if not os.path.exists(plist_path):
        print(f"Error: {plist_path} not found.")
        return
 
    # 读取现有的 plist
    with open(plist_path, 'rb') as f:
        pl = plistlib.load(f)
 
    # 1. 定义要添加的 CFBundleDocumentTypes
    new_doc_types = [
        {
            'CFBundleTypeName': 'OpenUtau Sequence File',
            'LSItemContentTypes': ['com.stakira.openutau.ustx'],
            'CFBundleTypeRole': 'Editor',
            'LSHandlerRank': 'Owner',
            'CFBundleTypeIconFile': 'OpenUtau.icns'
        }
    ]
 
    # 2. 定义要添加的 UTExportedTypeDeclarations
    new_uti_decls = [
        {
            'UTTypeIdentifier': 'com.stakira.openutau.ustx',
            'UTTypeDescription': 'OpenUtau Sequence File',
            'UTTypeConformsTo': ['public.data'],
            'UTTypeTagSpecification': {
                'public.filename-extension': ['ustx']
            }
        }
    ]
 
    # 写入数据（如果已存在则覆盖，不存在则创建）
    pl['CFBundleDocumentTypes'] = new_doc_types
    pl['UTExportedTypeDeclarations'] = new_uti_decls
    
    # 顺便确保一些基础字段正确
    pl['CFBundleIconFile'] = 'OpenUtau.icns'
 
    # 写回文件
    with open(plist_path, 'wb') as f:
        plistlib.dump(pl, f)
    
    print(f"Successfully patched {plist_path}")


if sys.platform == 'win32':
    if appcast_ver is not None:
        os.system("git tag build/%s 2>&1" % (appcast_ver))
        os.system("git push origin build/%s 2>&1" % (appcast_ver))

    os.system("del *.xml 2>&1")

    import urllib.request
    urllib.request.urlretrieve("https://www.nuget.org/api/v2/package/Microsoft.AI.DirectML/1.12.0", "Microsoft.AI.DirectML.nupkg")
    os.system("mkdir Microsoft.AI.DirectML")
    os.system("tar -xf Microsoft.AI.DirectML.nupkg -C Microsoft.AI.DirectML")

    os.system("dotnet restore OpenUtau -r win-x86")
    os.system(
        "dotnet publish OpenUtau -c Release -r win-x86 --self-contained true -o bin/win-x86")
    os.system("copy /y OpenUtau.Plugin.Builtin\\bin\\Release\\netstandard2.1\\OpenUtau.Plugin.Builtin.dll bin\\win-x86")
    write_appcast("windows", "win-x86", "OpenUtau-win-x86.zip")

    os.system("dotnet restore OpenUtau -r win-x64")
    os.system(
        "dotnet publish OpenUtau -c Release -r win-x64 --self-contained true -o bin/win-x64")
    os.system("copy /y OpenUtau.Plugin.Builtin\\bin\\Release\\netstandard2.1\\OpenUtau.Plugin.Builtin.dll bin\\win-x64")
    write_appcast("windows", "win-x64", "OpenUtau-win-x64.zip")

    os.system("makensis -DPRODUCT_VERSION=%s OpenUtau.nsi" % (appcast_ver))
    write_appcast("windows", "win-x64-installer", "OpenUtau-win-x64.exe")

elif sys.platform == 'darwin':
    os.system("rm *.dmg")
    os.system("rm *.xml")
    appcast_rid = "osx-x64"
    if platform.machine() == 'arm64':
        appcast_rid = "osx-arm64"
    if len(sys.argv) > 2:
        appcast_rid = sys.argv[2]

    # os.system("git checkout OpenUtau/OpenUtau.csproj")
    # os.system("rm LICENSE.txt")
    os.system("sed -i '' \"s/0.0.0/%s/g\" OpenUtau/OpenUtau.csproj" % (appcast_ver))
    os.system(f"dotnet restore OpenUtau -r {appcast_rid}")
    # 下面这个命令也是编译命令，可以用来检查编译的报错信息，下面的打包编译命令没有报错信息，编译不过去也不知道怎么回事。
    # dotnet msbuild OpenUtau -t:BundleApp -p:Configuration=Release -v:detailed
    os.system(f"dotnet msbuild OpenUtau -t:BundleApp -p:Configuration=Release -p:RuntimeIdentifier={appcast_rid} \
              -p:UseAppHost=true -p:OutputPath=../bin/{appcast_rid}/ -p:PublishReadyToRun=false")
    
    os.system(f"cp OpenUtau/Assets/OpenUtau.icns bin/{appcast_rid}/publish/OpenUtau.app/Contents/Resources/")
    patch_info_plist(f"./bin/{appcast_rid}/publish/OpenUtau.app")
    os.system("rm *.dmg")
    # 这个命令只需要执行一次，并且需要管理员权限，需要单独执行
    # os.system("npm install -g create-dmg")
    os.system(f"create-dmg bin/{appcast_rid}/publish/OpenUtau.app")
    os.system(f"mv *.dmg OpenUtau-{appcast_rid}.dmg")
    os.system(f"codesign -fvs - OpenUtau-{appcast_rid}.dmg")
    # os.system("git checkout OpenUtau/OpenUtau.csproj")
    # os.system("git checkout LICENSE.txt")

    write_appcast("macos", appcast_rid, f"OpenUtau-{appcast_rid}.dmg")

else:
    os.system("rm *.xml")

    os.system("dotnet restore OpenUtau -r linux-x64")
    os.system(
        "dotnet publish OpenUtau -c Release -r linux-x64 --self-contained true -o bin/linux-x64")
    os.system("chmod +x bin/linux-x64/OpenUtau")
    os.system("tar -C bin/linux-x64 -czvf OpenUtau-linux-x64.tar.gz .")
    write_appcast("linux", "linux-x64", "OpenUtau-linux-x64.tar.gz")
