"""One connected, textured paper mesh folding into a coarse origami wad.

The sheet retains a single shared-vertex topology at every pose. Its triangular
facets bend, overlap and shade; no triangle receives an independent trajectory.
Poses are cached as transparent PNGs so the existing image-layer trace remains
usable without a custom AE mesh/warp implementation.
"""
from __future__ import annotations

import hashlib
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter
from art import skia, skimg, rgba

POSE_COUNT=13
RENDERER_VERSION='connected-origami-v1'


def clamp(value):return max(0,min(1,value))
def ease(value):value=clamp(value);return value*value*(3-2*value)


def shared_mesh(width,height,seed):
    """A single triangulated rectangle; every adjoining face shares its vertices."""
    xs=[-.5,-.38,-.26,-.16,0,.16,.26,.38,.5]
    ys=[-.5,-.35,-.16,0,.16,.35,.5]
    rng=random.Random(seed)
    vertices=[]
    for j,y in enumerate(ys):
        for i,x in enumerate(xs):
            vertices.append(dict(x=x*width,y=y*height,u=(x+.5)*width,v=(y+.5)*height,
                ridge=(1 if (i+j)%2 else -.72)*rng.uniform(.65,1),
                driftx=rng.uniform(-1,1),drifty=rng.uniform(-1,1),row=j,col=i))
    faces=[];columns=len(xs)
    for j in range(len(ys)-1):
        for i in range(columns-1):
            a=j*columns+i;b=a+1;c=a+columns+1;d=a+columns
            faces.extend([(a,b,d),(b,c,d)] if (i+j)%2 else [(a,b,c),(a,c,d)])
    return vertices,faces


def folded_vertices(vertices,width,height,progress,sign):
    p=clamp(progress)
    left=math.radians(159)*ease(p/.67)
    right=math.radians(147)*ease((p-.10)/.74)
    top=math.radians(153)*ease((p-.25)/.70)
    bottom=math.radians(141)*ease((p-.34)/.66)
    squeeze=ease((p-.22)/.78)
    corrugation=ease((p-.25)/.75)
    yaw=sign*math.radians(13)*p;pitch=math.radians(17)*p
    roll=sign*math.radians(9)*p
    result=[]
    for vertex in vertices:
        x,y=vertex['x'],vertex['y'];z=0
        if x<-.16*width:
            distance=x+.16*width
            x=-.16*width+distance*math.cos(left);z-=distance*math.sin(left)
        elif x>.16*width:
            distance=x-.16*width
            x=.16*width+distance*math.cos(right);z+=distance*math.sin(right)
        if y<-.16*height:
            distance=y+.16*height
            y=-.16*height+distance*math.cos(top);z-=distance*math.sin(top)
        elif y>.16*height:
            distance=y-.16*height
            y=.16*height+distance*math.cos(bottom);z+=distance*math.sin(bottom)
        x*=1-.53*squeeze;y*=1-.37*squeeze;z*=1-.42*squeeze
        x+=width*.018*corrugation*vertex['driftx']
        y+=height*.019*corrugation*vertex['drifty']
        z+=min(width,height)*.074*corrugation*vertex['ridge']
        # A modest change of viewpoint exposes folded-over backs and ridges.
        xx=x*math.cos(yaw)+z*math.sin(yaw);zz=-x*math.sin(yaw)+z*math.cos(yaw)
        yy=y*math.cos(pitch)-zz*math.sin(pitch);zz=y*math.sin(pitch)+zz*math.cos(pitch)
        x=xx*math.cos(roll)-yy*math.sin(roll);y=xx*math.sin(roll)+yy*math.cos(roll)
        perspective=4*max(width,height)/(4*max(width,height)-zz)
        result.append((width*.5+x*perspective,height*(.5-.035*p)+y*perspective,zz))
    return result


def normal(points):
    a,b,c=points
    u=[b[i]-a[i] for i in range(3)];v=[c[i]-a[i] for i in range(3)]
    n=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])
    length=math.sqrt(sum(value*value for value in n))
    return tuple(value/length for value in n) if length else (0,0,1)


def polygon_path(points):
    path=skia.Path();path.moveTo(points[0][0],points[0][1])
    for point in points[1:]:path.lineTo(point[0],point[1])
    path.close();return path


