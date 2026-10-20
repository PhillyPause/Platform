"""Proof rendering leaves every source file untouched, and repeats exactly.

Runs the real command, production/film.py --format landscape --proof, in fresh
processes with PHILLYPAUSE_BUILD pointed at temporary folders, two ways:

  frozen      the R5 baseline artwork and crumple poses, as approved
  regenerate  PHILLYPAUSE_IGNORE_BASELINE=1: every asset and both crumples are
              drawn from code, the path a creative revision takes

Each runs twice into one folder, cold then warm, under different hash seeds,
and the two must agree byte for byte. Run from Video/Explainer:

  .venv/Scripts/python -m unittest discover -s production/tests -v
"""
from __future__ import annotations
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

PRODUCTION=Path(__file__).resolve().parents[1]
PROJECT=PRODUCTION.parent
SKIP={'.venv','__pycache__','.pytest_cache'}
SCRATCH=tempfile.TemporaryDirectory(prefix='phillypause-render-')


def setUpModule():
    # Imported here, the guard must not create production/build either.
    os.environ['PHILLYPAUSE_BUILD']=str(Path(SCRATCH.name)/'unit')
    sys.path.insert(0,str(PRODUCTION))


def tearDownModule():
    SCRATCH.cleanup()


def tree(root):
    """sha256 of every file under root, except virtualenvs and bytecode."""
    digest={}
    for folder,dirs,files in os.walk(root):
        dirs[:]=[d for d in dirs if d not in SKIP]
        for name in files:
            path=Path(folder)/name
            digest[path.relative_to(root).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
    return digest


def proof(build,regenerate,seed):
    env=dict(os.environ,PHILLYPAUSE_BUILD=str(build),PYTHONHASHSEED=seed)
    env.pop('PHILLYPAUSE_IGNORE_BASELINE',None)
    if regenerate:env['PHILLYPAUSE_IGNORE_BASELINE']='1'
    result=subprocess.run([sys.executable,str(PRODUCTION/'film.py'),'--format','landscape','--proof'],
        cwd=PROJECT,env=env,capture_output=True,text=True,timeout=1800)
    if result.returncode:raise AssertionError('Proof failed:\n'+result.stdout+result.stderr)
    return tree(build)


class ProofRendering(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base=Path(SCRATCH.name)
        cls.before=tree(PROJECT)
        cls.runs={mode:[proof(cls.base/mode,mode=='regenerate',seed) for seed in ('1','2')]
                  for mode in ('frozen','regenerate')}
        cls.after=tree(PROJECT)

    def provenance(self,mode):
        return json.loads((self.base/mode/'proof/landscape_provenance.json').read_text())

    def test_sources_untouched(self):
        changed=sorted(k for k in self.before.keys()&self.after.keys() if self.before[k]!=self.after[k])
        added=sorted(self.after.keys()-self.before.keys())
        removed=sorted(self.before.keys()-self.after.keys())
        self.assertEqual(dict(changed=changed,added=added,removed=removed),dict(changed=[],added=[],removed=[]))

    def test_frozen_proof_repeats(self):
        cold,warm=self.runs['frozen']
        self.assertEqual(cold,warm)
        self.assertEqual(len([k for k in cold if k.endswith('.png')]),17)

    def test_regenerated_proof_repeats(self):
        cold,warm=self.runs['regenerate']
        self.assertEqual(cold,warm)

    def test_frozen_proof_is_drawn_from_r5(self):
        prov=self.provenance('frozen')
        self.assertEqual(prov['artwork']['generated'],[])
        # A rejected R5 crumple usually means paper_crumple.py was edited:
        # its own hash is part of the frozen poses' key.
        self.assertEqual({k:v['origin'] for k,v in prov['crumples'].items()},{'03':'baseline','08':'baseline'})
        self.assertEqual(sorted(p.name for p in (self.base/'frozen').iterdir()),['proof'])

    def test_regenerate_draws_everything(self):
        prov=self.provenance('regenerate')
        self.assertEqual(prov['artwork']['baseline'],[])
        self.assertGreater(len(prov['artwork']['generated']),100)
        self.assertEqual({k:v['origin'] for k,v in prov['crumples'].items()},{'03':'generated','08':'generated'})


class WriteGuard(unittest.TestCase):
    def setUp(self):
        import paths
        self.paths=paths

    def test_refuses_source(self):
        p=self.paths
        for target in [p.BASELINE_ART/'auto_template.png',p.BASELINE_FOLDS/'landscape_03/folds.json',
                       p.ROOT/'audio/mix_revision4.wav',p.ROOT/'captions/web.ass',p.ROOT/'film.py',p.ROOT/'proof/x.png']:
            with self.subTest(target=target),self.assertRaises(PermissionError):p.writable(target)

    def test_allows_build(self):
        self.assertEqual(self.paths.output('proof','x.png'),self.paths.BUILD/'proof/x.png')

    def test_refuses_build_folder_in_source(self):
        for build in [PRODUCTION,PROJECT,PRODUCTION/'assets/render',PRODUCTION/'audio/tmp']:
            with self.subTest(build=build):
                env=dict(os.environ,PHILLYPAUSE_BUILD=str(build))
                result=subprocess.run([sys.executable,'-c','import paths'],cwd=PRODUCTION,env=env,capture_output=True,text=True)
                self.assertNotEqual(result.returncode,0)
                self.assertIn('would mix generated files with source',result.stderr)

    @unittest.skipIf(shutil.which('git') is None,'git not on PATH')
    def test_default_build_folder_is_ignored(self):
        result=subprocess.run(['git','check-ignore','-q','production/build/proof/landscape_contact.jpg'],cwd=PROJECT)
        self.assertEqual(result.returncode,0,'production/build/ must be gitignored')


if __name__=='__main__':
    unittest.main()
