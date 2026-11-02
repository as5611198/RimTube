"""Prepare split source/runtime packages; verify public sources before clearance.

No GitHub or Steam writes occur here. Publication uses the prepared release assets
only; the verifier uses unauthenticated HTTPS and checks every byte before marking
the runtime package as suitable for external-source distribution.
"""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import json
import re
import shutil
import tempfile
import urllib.request
import zipfile


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8'))


def records(folder):
    return [dict(path=p.relative_to(folder).as_posix(), bytes=p.stat().st_size, sha256=digest(p))
            for p in sorted(folder.rglob('*')) if p.is_file()]


def safe_name(name):
    parts = PurePosixPath(name)
    if parts.is_absolute() or '\\' in name or ':' in name or '..' in parts.parts or name in ('', '.'):
        raise ValueError(f'Unsafe distribution path: {name}')
    return name


def validate_files(folder, entries):
    expected = {safe_name(f['path']): f for f in entries}
    actual = {p.relative_to(folder).as_posix() for p in folder.rglob('*') if p.is_file()}
    if len(expected) != len(entries) or actual != set(expected):
        raise ValueError('Distribution file list does not match its manifest')
    for name, entry in expected.items():
        path = folder / name
        if path.stat().st_size != entry['bytes'] or digest(path) != entry['sha256']:
            raise ValueError(f'Distribution file changed: {name}')


def zip_verified(folder, output, files):
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=4) as z:
        for f in files:
            z.write(folder / f['path'], f['path'])
    with zipfile.ZipFile(output) as z:
        if z.namelist() != [f['path'] for f in files]:
            raise ValueError('Release ZIP member list differs')
        for f in files:
            with z.open(f['path']) as stream:
                verify_stream(stream, f)


def source_readme(release_url, assets):
    lines = [
        'RimTube — third-party tool corresponding sources / 第三方工具對應來源', '',
        'Playback tools are bundled. These source materials are optional downloads for users,',
        'not runtime dependencies. No source download occurs on game startup.',
        '播放器工具仍隨模組提供；來源材料可按需下載，不影響開箱即用，也不會在遊戲啟動時下載。', '',
        'Fixed version / 固定版本：' + release_url, '',
    ]
    for a in assets:
        lines += [a['name'], a['url'], f"SHA-256: {a['sha256']}", f"Bytes: {a['bytes']}", '']
    lines += [
        'Download the three source archives and metadata ZIP for the complete tool source materials.',
        'The GitHub repository Source code ZIP contains only the index; use the named Release assets.',
        '要取得完整工具來源，請下載三份來源 ZIP 和 metadata ZIP；GitHub 自動產生的 Source code',
        '壓縮檔只有索引，不等於完整對應來源。', '',
        'Upstream copyrights and licenses remain unchanged. Build recipes, patches, dependency',
        'sources, notices and version/checksum records are retained. Source snapshots omit unrelated',
        'Git history; the corresponding source trees and recorded build inputs remain available.',
        'The maintainer must retain each fixed source release while its binaries are distributed.',
        'If a link fails, report its fixed tag; do not substitute a newer version of the sources.',
    ]
    return '\n'.join(lines) + '\n'


