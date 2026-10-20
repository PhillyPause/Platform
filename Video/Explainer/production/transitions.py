"""Varied paper-stage handoffs; all visible parts export as ordinary AE layers."""
from __future__ import annotations
import hashlib,json,math,random,sys
from contextlib import contextmanager
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from art import skia,skimg,rgba
import paper_crumple,paths
from paper_crumple import prepare_fold_cache

# How far a live scene snapshot may sit from R5's own and still be the same
# picture. Rasterisers on different machines move edge pixels by a few levels
# (here, at most 3/255, mean under .07); any real edit to the artwork moves
# them by far more. The mean bound catches a faint all-over tint.
FOLD_MATCH=dict(max_delta=8,mean_delta=.25)

SPECS={
 1:dict(kind='tilted_slide',duration=.64,dx=1.15,dy=.66,angle=-13,label='Tilted slide from lower right'),
 2:dict(kind='tumble',duration=.82,sign=1,dx=.74,dy=1.10,label='Clockwise tumble from below'),
 3:dict(kind='crumble',duration=.88,direction='down',label='Connected paper folds into a wad and drops'),
 4:dict(kind='slide',duration=.55,dx=0,dy=1.18,angle=0,label='Rise from below'),
 5:dict(kind='tilted_slide',duration=.66,dx=-1.15,dy=-.65,angle=12,label='Tilted slide from upper left'),
 6:dict(kind='slide',duration=.62,dx=1.12,dy=0,angle=0,label='Right-to-left slide'),
 7:dict(kind='tumble',duration=.76,sign=-1,dx=-.92,dy=.82,label='Counterclockwise tumble'),
 8:dict(kind='crumble',duration=.78,direction='left',label='Ledger crumples as one sheet and tumbles left'),
 9:dict(kind='tilted_slide',duration=.68,dx=-1.13,dy=.72,angle=-12,label='Tilted slide from lower left'),
10:dict(kind='slide',duration=.72,dx=0,dy=-1.16,angle=0,label='Drop from above'),
11:dict(kind='tumble',duration=.76,sign=1,dx=1.08,dy=-.84,label='Tumble from upper right'),
12:dict(kind='tilted_slide',duration=.66,dx=1.12,dy=-.70,angle=13,label='Tilted slide from upper right'),
13:dict(kind='lift',duration=.34,label='Quick upward paper lift before JOIN'),
14:dict(kind='slide',duration=.70,dx=-1.14,dy=0,angle=0,label='Left-to-right slide'),
15:dict(kind='landing',duration=.52,label='Closing card settles flat'),
}

def clamp(x):return max(0,min(1,x))
def ease(x):x=clamp(x);return x*x*(3-2*x)

@contextmanager
def story_pose(film,t):
    """Use the requested story clock, restoring even an initially absent pose_t."""
    existed=hasattr(film,'pose_t');previous=getattr(film,'pose_t',None)
    film.pose_t=t
    try:yield
    finally:
        if existed:film.pose_t=previous
        elif hasattr(film,'pose_t'):del film.pose_t

def scene_snapshot(film,index,t):
    """Freeze a source scene without disturbing the live trace or renderer state."""
    names=('c','m','alpha','prefix','t','record','_last_track_in_frame')
    state={n:getattr(film,n) for n in names};old_used=set(film.used)
    try:
        surface=skia.Surface(film.W,film.H)
        film.c=surface.getCanvas();film.c.clear(rgba('#ad9873'))
        film.m=(1,0,0,1,0,0);film.alpha=1;film.prefix='snapshot.';film.record=False
        module=sys.modules.get(type(film).__module__)
        film.t=t+float(getattr(module,'INTRO_SECONDS',0))
        with story_pose(film,t):
            film.backdrop(t,44.9<t<54)
            film.scene_content(index,t)
        return Image.fromarray(surface.makeImageSnapshot().toarray(colorType=skia.ColorType.kRGBA_8888_ColorType))
    finally:
        for n,v in state.items():setattr(film,n,v)
        film.used=old_used

def baseline_folds(snapshot,folder,si,frozen_t,format_name):
    """R5's frozen fold poses, if they were folded from this same picture; else None.

    The R5 manifest keys its poses on an exact pixel hash, which only the
    machine that made them can reproduce. Here the folding code must match
    exactly and the snapshot only within FOLD_MATCH.
    """
    manifest=folder/'folds.json'
    if not (paths.USE_BASELINE and manifest.is_file()):return None
    data=json.loads(manifest.read_text())
    if (data.get('renderer_sha256')!=paths.text_sha256(paper_crumple.__file__)
        or (data.get('source_scene'),data.get('format'))!=(si,format_name)
        or abs(data.get('frozen_story_time',-1)-frozen_t)>1e-9
        or not all((folder/pose['file']).is_file() for pose in data['poses'])):return None
    frozen=np.asarray(Image.open(folder/'source_snapshot.png').convert('RGBA'),dtype=np.int16)
    live=np.asarray(snapshot.convert('RGBA'),dtype=np.int16)
    if frozen.shape!=live.shape:return None
    delta=np.abs(frozen-live)
    stats=dict(max_delta=int(delta.max()),mean_delta=round(float(delta.mean()),5))
    if stats['max_delta']>FOLD_MATCH['max_delta'] or stats['mean_delta']>FOLD_MATCH['mean_delta']:return None
    data['provenance']=dict(origin='baseline',**stats)
    return data