def render_pose(snapshot,vertices,faces,progress,sign):
    width,height=snapshot.size
    if progress==0:return snapshot.copy(),dict(alphaConnectedSheet=True,facets=len(faces))
    projected=folded_vertices(vertices,width,height,progress,sign)
    # The entire connected silhouette casts one shadow, not one shadow per face.
    mask=Image.new('L',(width,height),0);mask_draw=ImageDraw.Draw(mask)
    for face in faces:mask_draw.polygon([(projected[i][0],projected[i][1]) for i in face],fill=255)
    shadow=Image.new('RGBA',(width,height),(28,23,15,0))
    shadow.putalpha(mask.filter(ImageFilter.GaussianBlur(9+8*progress)).point(lambda value:round(value*.36)))
    target=Image.new('RGBA',(width,height),(0,0,0,0));target.alpha_composite(shadow,(round(12+9*progress),round(17+9*progress)))
    surface=skia.Surface(width,height);canvas=surface.getCanvas();canvas.clear(skia.ColorTRANSPARENT)
    canvas.drawImage(skimg(target),0,0)
    # Flat paper underfill prevents subpixel raster cracks along shared edges.
    for face in faces:
        canvas.drawPath(polygon_path([projected[i] for i in face]),skia.Paint(Color=rgba('#ddcda9'),AntiAlias=True))
    texture=skimg(snapshot).makeShader(skia.TileMode.kClamp,skia.TileMode.kClamp,
        skia.SamplingOptions(skia.FilterMode.kLinear))
    paint=skia.Paint(Shader=texture,AntiAlias=True)
    ordered=sorted(faces,key=lambda face:sum(projected[i][2] for i in face)/3)
    light=(-.43,-.48,.764)
    strength=ease(progress/.24)
    backfaces=0
    for face in ordered:
        points=[projected[i] for i in face];n=normal(points)
        back=n[2]<0
        if back:n=tuple(-value for value in n);backfaces+=1
        incidence=max(0,sum(a*b for a,b in zip(n,light)))
        brightness=1-strength*(.36*(1-incidence))
        tone=round(255*brightness)
        mesh=skia.Vertices.MakeCopy(skia.Vertices.kTriangles_VertexMode,
            [skia.Point(point[0],point[1]) for point in points],
            [skia.Point(vertices[i]['u'],vertices[i]['v']) for i in face],
            [skia.ColorSetARGB(255,tone,tone,tone)]*3)
        canvas.drawVertices(mesh,paint,skia.BlendMode.kModulate)
        path=polygon_path(points)
        if back:
            # A turned-over sheet shows its warm paper reverse, with faint print.
            paper=tuple(round(value*(.71+.29*incidence)) for value in (231,216,182))
            canvas.drawPath(path,skia.Paint(Color=rgba(paper,.88*strength),AntiAlias=True))
        elif incidence>.65:
            canvas.drawPath(path,skia.Paint(Color=rgba('#fff0ca',.10*strength*(incidence-.65)/.35),AntiAlias=True))
        # Fine dark valley / light ridge edges read as creases, never gaps.
        crease_alpha=strength*(.12 if back else .095)
        edgepaint=skia.Paint(Color=rgba('#3e3120',crease_alpha),AntiAlias=True,
            Style=skia.Paint.kStroke_Style,StrokeWidth=max(1,width/1500))
        canvas.drawPath(path,edgepaint)
    image=Image.fromarray(surface.makeImageSnapshot().toarray(colorType=skia.ColorType.kRGBA_8888_ColorType))
    return image,dict(alphaConnectedSheet=True,facets=len(faces),backFacingFacets=backfaces)


def prepare_fold_cache(snapshot,folder,source_scene,frozen_time,format_name):
    """Refresh from current scene pixels; never reuse a stale geometry snapshot."""
    folder=Path(folder);folder.mkdir(parents=True,exist_ok=True)
    source_hash=hashlib.sha256(snapshot.tobytes()).hexdigest()
    renderer_hash=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    manifest=folder/'folds.json'
    cached=json.loads(manifest.read_text()) if manifest.exists() else None
    valid=cached and cached.get('source_sha256')==source_hash and cached.get('renderer_sha256')==renderer_hash
    if valid:
        valid=all((folder/pose['file']).is_file() for pose in cached['poses'])
    if not valid:
        snapshot.save(folder/'source_snapshot.png')
        vertices,faces=shared_mesh(*snapshot.size,seed=5197+source_scene*101)
        sign=1 if source_scene==3 else -1
        poses=[]
        for index in range(POSE_COUNT):
            progress=index/(POSE_COUNT-1)
            image,details=render_pose(snapshot,vertices,faces,progress,sign)
            box=image.getbbox() or (0,0,1,1)
            image=image.crop(box)
            name=f'fold_{index:02d}.png';temporary=folder/(name+'.tmp')
            image.save(temporary,format='PNG');temporary.replace(folder/name)
            poses.append(dict(file=name,progress=progress,
                anchor=[snapshot.width*.5-box[0],snapshot.height*.5-box[1]],
                width=image.width,height=image.height,**details))
        cached=dict(renderer=RENDERER_VERSION,renderer_sha256=renderer_hash,source_sha256=source_hash,
            source_scene=source_scene,frozen_story_time=frozen_time,format=format_name,
            width=snapshot.width,height=snapshot.height,topology=dict(vertices=len(vertices),facets=len(faces),
                connectedComponents=1,independentFragmentTrajectories=0),poses=poses)
        temporary=manifest.with_suffix('.json.tmp');temporary.write_text(json.dumps(cached,indent=2)+'\n');temporary.replace(manifest)
    return cached