def prepare(source, manifest_path, output, repository, tag, readme_path=None):
    source, manifest_path, output = source.resolve(), manifest_path.resolve(), output.resolve()
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError('Expected a GitHub owner/repository')
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]*', tag):
        raise ValueError('Expected a fixed safe release tag')
    if output.exists() or output == source or output.is_relative_to(source):
        raise ValueError('Output must be a new directory outside the input package')
    baseline = read_json(manifest_path)
    if not baseline['public_redistribution_cleared']:
        raise ValueError('The complete baseline source package must be audited first')
    validate_files(source, baseline['files'])
    source_entries = [f for f in baseline['files'] if f['path'].startswith('Tools/Sources/')
                      and f['path'].endswith('.zip')]
    if not source_entries:
        raise ValueError('Baseline has no corresponding source archives')
    release_url = f'https://github.com/{repository}/releases/tag/{tag}'
    asset_base = f'https://github.com/{repository}/releases/download/{tag}/'
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='external-sources-', dir=output.parent) as temporary:
        work = Path(temporary)
        package, release = work/'package', work/'release'
        release.mkdir()
        shutil.copytree(source, package, ignore=lambda folder, names: [Path(f['path']).name for f in source_entries]
                        if Path(folder) == source/'Tools/Sources' else [])
        assets = []
        for entry in source_entries:
            name = Path(entry['path']).name
            shutil.copyfile(source/entry['path'], release/name)
            assets.append(dict(name=name, bytes=entry['bytes'], sha256=entry['sha256'], url=asset_base+name))
        metadata = work/'metadata'
        metadata.mkdir()
        for entry in baseline['files']:
            name = entry['path']
            if name.startswith(('Tools/licenses/', 'Tools/provenance/')) or name in (
                    'Tools/bundled-tools.json', 'Tools/THIRD_PARTY_NOTICES.txt'):
                target = metadata/name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source/name, target)
        (metadata/'README.txt').write_text(
            'Metadata copied from the complete baseline tool package. Its source archive paths\n'
            'refer to the sibling assets on this fixed Release: '+release_url+'\n'
            'Original source archives are supplied byte-for-byte, with their build recipes and notices.\n'
            'No RimTube source code, game assets, media, credentials or player data are included.\n', encoding='utf-8')
        metadata_name = 'tool-source-metadata.zip'
        zip_verified(metadata, release/metadata_name, records(metadata))
        assets.append(dict(name=metadata_name, bytes=(release/metadata_name).stat().st_size,
                           sha256=digest(release/metadata_name), url=asset_base+metadata_name))
        source_manifest = dict(schema=1, repository=repository, tag=tag, release_url=release_url,
                               assets=assets, tools=read_json(source/'Tools/bundled-tools.json')['tools'],
                               baseline_tool_manifest_sha256=digest(source/'Tools/bundled-tools.json'),
                               retention='Retain this exact release while corresponding binaries are distributed; do not replace assets.')
        write_json(release/'source-manifest.json', source_manifest)
        manifest_sha = digest(release/'source-manifest.json')
        sums = ''.join(f"{a['sha256']}  {a['name']}\n" for a in assets)
        sums += f'{manifest_sha}  source-manifest.json\n'
        (release/'SHA256SUMS').write_text(sums, encoding='utf-8')
        text = source_readme(release_url, assets)
        (package/'Tools/Sources/README.txt').write_text(text, encoding='utf-8')
        (work/'SOURCE-DOWNLOADS.txt').write_text(text, encoding='utf-8')
        if readme_path is not None:
            shutil.copyfile(readme_path, package/'README.txt')
        (package/'Tools/THIRD_PARTY_NOTICES.txt').write_text(
            'RimTube bundled tools — Windows x64\n\n'
            'The unmodified playback tools retain their own licenses and communicate with RimTube by process/IPC.\n'
            'All upstream license/copyright texts remain under Tools/licenses/. Exact tool versions and hashes\n'
            'are in bundled-tools.json; package/build provenance remains under Tools/provenance/.\n\n'
            'Complete corresponding native sources, patches, PKGBUILD recipes, interface dependencies, Rust\n'
            'crates/lockfiles/notices and resolver source/build materials are provided as fixed Release assets:\n'
            + release_url + '\nSee Tools/Sources/README.txt and Tools/provenance/source-downloads.json\n'
            'for direct downloads, sizes and SHA-256 checksums. No login or payment is required.\n'
            'Source snapshots retain the exact recorded trees and original/snapshot build recipes; unrelated\n'
            'Git history is omitted. This is source delivery, not a claim of independently reproduced binaries.\n'
            'General compilers/build tools and operating-system libraries remain separate prerequisites.\n\n'
            'Node.js retains its complete official third-party notices in Tools/licenses/Node-v22.23.4-LICENSE.txt.\n'
            'Node.js source: https://nodejs.org/dist/v22.23.4/ . The resolver uses the unmodified yt-dlp core,\n'
            'yt-dlp-ejs and CPython; exact source dependencies, build wheels and PyInstaller exception are retained.\n'
            'Optional mutagen/curl_cffi/secretstorage are not bundled. This product includes software developed\n'
            'by the OpenSSL Project for use in the OpenSSL Toolkit (http://www.openssl.org/).\n\n'
            'The player-requested resolver updater downloads the official stable release and its own notices\n'
            'into the player profile; no scheduled updates, cookies or login integration.\n\n'
            '工具授權、告示及版本索引仍隨包；完整對應來源與建置配方由上方固定 GitHub Release 免費提供。\n'
            '來源材料不會在遊戲啟動時下載；播放工具全部保留。散布執行檔期間須持續保留對應來源。\n', encoding='utf-8')
        write_json(package/'Tools/provenance/source-downloads.json', source_manifest)
        lock_path = package/'Tools/bundled-tools.json'
        lock = read_json(lock_path)
        lock['public_redistribution_cleared'] = False
        lock['source_closure'] = 'Complete audited source assets split to a fixed GitHub Release; public byte verification pending.'
        lock['source_distribution'] = dict(status='pending', release_url=release_url,
                                            manifest_url=asset_base+'source-manifest.json',
                                            manifest_sha256=manifest_sha, assets=assets)
        lock['files'] = [f for f in records(package) if f['path'].startswith('Tools/')
                         and f['path'] != 'Tools/bundled-tools.json']
        write_json(lock_path, lock)
        manifest = dict(baseline, public_redistribution_cleared=False, files=records(package),
                        source_archives=[], source_distribution=lock['source_distribution'],
                        source_closure=lock['source_closure'], publication_status='held; public source verification pending')
        for key in ('archive', 'archive_sha256', 'archive_bytes'):
            manifest.pop(key, None)
        write_json(work/'package-manifest.json', manifest)
        write_json(work/'baseline-manifest.json', baseline)
        output.mkdir()
        for path in (package, release, work/'package-manifest.json', work/'baseline-manifest.json', work/'SOURCE-DOWNLOADS.txt'):
            shutil.move(str(path), str(output/path.name))
    print(f'PREPARED {len(assets)} source assets; runtime clearance pending public download verification.', flush=True)
    return manifest


