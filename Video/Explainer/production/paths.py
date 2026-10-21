"""Where the explainer pipeline reads, and the one place it may write.

Everything under production/ is source, and the pipeline only reads it:
historical scans, cutouts, brand, fonts, audio, captions, and the frozen
revision 5 artwork in assets/render and assets/transitions/crumple_r5. Every
generated file (artwork the baseline lacks, crumple poses, proofs, renders,
traces) goes under BUILD, which can be deleted at any time.

PHILLYPAUSE_BUILD moves BUILD elsewhere. PHILLYPAUSE_IGNORE_BASELINE=1 redraws
every asset from code into BUILD instead of loading the frozen R5 artwork; it
exists for diagnostics and tests, and does not reproduce R5's lettering.
"""
from __future__ import annotations
import hashlib
import os
from pathlib import Path

ROOT=Path(__file__).resolve().parent
ASSETS=ROOT/'assets'
BASELINE_ART=ASSETS/'render'
BASELINE_FOLDS=ASSETS/'transitions'/'crumple_r5'
SOURCE_DIRS=(ASSETS,ROOT/'audio',ROOT/'captions')
BUILD=Path(os.environ.get('PHILLYPAUSE_BUILD') or ROOT/'build').resolve()
USE_BASELINE=os.environ.get('PHILLYPAUSE_IGNORE_BASELINE','') in ('','0')


def inside(path,folder):
    path,folder=Path(path).resolve(),Path(folder).resolve()
    return path==folder or folder in path.parents


if inside(ROOT,BUILD) or any(inside(BUILD,d) for d in SOURCE_DIRS):
    raise RuntimeError(f'Build folder {BUILD} would mix generated files with source; choose another PHILLYPAUSE_BUILD')


def writable(path):
    """Resolve a destination, refusing anything in the source tree outside BUILD."""
    path=Path(path).resolve()
    if inside(path,ROOT) and not inside(path,BUILD):
        raise PermissionError(f'Refusing to write {path}: production/ is source; generated files belong under {BUILD}')
    path.parent.mkdir(parents=True,exist_ok=True)
    return path


def output(*parts):
    """A path under BUILD, with its parent folder created."""
    return writable(BUILD.joinpath(*parts))


def text_bytes(path):
    """File bytes with CRLF folded to LF, so a Windows checkout hashes like the original."""
    return Path(path).read_bytes().replace(b'\r\n',b'\n')


def text_sha256(path):
    return hashlib.sha256(text_bytes(path)).hexdigest()


def portable(path):
    """Record a file relative to production/ when it lives there, with forward slashes on every OS."""
    path=Path(path).resolve()
    return (path.relative_to(ROOT) if inside(path,ROOT) else path).as_posix()