def prepare_fragments(film,starts):
    """Retained interface: prepare whole connected-sheet folds, never fragments."""
    results={}
    for si,spec in SPECS.items():
        if spec['kind']!='crumble':continue
        frozen_t=(math.ceil(starts[si]*24)-1)/24
        snapshot=scene_snapshot(film,si-1,frozen_t)
        name=f'{film.format}_{si:02d}'
        folder=paths.BASELINE_FOLDS/name
        data=baseline_folds(snapshot,folder,si,frozen_t,film.format)
        if data is None:
            # Folded afresh into BUILD, keyed there by this machine's exact snapshot.
            folder=paths.output('transitions','crumple_r5',name)
            data=prepare_fold_cache(snapshot,folder,si,frozen_t,film.format)
            data['provenance']=dict(origin='generated')
        for i,pose in enumerate(data['poses']):
            asset=f'crumple_r5_{film.format}_{si}_{i}'
            film.bank.load(asset,folder/pose['file']);pose['asset']=asset
        results[si]=data
    return results

def card(film,index,t,tag,dx=0,dy=0,angle=0,scale=1,alpha=1):
    """A paper scene rotates around its centre; all original parts stay separate."""
    with film.group(film.W*.5+dx,film.H*.5+dy,scale=scale,angle=angle,alpha=alpha):
        with film.group(-film.W*.5,-film.H*.5):
            with film.group(prefix=tag):
                film.draw('paper','transition_paper_'+film.format,film.W*.5,film.H*.5)
                film.clouds(t,44.9<t<54)
                if 'historic_philadelphia_skyline' in film.bank.items:
                    film.draw('skyline','historic_philadelphia_skyline',film.W*.5-t*2,film.H*.49,film.W/2400,alpha=.37)
            with film.group(prefix=f'scene{index+1:02d}'):
                with story_pose(film,t):film.scene_content(index,t)

def handoff(film,si,t,starts):
    spec=SPECS[si];u=clamp((t-starts[si])/spec['duration']);q=ease(u)
    old_t=(math.ceil(starts[si]*24)-1)/24
    old,new=si-1,si
    kind=spec['kind'];w,h=film.W,film.H
    if kind=='crumble':
        with film.group(prefix=f'scene{si+1:02d}'):
            with story_pose(film,t):film.scene_content(new,t)
        # Geometry advances on twos. A single connected sheet folds corner by
        # corner, develops angular creases, then leaves as one compact wad.
        poses=film.fragments[si]['poses']
        beat=math.floor(max(0,t-starts[si])*12+1e-7)/12
        fold=clamp(beat/(spec['duration']*.70))
        index=min(len(poses)-1,round(fold*(len(poses)-1)))
        pose=poses[index]
        exit_progress=ease((u-.70)/.30)
        dx=w*.11*math.sin(exit_progress*math.pi)
        dy=h*1.09*exit_progress
        spin=38*exit_progress
        if spec['direction']=='left':
            dx=-w*1.12*exit_progress;dy=h*.19*exit_progress
            spin=-67*exit_progress
        with film.group(prefix=f'crumble{si:02d}'):
            film.draw(f'connected_sheet_pose{index:02d}',pose['asset'],w*.5+dx,h*.5+dy,
                scale=1-.16*exit_progress,angle=spin,anchor=pose['anchor'])
        return
    if kind in ('slide','tilted_slide'):
        dx,dy=spec['dx'],spec['dy'];a=spec['angle']
        card(film,old,old_t,f'paper{si:02d}out',-dx*w*q*.78,-dy*h*q*.78,-a*q*.72,1-.05*q)
        card(film,new,t,f'paper{si:02d}in',dx*w*(1-q),dy*h*(1-q),a*(1-q),1-.04*(1-q))
    elif kind=='tumble':
        sign=spec['sign'];dx,dy=spec['dx'],spec['dy']
        card(film,old,old_t,f'paper{si:02d}out',-dx*w*q*.75,dy*h*q*.66,sign*112*q,1-.34*q)
        card(film,new,t,f'paper{si:02d}in',dx*w*(1-q),dy*h*(1-q),-sign*112*(1-q),.67+.33*q)
    elif kind=='lift':
        with film.group(prefix=f'scene{si+1:02d}'):film.scene_content(new,t)
        card(film,old,old_t,f'paper{si:02d}out',w*.16*q,-h*1.30*q,-12*q,1-.10*q)
    elif kind=='landing':
        card(film,old,old_t,f'paper{si:02d}out',-w*.95*q,-h*.38*q,-24*q,1-.10*q)
        card(film,new,t,f'paper{si:02d}in',w*.15*(1-q),h*1.10*(1-q),8*(1-q),1+.045*(1-q))