def verify_stream(stream, expected):
    hasher, size = hashlib.sha256(), 0
    while chunk := stream.read(1024*1024):
        size += len(chunk)
        if size > expected['bytes']:
            raise ValueError('Source download exceeds the verified size')
        hasher.update(chunk)
    if size != expected['bytes'] or hasher.hexdigest() != expected['sha256']:
        raise ValueError('Source download size or SHA-256 differs')
    return hasher.hexdigest()


def verify_and_finalize(output, opener=None):
    output = output.resolve()
    opener = opener or urllib.request.urlopen
    manifest = read_json(output/'package-manifest.json')
    package = output/'package'
    validate_files(package, manifest['files'])
    distribution = manifest['source_distribution']
    local_index = output/'release/source-manifest.json'
    if digest(local_index) != distribution['manifest_sha256']:
        raise ValueError('Local source manifest changed')
    downloads = [dict(name='source-manifest.json', url=distribution['manifest_url'],
                      bytes=local_index.stat().st_size, sha256=distribution['manifest_sha256'])]
    downloads += distribution['assets']
    verified = []
    for asset in downloads:
        print(f"VERIFY_PUBLIC {asset['name']} ({asset['bytes']} bytes)", flush=True)
        request = urllib.request.Request(asset['url'], headers={'User-Agent': 'RimTube-source-release-verification'})
        with opener(request, timeout=90) as response:
            if hasattr(response, 'geturl') and not response.geturl().startswith('https://'):
                raise ValueError('Source download left HTTPS')
            verify_stream(response, asset)
        verified.append(asset)
        print(f"VERIFIED_PUBLIC {asset['name']}", flush=True)
    # No clearance change occurs until every download passed. Never rewrite tool binaries.
    lock_path = package/'Tools/bundled-tools.json'
    lock = read_json(lock_path)
    lock['public_redistribution_cleared'] = True
    lock['source_closure'] = 'Complete audited sources and build materials at fixed GitHub Release; all public assets verified by size/SHA-256.'
    lock['source_distribution']['status'] = 'verified'
    write_json(lock_path, lock)
    files = records(package)
    baseline = {f['path']: f for f in read_json(output/'baseline-manifest.json')['files']}
    binaries = [f for f in files if f['path'].lower().endswith(('.dll', '.exe'))]
    if any(f['path'] not in baseline or f['sha256'] != baseline[f['path']]['sha256'] for f in binaries):
        raise ValueError('Runtime executable/DLL changed during source separation')
    archive = output/'release.zip'
    zip_verified(package, archive, files)
    manifest.update(public_redistribution_cleared=True, files=files,
                    source_closure=lock['source_closure'], source_distribution=lock['source_distribution'],
                    archive=archive.name, archive_bytes=archive.stat().st_size, archive_sha256=digest(archive),
                    publication_status='held; sources verified; Steam upload is a separate final step',
                    report='docs/release/外部來源分流-20261102.md')
    write_json(output/'package-manifest.json', manifest)
    receipt = dict(all_assets_verified=True, authentication_used=False, assets=verified,
                   source_release=distribution['release_url'], binary_files_unchanged=len(binaries),
                   runtime_files_verified=len(files), runtime_bytes=sum(f['bytes'] for f in files),
                   archive_bytes=archive.stat().st_size, archive_sha256=manifest['archive_sha256'],
                   steam_uploaded=False, deployed=False)
    write_json(output/'source-verification.json', receipt)
    print(json.dumps(receipt, ensure_ascii=False, indent=2), flush=True)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('prepare')
    p.add_argument('--source', type=Path, required=True)
    p.add_argument('--manifest', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--repository', required=True)
    p.add_argument('--tag', required=True)
    p.add_argument('--readme', type=Path)
    p = commands.add_parser('verify')
    p.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.command == 'prepare':
        prepare(args.source, args.manifest, args.output, args.repository, args.tag, args.readme)
    else:
        verify_and_finalize(args.output)


if __name__ == '__main__':
    main()
