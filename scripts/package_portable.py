"""Create a verified, flat portable ZIP; only temporary extraction is performed here."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import stat
import zipfile

ROOT = Path(__file__).resolve().parent.parent
NATIVE = 'native/mpv/win-x64/libmpv-2.dll'
MANIFEST = 'native/mpv/win-x64/runtime-manifest.json'
REQUIRED = {
    'NexusExplorer.exe', 'appsettings.json', 'release.json', 'e_sqlite3.dll',
    'PresentationNative_cor3.dll', 'wpfgfx_cor3.dll', NATIVE, MANIFEST,
    'native/mpv/input.conf', 'licenses/NexusExplorer-GPL-2.0.txt',
    'licenses/NexusExplorer-source.zip', 'licenses/SOURCE.txt',
}

def hash_stream(stream):
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(block)
    return digest.hexdigest()

def allowed(name):
    if '\\' in name or ':' in name or PurePosixPath(name).is_absolute() or '..' in PurePosixPath(name).parts:
        return False
    return (('/' not in name and (name in {'NexusExplorer.exe', 'appsettings.json', 'release.json'}
        or name.lower().endswith('.dll') and not name.lower().endswith('.tests.dll')))
        or name in {NATIVE, MANIFEST, 'native/mpv/input.conf'} or name.startswith('licenses/'))

def check_defaults(config):
    for section, key in (('Storage', 'RootPath'), ('Database', 'Path'), ('Logging', 'Directory')):
        if config[section][key]:
            raise ValueError('Portable configuration must not contain user data paths: ' + section + '.' + key)

def inspect_archive(archive):
    with zipfile.ZipFile(archive) as package:
        infos = package.infolist()
        names = [info.filename for info in infos]
        if len(set(names)) != len(names) or not REQUIRED.issubset(names):
            raise ValueError('Archive contains duplicate entries or lacks required runtime files')
        for info in infos:
            if info.is_dir() or not allowed(info.filename) or stat.S_ISLNK(info.external_attr >> 16):
                raise ValueError('Unexpected archive member: ' + info.filename)
        if package.testzip() is not None:
            raise ValueError('Portable ZIP CRC check failed')
        check_defaults(json.loads(package.read('appsettings.json').decode('utf-8-sig')))
        manifest = json.loads(package.read(MANIFEST).decode('utf-8-sig'))
        with package.open(NATIVE) as stream:
            if hash_stream(stream) != manifest['dllSha256']:
                raise ValueError('Archived libmpv checksum mismatch')
        return names

def create_archive(published, archive):
    published, archive = Path(published).resolve(), Path(archive).absolute()
    files = []
    for path in sorted(published.rglob('*')):
        if not path.is_file():
            continue
        name = path.relative_to(published).as_posix()
        if allowed(name):
            if path.is_symlink() or getattr(path.stat(follow_symlinks=False), 'st_file_attributes', 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError('Published runtime contains a link: ' + name)
            files.append((path, name))
    if not REQUIRED.issubset({name for _, name in files}):
        raise ValueError('Publish directory lacks required runtime files')
    archive.parent.mkdir(parents=True, exist_ok=True)
    temporary = archive.with_name('.' + archive.name + '.tmp')
    if temporary.exists() or temporary.is_symlink():
        raise ValueError('A temporary package already exists; do not overwrite an unknown file')
    created = False
    try:
        with zipfile.ZipFile(temporary, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=5, allowZip64=True) as package:
            created = True
            for path, name in files:
                package.write(path, name)
        names = inspect_archive(temporary)
        os.replace(temporary, archive)
        return names
    finally:
        if created and temporary.exists():
            temporary.unlink()

def extract_verified(archive, destination):
    destination = Path(destination).resolve()
    if destination.exists():
        raise ValueError('Verification extraction requires a new, empty destination')
    names = inspect_archive(archive)
    destination.mkdir(parents=True)
    with zipfile.ZipFile(archive) as package:
        package.extractall(destination)
        for name in names:
            with package.open(name) as original, (destination / name).open('rb') as extracted:
                if hash_stream(original) != hash_stream(extracted):
                    raise ValueError('Extracted file checksum mismatch: ' + name)
    return names

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--published-directory', required=True, type=Path)
    parser.add_argument('--archive', type=Path, default=ROOT / 'artifacts/NexusExplorer_new.zip')
    parser.add_argument('--extract-directory', required=True, type=Path)
    args = parser.parse_args()
    create_archive(args.published_directory, args.archive)
    names = extract_verified(args.archive, args.extract_directory)
    with args.archive.open('rb') as stream:
        digest = hash_stream(stream)
    print(json.dumps({'archive': str(args.archive), 'sha256': digest, 'bytes': args.archive.stat().st_size,
        'file_count': len(names), 'crc_and_extracted_hashes': 'PASS', 'names': names}, ensure_ascii=True))

if __name__ == '__main__':
    main()
