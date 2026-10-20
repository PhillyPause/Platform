"""A continuous 24 fps paper-theatre film with articulated motion on twos.

Render: python3 film.py --format landscape --width 1920 --output picture.mp4
Proofs: python3 film.py --proof   (written to build/proof/, with a provenance record)
The same draw calls optionally record fully separated image layers for AE.
Nothing here writes outside build/ inside production/; see paths.py.
"""
from __future__ import annotations
import sys, math, json, argparse, subprocess, time, random, heapq
from contextlib import contextmanager
from pathlib import Path
from art import *
import paths
from transitions import SPECS as TRANSITIONS, prepare_fragments, handoff

FPS=24
INTRO_SECONDS=1.0
END_START=1547/24
STORY_END_HOLD_START=65.0
END_HOLD_START=INTRO_SECONDS+STORY_END_HOLD_START
END_HOLD_SECONDS=10.0
DURATION=END_HOLD_START+END_HOLD_SECONDS
STARTS=[0,6.810,12.210,19.410,25.100,26.610,30.790,32.920,35.900,40.210,44.950,49.700,52.320,53.830,60.720,END_START]
ENDS=STARTS[1:]+[DURATION-INTRO_SECONDS]

def clamp(x,a=0,b=1):return max(a,min(b,x))
def ease(x):x=clamp(x);return x*x*(3-2*x)
def lerp(a,b,t):return a+(b-a)*t
def swing(t,speed=3,amp=1,phase=0):return math.sin(t*speed+phase)*amp
def sample(t):return math.floor(t*12+1e-5)/12
def mul(m,n):
 a,b,c,d,x,y=m;e,f,g,h,u,v=n
 return a*e+c*f,b*e+d*f,a*g+c*h,b*g+d*h,a*u+c*v+x,b*u+d*v+y
def matrix(x=0,y=0,sx=1,sy=1,angle=0):
 r=math.radians(angle);c=math.cos(r);s=math.sin(r)
 return c*sx,s*sx,-s*sy,c*sy,x,y
def point(m,x,y):a,b,c,d,tx,ty=m;return a*x+c*y+tx,b*x+d*y+ty
def inverse(m):
 a,b,c,d,x,y=m;det=a*d-b*c
 if abs(det)<1e-12:raise ValueError('Cannot attach a point through a zero-scale group')
 return d/det,-b/det,-c/det,a/det,(c*y-d*x)/det,(b*x-a*y)/det

class Film:
 def __init__(self,format='landscape',record=False):
  self.bank=build_bank();self.format=format
  self.W,self.H={'landscape':(1920,1080),'square':(1440,1440),'vertical':(1080,1920)}[format]
  self.P=self.H/self.W>1.25;self.Q=format=='square';self.u=1 if not self.P else .93
  self.c=None;self.m=(1,0,0,1,0,0);self.alpha=1.;self.prefix='';self.t=0;self.record=record
  self.tracks={};self.order_count=0;self.used=set();self.lines={}
  self.order_edges=set();self._last_track_in_frame=None
  self.setup_assets()
  self.fragments=prepare_fragments(self,STARTS)
 def setup_assets(self):
  b=self.bank
  self.labels={}
  for key,txt,w,h,size,font,colour in [
   ('race','THE RACE',540,105,54,'wood',INK),
   ('your','YOUR JOB',510,126,66,'wood',INK),
   ('my','MY JOB',450,126,71,'wood',OXBLOOD),
   ('every','EVERY JOB',580,145,77,'wood',OXBLOOD),
   ('super','SUPERINTELLIGENCE',1170,175,76,'wood',INK),
   ('human','HUMAN-REPLACEMENT AI',1170,180,73,'wood',OXBLOOD),
   ('july','JULY',400,135,86,'wood',OXBLOOD),
   ('openai','OpenAI\nEXPERIMENTAL WORKS',580,125,37,'serif',INK),
   ('1000','1,000+',405,150,94,'wood',LIGHT),
   ('700','700',290,148,103,'wood',LIGHT),
   ('isolated','EACH IN ITS OWN JAR',730,85,34,'serif',INK),
   ('orders','INSTRUCTIONS',520,102,51,'wood',INK),
   ('blank','No such instruction',455,85,32,'italic',INK),
   ('today',"TODAY’S AI",890,172,104,'wood',OXBLOOD),
   ('test','TEST',350,122,76,'wood',LIGHT),
   ('pause','PAUSE',710,170,111,'wood',OXBLOOD),
   ('join','JOIN PHILLYPAUSE',1060,163,75,'wood',OXBLOOD),
   ('share','SHARE',425,132,73,'wood',INK),
   ('congress','TELL CONGRESS',845,130,71,'wood',OXBLOOD),
   ('safe','No superintelligence\nuntil we know it will be safe',1150,190,50,'serif',INK),
   ('us',"THIS TIME, IT’S US",1000,155,68,'wood',INK),
   ('url','phillypause.org',1040,156,91,'serif',INK),
   ('cta','SHARE THIS VIDEO',646,104,47,'serif',INK),
   ('scan','SCAN TO JOIN',520,104,47,'serif',INK),
  ]:
   self.labels[key]=b.label(txt,w,h,size,font,colour,('#423b2c' if key in ['1000','700','test'] else LIGHT))
  def table(a):
   a.rect(0,0,870,47,'#7d6140','linen',INK,4)
   for x in [62,742]:a.poly([(x,44),(x+36,44),(x+69,330),(x+30,330)],'#58462e','linen',INK,3)
   a.line([(115,231),(803,231)],'#6d5434',21)
  b.make('worktable',870,340,table)
  def blank_scroll(a):
      # Every side/cap join shares these exact coordinates.
      L,R,T,B,ry=12,708,48,752,36
      cx=(L+R)/2;rx=(R-L)/2;k=0.5522847498307936
      outer=skia.Path();outer.moveTo(L,T)
      outer.cubicTo(L,T-k*ry,cx-k*rx,T-ry,cx,T-ry)
      outer.cubicTo(cx+k*rx,T-ry,R,T-k*ry,R,T)
      outer.lineTo(R,B)
      outer.cubicTo(R,B+k*ry,cx+k*rx,B+ry,cx,B+ry)
      outer.cubicTo(cx-k*rx,B+ry,L,B+k*ry,L,B)
      outer.close()
      a.shape(outer,'#dfcda6','paper',None)
      # End-cap fills have no separate floating outer strokes.
      a.ellipse(L,T-ry,R-L,2*ry,'#c1a777','paper',None)
      a.ellipse(L,B-ry,R-L,2*ry,'#c1a777','paper',None)
      # Two internal half-arcs meet the vertical sides at L/R,T/B.
      a.curve([L,T,L,T+k*ry,cx-k*rx,T+ry,cx,T+ry],INK,2)
      a.curve([cx,T+ry,cx+k*rx,T+ry,R,T+k*ry,R,T],INK,2)
      a.curve([L,B,L,B-k*ry,cx-k*rx,B-ry,cx,B-ry],INK,2)
      a.curve([cx,B-ry,cx+k*rx,B-ry,R,B-k*ry,R,B],INK,2)
      a.c.drawPath(outer,skia.Paint(Color=rgba(INK),AntiAlias=True,
          Style=skia.Paint.kStroke_Style,StrokeWidth=2,
          StrokeJoin=skia.Paint.kRound_Join))
      # Original content and baselines are unchanged.
      a.text('INSTRUCTIONS',359,137,49,'wood',INK,width=595)
      a.line([(70,168),(649,168)],INK,2)
      for y in [610,633,656]:a.line([(150,y),(584,y)],INK,1,.16)
  b.make('blank_scroll_r5',720,800,blank_scroll)
  def calendar(a):
   a.rect(0,0,520,570,'#b79660','linen',INK,3)
   a.poly([(20,43),(496,48),(500,546),(10,552)],LIGHT,'paper',INK,2)
   a.text('JULY',262,194,111,'wood',OXBLOOD,width=434)
   a.text('2026',262,284,70,'serif',INK)
   for y in range(342,490,49):
    for x in range(71,468,66):a.line([(x-16,y),(x+16,y)],INK,3,.5)
   a.rivet(79,24,10);a.rivet(442,24,10)
  b.make('calendar',526,580,calendar)
  def torn(a):
   a.poly([(0,0),(620,0),(630,75),(593,143),(619,184),(578,257),(616,308),(586,355),(622,431),(580,500),(608,580),(0,580)],'#cdb686','paper',INK,1)
  b.make('tornpage',640,580,torn)
  def thread(a):a.line([(0,10),(1000,10)],ELECTRIC,3,.65)
  b.make('thread',1000,24,thread,pad=0,shadow=False)
  def scribble(a):
   rng=random.Random(412)
   for y in [20,41,63,82]:
    pts=[(x,y+rng.uniform(-5,5)) for x in range(2,342,8)];a.line(pts,INK,2,.8)
  b.make('scribble',349,95,scribble,pad=0,shadow=False)
  def crossout(a):
   a.line([(0,25),(365,0),(18,52),(352,18)],OXBLOOD,5,.9)
  b.make('crossout',378,70,crossout,pad=0,shadow=False)
  def rodglow(a):
   conductor=[(52,0),(53,605),(55,784),(125,826),(132,905)]
   a.line(conductor,ELECTRIC,25,.15)
   a.line(conductor,'#fff3b3',4,.9)
  b.make('rodglow_r5',200,950,rodglow,pad=35,shadow=False)
  def floor(a):
   a.poly([(0,150),(1760,0),(2090,320),(60,500)],'#856d47','linen',INK,3)
   for k in range(12):a.line([(100+k*151,440),(50+k*145,50)],'#483d2c',2,.5)
  b.make('platform',2100,500,floor,pad=20)
  # A single original automaton template is repeated exactly 700 times, in four depth plates.
  def template(a):
   for name,x,y,w,h in [('auto_shin',38,257,32,89),('auto_shin',97,257,32,89),('auto_thigh',35,186,45,91),('auto_thigh',90,186,45,91),('auto_torso',23,79,137,179),('auto_head',47,0,89,94),('auto_upperarm',2,99,35,92),('auto_upperarm',140,99,35,92),('auto_forearm',5,183,31,84),('auto_forearm',138,183,31,84)]:
    a.c.drawImageRect(b.get(name),skia.Rect.MakeXYWH(x,y,w,h),skia.SamplingOptions(skia.FilterMode.kLinear))
  b.make('auto_template',180,350,template,pad=0,shadow=False)
  for depth in range(4):
   def crowd(a,depth=depth):
    rng=random.Random(91+depth)
    for row in range(5):
     for col in range(35):
      h=45+row*5;w=h*.5;x=col*42+(row%2)*17;y=row*46+rng.uniform(-3,3)
      a.c.drawImageRect(b.get('auto_template'),skia.Rect.MakeXYWH(x,y,w,h),skia.SamplingOptions(skia.FilterMode.kLinear))
   b.make('crowd_'+str(depth),1500,280,crowd,pad=0,shadow=False)
  # Broad, soft vignette as its own editable layer.
  def vig(a):
   shader=skia.GradientShader.MakeRadial((self.W*.5,self.H*.47),math.hypot(self.W,self.H)*.59,[rgba('#161c1b',0),rgba('#151a19',.13),rgba('#111717',.76)],[0,.56,1])
   a.c.drawRect(skia.Rect.MakeWH(self.W,self.H),skia.Paint(Shader=shader))
  b.make('vignette_'+self.format,self.W,self.H,vig,pad=0,shadow=False)
  def transition_paper(a):
   im=b.get('backdrop');s=max(self.W/im.width(),self.H/im.height())*1.06
   a.c.drawImageRect(im,skia.Rect.MakeXYWH((self.W-im.width()*s)/2,(self.H-im.height()*s)/2,im.width()*s,im.height()*s),skia.SamplingOptions(skia.FilterMode.kLinear))
  b.make('transition_paper_'+self.format,self.W,self.H,transition_paper,pad=0,shadow=False)
  ew,eh=(960,1780) if self.P else ((1300,1300) if self.Q else (1680,960))
  def endpaper(a):
   rng=random.Random(9160);pts=[]
   for x in range(0,ew+1,40):pts.append((x,rng.uniform(0,5)))
   pts.extend([(ew-1,eh-7),(ew*.72,eh-1),(ew*.31,eh-5),(6,eh-2),(0,eh*.45)])
   a.poly(pts,LIGHT,'paper','#604d35',2)
   a.line([(40,37),(ew-43,39)],INK,1,.3)
  b.make('endpaper_qr_'+self.format,ew,eh,endpaper,pad=24,shadow=True)
  def copyright_notice(a):
   a.text('© 2026 PhillyPause',300,39,27,'serif',LIGHT)
  b.make('endcopyright_r3',600,60,copyright_notice,pad=0,shadow=False)

 @contextmanager
 def group(self,x=0,y=0,scale=1,angle=0,sx=None,sy=None,alpha=1,prefix=None):
  om,oa,op=self.m,self.alpha,self.prefix
  self.m=mul(self.m,matrix(x,y,scale if sx is None else sx,scale if sy is None else sy,angle));self.alpha*=alpha
  if prefix is not None:self.prefix+=prefix+'.'
  self.c.save();self.c.translate(x,y);self.c.rotate(angle);self.c.scale(scale if sx is None else sx,scale if sy is None else sy)
  try:yield
  finally:self.c.restore();self.m,self.alpha,self.prefix=om,oa,op
 def draw(self,id,asset,x,y,scale=1,angle=0,alpha=1,anchor=None,sx=None,sy=None):
  if alpha*self.alpha<=.001:return
  im=self.bank.get(asset);pad=self.bank.meta[asset].get('pad',0)
  if anchor is None:anchor=(im.width()/2,im.height()/2)
  elif anchor=='top':anchor=(pad,pad)
  elif anchor=='base':anchor=(im.width()/2,im.height()-pad)
  sx=scale if sx is None else sx;sy=scale if sy is None else sy
  self.c.save();self.c.translate(x,y);self.c.rotate(angle);self.c.scale(sx,sy)
  self.c.drawImage(im,-anchor[0],-anchor[1],skia.SamplingOptions(skia.FilterMode.kLinear),skia.Paint(Alphaf=alpha*self.alpha))
  self.c.restore();self.used.add(asset)
  if self.record:
   mid=mul(self.m,matrix(x,y,sx,sy,angle));a,b,c,d,px,py=mid
   ssx=math.hypot(a,b);ssy=(a*d-b*c)/ssx if ssx else 0;r=math.degrees(math.atan2(b,a))
   name=self.prefix+id;key=[round(self.t,6),round(px,3),round(py,3),round(ssx*100,4),round(ssy*100,4),round(r,4),round(alpha*self.alpha*100,3)]
   if name not in self.tracks:
    self.tracks[name]={'id':name,'asset':asset,'anchor':list(anchor),'order':self.order_count,'keys':[]};self.order_count+=1
   tr=self.tracks[name]
   if tr['asset']!=asset:
    name+='.'+asset
    if name not in self.tracks:self.tracks[name]={'id':name,'asset':asset,'anchor':list(anchor),'order':self.order_count,'keys':[]};self.order_count+=1
    tr=self.tracks[name]
   tr['keys'].append(key)
   if self._last_track_in_frame is not None and self._last_track_in_frame!=name:
    self.order_edges.add((self._last_track_in_frame,name))
   self._last_track_in_frame=name
 def line(self,id,x1,y1,x2,y2,colour=INK,sw=3,alpha=1):
  # Each segment remains a transformable thin paper strip in the project.
  n='line_'+colour.replace('#','')+'_'+str(sw)
  if n not in self.bank.items:
   self.bank.make(n,1000,max(sw,2),lambda a:a.rect(0,0,1000,max(sw,2),colour,None,None),pad=0,shadow=False)
  length=math.hypot(x2-x1,y2-y1);angle=math.degrees(math.atan2(y2-y1,x2-x1))
  self.draw(id,n,x1,y1,sx=length/1000,sy=1,angle=angle,anchor=(0,max(sw,2)/2),alpha=alpha)
 def placed_point(self,asset,local,x,y,scale=1,angle=0,anchor=None,sx=None,sy=None):
  """Place an artwork point with exactly the same geometry as draw().

  local uses the unpadded coordinates in the asset's drawing function.
  The returned point is in the caller's current group coordinates.
  """
  im=self.bank.get(asset);pad=self.bank.meta[asset].get('pad',0)
  if anchor is None:anchor=(im.width()/2,im.height()/2)
  elif anchor=='top':anchor=(pad,pad)
  elif anchor=='base':anchor=(im.width()/2,im.height()-pad)
  return point(matrix(x,y,scale if sx is None else sx,scale if sy is None else sy,angle),
               pad+local[0]-anchor[0],pad+local[1]-anchor[1])
 def cord(self,id,start,end,sag=0,colour=INK,sw=2,alpha=1):
  """A continuous cord whose end points remain on its attachment sockets."""
  x1,y1=start;x2,y2=end;previous=start
  for i in range(1,13):
   u=i/12;current=(lerp(x1,x2,u),lerp(y1,y2,u)+math.sin(math.pi*u)*sag)
   self.line(id+'_'+str(i),*previous,*current,colour,sw,alpha)
   previous=current
 def label(self,key,x,y,scale=1,angle=0,alpha=1):self.draw('type_'+key,self.labels[key],x,y,scale,angle,alpha)
 def stamp(self,key,x,y,t,when,scale=1,angle=0):
  d=t-when
  if d<0:return
  q=clamp(d/.14);s=(1.5-.5*ease(q))*scale
  self.label(key,x,y,s,angle+math.sin(d*25)*2*math.exp(-d*8),ease(d/.045))
 def clouds(self,t,dark=False):
  if 'historic_engraved_cloud_bank' in self.bank.items:
   for i,x in enumerate([-.1,.56,1.3]):
    self.draw('cloud'+str(i),'historic_engraved_cloud_bank',self.W*x+t*7,self.H*(.12+.07*(i%2)),self.W/2100*.58,angle=-4+i*3,alpha=.20 if dark else .33)
 def backdrop(self,t,dark=False):
  im=self.bank.get('backdrop');s=max(self.W/im.width(),self.H/im.height())*1.06
  self.draw('background','backdrop',self.W*.5+math.sin(t*.03)*12,self.H*.5,s)
  self.clouds(t,dark)
  if 'historic_philadelphia_skyline' in self.bank.items:
   self.draw('skyline','historic_philadelphia_skyline',self.W*.5-t*2,self.H*.49,self.W/2400,alpha=.37)
 def town(self,t,y=None,scale=1,rods=False,burning=False):
  if y is None:y=self.H*.82
  for i in range(5):
   sc=(.56+(i%3)*.09)*scale;xx=self.W*(i/4)-80
   self.draw('house'+str(i),'house',xx,y,sc,anchor='base',angle=(i%3-1)*2)
   if rods:self.draw('roof_rod'+str(i),'rod',xx+75*sc,y-550*sc,sc*.46,anchor=(77,34),alpha=.75)
   if burning:
    for j in range(2):
     phase=sample(t)*3+i*3+j
     self.draw('town_flame'+str(i)+'_'+str(j),'flame',xx+(j-.5)*135*sc,y-220*sc,sc*(.8+.1*math.sin(phase)),math.sin(phase)*9,anchor='base')
 def ground(self,t,y=None):
  if y is None:y=self.H*.84
  self.draw('ground','ground',self.W*.5,y,self.W/2400*1.2,anchor=(1300,70))
 def puppet(self,id,kind,x,y,scale=1,walk=0,pose='idle',phase=0,angle=0,alpha=1):
  t=sample(getattr(self,'pose_t',self.t));ph=t*8+phase
  auto=kind=='auto';pref='auto' if auto else kind
  if kind=='franklin':pref='franklin'
  unparent=inverse(self.m);sockets={}
  def socket(name,xy):sockets[name]=point(mul(unparent,self.m),*xy)
  with self.group(x,y,scale,angle,alpha=alpha,prefix=id):
   sway=(math.sin(ph*.32)*2 if not walk else math.cos(ph*2)*3)
   # Feet are planted around a shared ground plane; walking changes joint angles.
   for side in [0,1]:
    hipx=(-30 if side==0 else 28);hipy=-161
    a=math.sin(ph+side*math.pi)*24*walk
    knee=12+max(0,-math.sin(ph+side*math.pi))*32*walk
    with self.group(hipx,hipy,angle=a,prefix='leg'+str(side)):
     self.draw('thigh',pref+'_thigh',0,0,anchor=(13+25,13+10))
     with self.group(0,82,angle=knee):
      self.draw('shin',pref+'_shin',0,0,anchor=(13+19,13+9))
      self.draw('shoe','auto_shoe' if auto else 'human_shoe',-15,90,scale=.89,anchor=(13+22,13+13),angle=-a-knee)
   armraise=0
   if pose=='raise':armraise=-147
   elif pose=='shrug':armraise=-91
   elif pose=='point':armraise=-78
   elif pose=='reach':armraise=-120
   elif pose=='hold':armraise=-110
   for side in [0,1]:
    shx=-73 if side==0 else 63;shy=-313
    ar=(math.sin(ph+side*math.pi)*26*walk)+(armraise if side else -armraise*.3)+sway
    if pose=='shrug':ar=(-85 if side==0 else 85)
    with self.group(shx,shy,angle=ar,prefix='arm'+str(side)):
     self.draw('upper',pref+'_upperarm',0,0,anchor=(13+22,13+10))
     elbow=(-15 if side==0 else 12)+(18*walk*math.sin(ph+side*math.pi))
     with self.group(0,88,angle=elbow):
      self.draw('fore',pref+'_forearm',0,0,anchor=(13+18,13+10))
      hand_asset='auto_hand' if auto else 'human_hand'
      self.draw('hand',hand_asset,0,85,scale=.76,anchor=(13+26,13+9))
      socket('hand'+str(side),self.placed_point(hand_asset,(26,40),0,85,scale=.76,anchor=(13+26,13+9)))
   # The spinning gear belongs to the torso and inherits its sway and pivot.
   with self.group(0,-335,angle=sway):
    self.draw('body','auto_torso' if auto else pref+'_torso',0,0,anchor=(20+(83 if auto else 80),20+5))
    if auto:
     self.draw('chestgear','gear',-1,120,scale=.19,angle=t*58,alpha=.85)
     socket('shoulder0',(-65,15));socket('shoulder1',(61,15))
     socket('chest',(-1,120))
   if kind=='franklin' and 'historic_franklin_head' in self.bank.items:
    self.draw('head','historic_franklin_head',0,-335,scale=.145,angle=sway*.7,anchor=(543,1071))
   else:
    nm='auto_head' if auto else pref+'_head';pa=16 if auto else 50
    self.draw('head',nm,-7,-335,scale=1.06,angle=sway*.8,anchor=(pa+55,pa+(115 if auto else 147)))
    crown={'auto':(60,0),'worker':(60,0),'printer':(60,0),'clerk':(49,4),'nurse':(53,-6),'tycoon':(50,-36)}.get(kind,(49,4))
    socket('head_top',self.placed_point(nm,crown,-7,-335,scale=1.06,angle=sway*.8,anchor=(pa+55,pa+(115 if auto else 147))))
  return sockets
 def jar_robot(self,id,x,y,jar_scale,pose='idle',phase=0,glass_alpha=1):
  """Fit the whole articulated robot inside the glass, feet on its platter.

  Fits cover the complete sway cycle, including the extended pointing hand.
  x/y locate the jar's base anchor; offsets are in unscaled jar coordinates.
  """
  ratio,dx,dy=(.41,-27.090,-57.021) if pose=='point' else (.43,-1.545,-57.851)
  sockets=self.puppet(id,'auto',x+dx*jar_scale,y+dy*jar_scale,
                      ratio*jar_scale,pose=pose,phase=phase)
  self.draw(id+'_glass','jar',x,y,jar_scale,anchor='base',alpha=glass_alpha)
  return sockets
 def carriage(self,id,x,y,s=1,phase=0,angle=0):
  t=sample(getattr(self,'pose_t',self.t));bob=math.sin(t*16+phase)*5
  with self.group(x,y+bob,s,angle,prefix=id):
   for i in range(3):
    q=(t*.8+i/3)%1
    self.draw('steam'+str(i),'smoke',-120-q*150, -210-q*160,.24+q*.44,angle=-q*20,alpha=(1-q)*.38)
   self.puppet('driver','tycoon',370,-62,.57,pose='point',phase=phase)
   self.draw('coach','carriage',0,0,anchor=(25,25+230))
   for k,xx in enumerate([87,432]):self.draw('wheel'+str(k),'wheel',xx,2,.73,angle=t*200+phase*50)
 def background_props(self,t):
  self.draw('farhill','hill',self.W*.56,self.H*.82,self.W/2300,anchor=(1200,160),alpha=.33)
  if 'historic_state_house_front' in self.bank.items:
   self.draw('statehouse','historic_state_house_front',self.W*.52,self.H*.73,self.W/5300,anchor='base',alpha=.77)
  self.town(t,self.H*.81,scale=.84)
 def scene01(self,t):
  self.background_props(t);self.ground(t)
  self.draw('raised_road','ground',self.W*.5,self.H*(.51 if self.P else .62),self.W/2300,anchor=(1300,70),alpha=.72)
  with self.group(-t*17,0):
   self.carriage('race_far',self.W*(.08 if self.P else .63)+t*(8 if self.P else 15),self.H*(.49 if self.P else .59),1.25 if self.P else .83,1)
   poses=['worker','printer','clerk','nurse'];when=[3.4,4.82,5.84,6.15]
   xs=[self.W*.17,self.W*.38,self.W*.60,self.W*.81]
   if self.P:xs=[self.W*.16,self.W*.41,self.W*.65,self.W*.87]
   for i,(kind,x) in enumerate(zip(poses,xs)):
    lift=ease((t-when[i])/.48);yy=self.H*.79-lift*self.H*1.2
    sockets=self.puppet('trade'+str(i),kind,x,yy,.79 if not self.P else 1.25,phase=i)
    if lift>0:
     self.line('lift'+str(i),*sockets['head_top'],x+80,-150,INK,2,.8)
   self.carriage('race_near',self.W*(.00 if self.P else .12)+t*(20 if self.P else 32),self.H*.90,1.30 if self.P else 1.08,0)
  self.stamp('your',self.W*.74,self.H*.25,t,3.2,.92 if not self.P else .73,-4)
  if t>=4.82:
   self.draw('blank_out_your','tornpage',self.W*.74,self.H*.25,.48,8,alpha=.0)
   self.stamp('my',self.W*.49,self.H*.17,t,4.82,1.08 if not self.P else .8,3)
  self.stamp('every',self.W*.53,self.H*.36,t,5.84,1.08 if not self.P else .92,-3)
 def scene02(self,t):
  self.town(t,self.H*.87,.77);self.ground(t,self.H*.88)
  cx=self.W*.5;banner_y=self.H*(.28 if not self.P else .28)
  ss=min(self.W/1330,1.22)
  self.label('human',cx,banner_y,ss,-2)
  tear=ease((t-9.25)/.5)
  if tear<1:
   with self.group((tear*self.W*1.15),-tear*self.H*.25,angle=tear*22):self.label('super',cx,banner_y,ss,-2)
  self.puppet('worker','worker',cx-self.W*.15,self.H*.82-self.H*ease((t-9.4)/.55),1.13 if not self.P else 1.30,phase=.4)
  x=lerp(self.W*1.1,cx,ease((t-9.4)/1.1))
  self.puppet('replacement','auto',x,self.H*.85,1.25 if not self.P else 2.0,walk=1-ease((t-10.2)/.7))
  self.draw('footlight','gear',self.W*.9,self.H*.97,.72,angle=t*18,alpha=.65)
 def scene03(self,t):
  sc=min(self.W/1890,self.H/1190)*.98
  if self.P:sc=self.W/1420
  by=self.H*.55
  if self.P:self.draw('almanac','sheet',self.W*.5,by,sx=.92,sy=1.75,angle=-2)
  else:self.draw('almanac','book',self.W*.5,by,sc,angle=-2)
  problem=ease((t-15.15)/.5)
  if not self.P:
   gx=lerp(self.W*.32,self.W*.66,problem);gy=self.H*.91
   robot=self.puppet('giant','auto',gx,gy,1.22,walk=.07)
   for i in range(2):
    xx=gx+(-190 if i==0 else 210);yy=self.H*.86
    maker=self.puppet('inventor'+str(i),'maker',xx,yy,.68,pose='raise' if problem<.5 else 'shrug',angle=(-problem*18 if i==0 else problem*17),phase=i)
    target=robot['shoulder'+str(i)]
    hand=min((maker['hand0'],maker['hand1']),key=lambda p:math.hypot(p[0]-target[0],p[1]-target[1]))
    self.cord('control'+str(i),hand,target,sag=lerp(4,75,problem))
  else:
   robot=self.puppet('giant','auto',self.W*.53,self.H*.84,2.10)
   for i in range(2):
    xx=self.W*(.25 if i==0 else .79)
    maker=self.puppet('inventor'+str(i),'maker',xx,self.H*.90,.82,pose='raise' if problem<.5 else 'idle',angle=problem*(14 if i else -14))
    target=robot['shoulder'+str(i)]
    hand=min((maker['hand0'],maker['hand1']),key=lambda p:math.hypot(p[0]-target[0],p[1]-target[1]))
    self.cord('control'+str(i),hand,target,sag=lerp(4,85,problem))
   label=self.bank.label('THE PROBLEM' if problem>.5 else 'THE PLAN',890,145,84)
   self.draw('pageheading',label,self.W*.5,self.H*.24,.94)
  self.draw('front_gear','gear',self.W*.94,self.H*.91,.75,angle=-t*14,alpha=.7)
 def scene04(self,t):
  p=ease((t-19.41)/5.5);sc=lerp(1.06,.83,p) if not self.P else 1.0
  with self.group(self.W*.52,self.H*.55,sc,angle=-1):
   self.draw('cabinet','cabinet',0,0,sx=.57 if self.P else 1,sy=1.34 if self.P else 1)
   for row in range(5 if self.P else 3):
    for col in range(4 if self.P else 8):
     xx=(-357+col*236) if self.P else (-745+col*211)
     yy=(-440+row*237) if self.P else (-210+row*274)
     self.jar_robot('isolated'+str(row)+'_'+str(col),xx,yy+76,.83,phase=row*8+col)
  calp=ease((t-20.3)/.65)
  self.draw('calendar','calendar',lerp(self.W*.22,-220,calp),lerp(self.H*.34,-400,calp),.76,angle=-calp*39,alpha=1-calp)
  self.label('openai',self.W*.49,self.H*.12,.86 if not self.P else 1)
  self.label('1000',self.W*.79 if not self.P else self.W*.5,self.H*(.88 if self.P else .83),1.02,-2)
  self.label('isolated',self.W*.37 if not self.P else self.W*.5,self.H*(.91 if self.P else .83),.72,-2)
 def scene05(self,t):
  bx=self.W*(.50 if self.P else .63);by=self.H*(.36 if self.P else .42)
  bs=1.2 if not self.P else 1.35
  self.draw('board','noticeboard',bx,by,bs,angle=2)
  # Exposed pins of notes 7,9,12,18,22..27 in the original board artwork.
  pins=[(290,175),(223,313),(341,281),(216,162),(248,349),
        (464,134),(442,279),(113,182),(392,337),(357,145)]
  for i in range(5):
   x=self.W*(.09+i*.18);y=self.H*.83
   sockets=self.jar_robot('messenger'+str(i),x,y+13,.92 if not self.P else 1.5,pose='point',phase=i,glass_alpha=.78)
   prog=ease((t-25.10-i*.1)/.4)
   for j in range(2):
    tx,ty=self.placed_point('noticeboard',pins[2*i+j],bx,by,bs,angle=2)
    hx,hy=sockets['hand1']
    self.line('thread'+str(i)+'_'+str(j),hx,hy,lerp(hx,tx,prog),lerp(hy,ty,prog),ELECTRIC,3,.84)
  self.draw('moving_note','sheet',lerp(self.W*.1,self.W*.64,ease((t-25.20)/.9)),lerp(self.H*.70,self.H*.3,ease((t-25.20)/.9)),.075,angle=t*35)
 def scene06(self,t):
  self.ground(t,self.H*.88)
  enter=ease((t-26.61)/1.0)
  bx=self.W*(.70 if not self.P else .55);by=self.H*.75
  self.draw('counting_house','counting_house',bx,by, .84 if not self.P else 1.04,anchor='base')
  # Four independently moving planes, each holding 175 copies of the same template.
  for i in range(4):
   sc=(.53+i*.12) if not self.P else (.45+i*.04)
   cx=lerp(-self.W*.8,self.W*(.30 if not self.P else .5),enter)+(sample(t)*27%29)
   self.draw('swarm_'+str(i),'crowd_'+str(i),cx,self.H*(.68+i*.058),sc,angle=-5+i*1.6)
  for i in range(10):
   p=(t* .33+i*.087)%1
   if i<5:
    xx=lerp(self.W*.07,bx-80,p);yy=self.H*.89
   else:
    xx=bx+(i-7)*54;yy=lerp(self.H*.83,self.H*.44,p)
   self.puppet('raider'+str(i),'auto',xx,yy,.44 if i<5 else .29,walk=1,pose='raise' if i>4 else 'idle',phase=i)
  self.label('700',self.W*.21 if not self.P else self.W*.50,self.H*.25,1.3 if not self.P else 1.17,-3)
  self.draw('foreground_gear','gear',self.W*.04,self.H*.92,1.1,angle=-t*22,alpha=.66)
 def scene07(self,t):
  self.draw('worktable','worktable',self.W*.48,self.H*.84,1.25 if not self.P else 1.0)
  self.puppet('maker','maker',self.W*.19 if not self.P else self.W*.24,self.H*.89,1.35 if not self.P else 1.45,pose='shrug' if not self.P else 'idle',phase=2)
  ss=.86 if not self.P else .92
  self.draw('orders','blank_scroll_r5',self.W*.64 if not self.P else self.W*.58,self.H*(.36 if self.P else .48),ss,angle=3)
  self.puppet('auto','auto',self.W*(.82 if self.P else .91),self.H*.9,.83 if not self.P else 1.05,walk=.05)
  self.draw('emptyline','scribble',self.W*.65,self.H*.60,.4,alpha=0)
 def scene08(self,t):
  sc=min(self.W/1320,self.H/1130) if not self.P else 1.32
  self.draw('ledger','ledger',self.W*.51,self.H*.53,sc,angle=-4)
  for i in range(6):
   y=self.H*.34+i*self.H*.08;xx=self.W*(.34 if i%2==0 else .65)
   self.draw('oldentry'+str(i),'scribble',xx,y,sc*.62,angle=-2)
   if t>33.3+i*.26:self.draw('crossout'+str(i),'crossout',xx,y,sc*.65,angle=-5)
  for i in range(5):
   x=self.W*.58+(i%2)*70;y=self.H*.39+i*73
   # Evidence fades only partly: attempts to cover tracks do not erase the historical record.
   al=1-.66*ease((t-34.0-i*.1)/.5)
   self.draw('footprint'+str(i),'footprint',x,y,sc,angle=25,alpha=al)
  qx=self.W*.65+math.sin(sample(t)*12)*64;qy=self.H*.35+(t-33.1)*94
  self.draw('quill','quill',qx,qy,sc*.85,angle=30,anchor=(34,439))
  self.draw('brasshand','auto_hand',qx+140*sc,qy-250*sc,sc*3,angle=35)
  self.draw('inkpot','inkblot',self.W*.83,self.H*.75,1.0)
 def scene09(self,t):
  W,H=self.W,self.H
  self.town(t,H*.91,scale=1.1);self.ground(t,H*.90)
  floor=self.bank.get('platform')
  self.draw('test_floor','platform',W*.5,H*.82,sx=W*.62/floor.width(),sy=H*.25/floor.height())
  self.puppet('specimen','auto',W*.5,H*.80,1.20*H/1080,walk=0)
  # One foreground theatre: full-height leaves share a rail and close inward.
  # Their overlap blocks the town completely, without a central light slit.
  close=ease((t-38.55)/.70)
  left=lerp(-.34,-.035,close)*W;right=lerp(1.34,1.035,close)*W
  self.draw('curtain_left','curtain_leaf_r4',left,-.04*H,sx=.54*W/1040,sy=1.12*H/1220,anchor=(0,0))
  self.draw('curtain_right','curtain_leaf_r4',right,-.04*H,sx=-.54*W/1040,sy=1.12*H/1220,anchor=(0,0))
  beam=self.bank.get('beam')
  self.draw('proscenium_rail','beam',-.02*W,-.01*H,sx=1.04*W/beam.width(),sy=.105*H/beam.height(),anchor=(0,0))
  self.draw('jamb_left','theatre_jamb_r4',-.005*W,0,sx=.025*W/50,sy=H/1280,anchor=(0,0))
  self.draw('jamb_right','theatre_jamb_r4',1.005*W,0,sx=-.025*W/50,sy=H/1280,anchor=(0,0))
  self.stamp('today',W*.5,H*.17,t,36.72,.93 if not self.P else .88,-2)
  self.label('test',W*.5,H*.92,.75,-1)
 def scene10(self,t):
  self.draw('cliff','hill',self.W*.48,self.H*.76,self.W/2000,angle=-5,anchor=(1200,145))
  self.draw('road','ground',self.W*.5-(t-40.2)*40,self.H*.78,self.W/2100,angle=-5,anchor=(1300,60))
  for i in range(5):
   x=self.W*(.10+i*.25)-(t-40.21)*80
   br=ease((t-(41.3+i*.5))/.45)
   self.draw('rail'+str(i),'rail',x+br*140,self.H*.76+br*160,.84,angle=-6+br*110,alpha=1-br*.4)
  if self.P:self.draw('upper_cliffroad','ground',self.W*.5,self.H*.55,self.W/1400,anchor=(1300,70),angle=-5,alpha=.75)
  self.carriage('leading',self.W*(.10 if self.P else .59),self.H*(.52 if self.P else .68),1.12 if self.P else .96,1,-5)
  self.carriage('racer',self.W*(.03 if self.P else .12)+(t-40.2)*(10 if self.P else 35),self.H*(.81 if self.P else .84),1.55 if self.P else 1.35,0,-4)
  self.draw('wheel_foreground','wheel',self.W*.96,self.H*1.04,1.6,angle=t*122,alpha=.65)
 def scene11(self,t):
  self.town(t,self.H*.88,.92)
  self.ground(t,self.H*.91)
  x=self.W*.27 if not self.P else self.W*.25
  franklin=self.puppet('franklin','franklin',x,self.H*.91,1.18 if not self.P else 1.75,pose='raise',phase=1)
  kx=self.W*.55+math.sin(t*1.7)*35;ky=self.H*.22
  ks=.55 if not self.P else .69;ka=math.sin(sample(t)*2)*9
  self.draw('kite','kite',kx,ky,ks,angle=ka)
  bridle=self.placed_point('kite',(145,158),kx,ky,ks,angle=ka)
  self.cord('kitestring',franklin['hand1'],bridle,sag=10,alpha=.8)
  appear=ease((t-47.0)/.5)
  rx=self.W*(.81 if not self.P else .76);ry=self.H*.24
  rs=1.00 if not self.P else 1.21
  self.draw('safetyrod','rod',rx,ry,rs,anchor=(75,24),alpha=appear)
  rod_tip=self.placed_point('rod',(52,0),rx,ry,rs,anchor=(75,24))
  if t>47.65:
   flash=math.exp(-((t-48.02)/.22)**2)+.6*math.exp(-((t-48.35)/.15)**2)
   self.draw('bolt','lightning',*rod_tip,.75,anchor=(30,608),alpha=clamp(flash))
   self.draw('grounded','rodglow_r5',rx,ry,rs,anchor=(86,35),alpha=clamp(flash*.9))
 def scene12(self,t):
  self.town(t,self.H*.86,1.11,burning=True);self.ground(t,self.H*.93)
  for i in range(5):
   self.draw('flame_fg'+str(i),'flame',self.W*(.15+i*.2),self.H*.95,(2.55 if self.P else 1.0)+(i%2)*.25,angle=math.sin(sample(t)*4+i)*11,anchor='base')
  fl=math.exp(-((t-50.05)/.19)**2)
  self.draw('strike','lightning',self.W*.32,self.H*.21,1.1,alpha=fl)
  self.carriage('indifferent',self.W*(.00 if self.P else .18)+(t-49.7)*(70 if self.P else 170),self.H*(.91 if self.P else .93),1.30 if self.P else 1.02,phase=2)
 def scene13(self,t):
  ft=min(t,53.12)
  previous_pose=getattr(self,'pose_t',None)
  self.pose_t=ft
  self.town(ft,self.H*.87,1.0,burning=True);self.ground(ft,self.H*.93)
  if self.P:
   for i in range(3):self.draw('frozenfire'+str(i),'flame',self.W*(.14+i*.35),self.H*.95,2.25,angle=math.sin(sample(ft)*4+i)*11,anchor='base')
  self.carriage('frozen_racer',self.W*.13+(ft-52.32)*100,self.H*.84,.93,2)
  if previous_pose is None:del self.pose_t
  else:self.pose_t=previous_pose
  x=self.W*(.52 if self.P else .67);y=self.H*(.66 if self.P else .7)
  self.draw('leverbase','leverbase',x,y,1.25 if not self.P else 2.05)
  pull=ease((t-52.82)/.3)
  self.draw('lever','lever',x,y,1.25 if not self.P else 2.05,angle=lerp(-28,66,pull),anchor=(24+29,24+277))
  ang=lerp(-28,66,pull);rad=math.radians(ang);ls=2.05 if self.P else 1.25
  self.draw('pulling_hand','human_hand',x+math.sin(rad)*241*ls,y-math.cos(rad)*241*ls,2.9 if self.P else 2.0,angle=ang)
  self.stamp('pause',self.W*.5,self.H*(.31 if self.P else .28),t,53.12,1.4 if not self.P else 1.15,-4)
 def scene14(self,t):
  self.draw('petition','sheet',self.W*.55,self.H*.52,1.24 if not self.P else 1.02,angle=2)
  self.draw('capitol','capitol',self.W*.80 if not self.P else self.W*.71,self.H*.72,.52 if not self.P else .54,angle=2)
  self.stamp('join',self.W*.50,self.H*.18,t,54.18,1.10 if not self.P else .87,-2)
  self.stamp('share',self.W*.29 if not self.P else self.W*.32,self.H*.35,t,55.50,1.0 if not self.P else .87,3)
  self.stamp('congress',self.W*.57 if not self.P else self.W*.50,self.H*.51,t,56.74,1.0 if not self.P else .90,-2)
  if t>=57.3:self.label('safe',self.W*.51,self.H*(.69 if self.P else .68),.88 if not self.P else .78,2,alpha=ease((t-57.3)/.3))
  self.draw('signingquill','quill',self.W*.82+math.sin(sample(t)*8)*19,self.H*.86,.35,angle=42,anchor=(34,438))
  self.ground(t,self.H*.94)
  for i,kind in enumerate(['worker','nurse','printer','clerk','citizen_dark']):
   p=ease((t-54.2-i*.24)/2.1);x=lerp(-150-i*70,self.W*(.11+i*.17),p)
   self.puppet('joiner'+str(i),kind,x,self.H*1.025,.48 if not self.P else .75,walk=1-p,phase=i)
 def scene15(self,t):
  self.town(t,self.H*.90,.97);self.ground(t,self.H*.94)
  roofY=self.H*.86
  self.draw('rooftop','platform',self.W*.52,roofY,self.W/1940,angle=-1,anchor=(1040,120))
  for i,kind in enumerate(['worker','nurse','printer','clerk','citizen_dark']):
   self.puppet('people'+str(i),kind,self.W*(.13+i*.15),roofY,.88 if not self.P else 1.05,pose='hold' if i==4 else ('point' if i==2 else 'idle'),phase=i)
  raiseP=ease((t-61.02)/1.20)
  rx=self.W*(.91 if self.P else .84);ry=lerp(self.H*.45,self.H*.18,raiseP)
  rs=.80 if not self.P else 1.2
  self.draw('peoplesrod','rod',rx,ry,rs,anchor=(75,24))
  rod_tip=self.placed_point('rod',(52,0),rx,ry,rs,anchor=(75,24))
  # The visible conductor runs beside the people, down to earth.
  flash=math.exp(-((t-62.55)/.16)**2)+.65*math.exp(-((t-62.85)/.16)**2)
  self.draw('lightning','lightning',*rod_tip,.86,anchor=(30,608),alpha=clamp(flash))
  self.draw('conductor','rodglow_r5',rx,ry,rs,anchor=(86,35),alpha=clamp(flash))
  if t>63.05:self.label('us',self.W*.48,self.H*.25,.96 if not self.P else .89,-2,alpha=ease((t-63.05)/.2))
 def scene16(self,t):
  if 'historic_state_house_front' in self.bank.items:
   self.draw('townending','historic_state_house_front',self.W*.5,self.H*1.08,self.W/2300,alpha=.24)
  self.draw('endpaper','endpaper_qr_'+self.format,self.W*.5,self.H*.5)
  if self.P:
   logo=(540,380,1.00);qr=(540,1130,462);url=(540,735,.84);cta=(540,1580,.91);scan=(540,1430,.91)
  elif self.Q:
   logo=(440,405,.84);qr=(1005,410,576);url=(720,960,1.08);cta=(440,715,.70);scan=(1005,760,.70)
  else:
   logo=(600,355,.97);qr=(1360,364,462);url=(960,835,1.14);cta=(600,660,.83);scan=(1360,660,.83)
  if 'brand_logo' in self.bank.items:self.draw('logo','brand_logo',*logo)
  if 'brand_qr' in self.bank.items:self.draw('qr','brand_qr',qr[0],qr[1],qr[2]/self.bank.get('brand_qr').width())
  self.label('url',*url);self.label('cta',*cta);self.label('scan',*scan)
  self.draw('copyright','endcopyright_r3',self.W*.5,self.H-31,1 if not self.P else 1.05)

 def scene_content(self,index,t):
  # Keep the accepted staging and the 24 fps camera breath within every shot.
  if index not in [3,4,7,13,15]:
   p=clamp((t-STARTS[index])/(ENDS[index]-STARTS[index]));zoom=1.025-.025*p
   with self.group(self.W*(1-zoom)*.5,self.H*(1-zoom)*.5,zoom):getattr(self,f'scene{index+1:02d}')(t)
  else:getattr(self,f'scene{index+1:02d}')(t)

 def frame(self,t,width=None):
  self.t=t;self.m=(1,0,0,1,0,0);self.alpha=1;self.prefix='global.'
  # Keep story beats in their original timebase. One second of live animation
  # precedes story time zero; narration, sound and captions shift together.
  display_t=min(t-INTRO_SECONDS,STORY_END_HOLD_START)
  self.pose_t=display_t
  self._last_track_in_frame=None
  width=width or self.W;height=round(width*self.H/self.W)
  surface=skia.Surface(width,height);self.c=surface.getCanvas();self.c.clear(rgba('#ad9873'));self.c.scale(width/self.W,height/self.H)
  self.backdrop(display_t,44.9<display_t<54)
  si=max([0]+[i for i,x in enumerate(STARTS) if display_t>=x])
  dt=display_t-STARTS[si]
  if si>0 and dt<TRANSITIONS[si]['duration']:
   handoff(self,si,display_t,STARTS)
  else:
   with self.group(prefix=f'scene{si+1:02d}'):self.scene_content(si,display_t)
  self.prefix='global.'
  self.draw('vignette','vignette_'+self.format,0,0,anchor=(0,0))
  return surface.makeImageSnapshot()
 def order_trace_layers(self):
  # First appearance is not depth: a delayed cross-out still belongs beneath
  # the quill. Preserve every observed draw-order relation with one stable DAG
  # ordering; fail explicitly if a future animation requires changing depth.
  indegree={name:0 for name in self.tracks};following={name:[] for name in self.tracks}
  for before,after in self.order_edges:
   following[before].append(after);indegree[after]+=1
  ready=[(self.tracks[name]['order'],name) for name in self.tracks if indegree[name]==0]
  heapq.heapify(ready);ordered=[]
  while ready:
   _,name=heapq.heappop(ready);ordered.append(name)
   for after in following[name]:
    indegree[after]-=1
    if indegree[after]==0:heapq.heappush(ready,(self.tracks[after]['order'],after))
  if len(ordered)!=len(self.tracks):
   raise ValueError('AE trace contains changing layer-depth relationships; split the affected tracks before exporting.')
  for order,name in enumerate(ordered):self.tracks[name]['order']=order
  return ordered
 def save_trace(self):
  # Preserve every displayed sample. AE uses HOLD interpolation, reproducing the
  # 24fps raster samples exactly while leaving all individual layers editable.
  self.order_trace_layers()
  data={'width':self.W,'height':self.H,'fps':FPS,'duration':DURATION,'assets':{k:self.bank.meta[k] for k in sorted(self.used)},'tracks':list(self.tracks.values()),'audio':[{'file':'audio/mix.wav','name':'Final mixed audio','gainDb':0},{'file':'audio/voice.wav','name':'Narration stem','gainDb':0,'enabled':False},{'file':'audio/music.wav','name':'Original score stem','gainDb':0,'enabled':False},{'file':'audio/foley.wav','name':'Foley stem','gainDb':0,'enabled':False}]}
  p=paths.output('master_timeline.json');p.write_text(json.dumps(data,separators=(',',':')));print('Saved',p,p.stat().st_size,flush=True)
  return p
 def provenance(self):
  """Where every loaded picture came from. An R5 landscape proof uses no generated artwork."""
  origin=self.bank.origin
  return {'format':self.format,'baseline_enabled':paths.USE_BASELINE,
   'artwork':{kind:sorted(n for n,o in origin.items() if o==kind and not n.startswith('crumple_')) for kind in ('baseline','source','generated')},
   'crumples':{f'{si:02d}':data['provenance'] for si,data in sorted(self.fragments.items())}}

def render(args):
 film=Film(args.format,record=args.trace)
 if args.proof:
  out=paths.output('proof');out.mkdir(exist_ok=True)
  times=[4.0,6.2,10.5,17.3,23.3,25.85,28.6,31.8,34.7,37.2,42.8,48.1,51.3,53.45,59.0,63.4,66.0]
  imgs=[]
  for k,t in enumerate(times):
   im=film.frame(t,960 if args.format=='landscape' else 540)
   p=out/f'{args.format}_{k+1:02d}_{t:05.2f}.png';im.save(str(p),skia.kPNG)
   pil=Image.open(p).convert('RGB');pil.thumbnail((480,400));tile=Image.new('RGB',(500,430 if args.format!='landscape' else 310),'#ded4bc');tile.paste(pil,((500-pil.width)//2,20))
   from PIL import ImageDraw,ImageFont
   ImageDraw.Draw(tile).text((15,tile.height-30),f'{k+1:02d} — {t:.2f}s',fill='#29261f',font=ImageFont.truetype(str(FONTS['sans']),16))
   imgs.append(tile)
  cols=4 if args.format=='landscape' else 5;rows=math.ceil(len(imgs)/cols)
  sheet=Image.new('RGB',(cols*500,rows*imgs[0].height),'#ded4bc')
  for i,im in enumerate(imgs):sheet.paste(im,((i%cols)*500,(i//cols)*im.height))
  sheet.save(out/(args.format+'_contact.jpg'),quality=92)
  prov=film.provenance();(out/(args.format+'_provenance.json')).write_text(json.dumps(prov,indent=1,sort_keys=True)+'\n')
  print('Proof saved',out/(args.format+'_contact.jpg'))
  print('Artwork:',', '.join(f'{len(v)} {k}' for k,v in prov['artwork'].items()),'| crumples:',', '.join(f'{k} {v["origin"]}' for k,v in prov['crumples'].items()));return
 width=args.width or film.W;height=int(round(width*film.H/film.W/2)*2)
 out=paths.writable(args.output)
 cmd=['ffmpeg','-y','-loglevel','error','-f','rawvideo','-pix_fmt','rgba','-s',f'{width}x{height}','-r','24','-i','-','-an','-c:v','libx264','-threads','4','-preset',args.preset,'-crf',str(args.crf),'-pix_fmt','yuv420p','-force_key_frames',f'{INTRO_SECONDS},{END_HOLD_START}','-movflags','+faststart',str(out)]
 if args.prores:
  pro=paths.writable(args.prores)
  cmd+=['-an','-c:v','prores_ks','-threads','4','-profile:v','2','-pix_fmt','yuv422p10le','-vendor','apl0',str(pro)]
 proc=subprocess.Popen(cmd,stdin=subprocess.PIPE)
 total=math.ceil(DURATION*24-1e-5);start=time.time()
 for frame in range(total):
  t=frame/24;im=film.frame(t,width)
  arr=im.toarray(colorType=skia.ColorType.kRGBA_8888_ColorType)
  proc.stdin.write(arr.tobytes())
  if frame%120==0:print(f'{args.format}: {frame}/{total} frames; {time.time()-start:.1f}s elapsed',flush=True)
 proc.stdin.close();rc=proc.wait()
 if rc:raise SystemExit(rc)
 if args.trace:film.save_trace()
 print(f'Rendered {out} in {time.time()-start:.1f}s',flush=True)

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--format',choices=['landscape','square','vertical'],default='landscape');p.add_argument('--width',type=int);p.add_argument('--output',default=str(paths.BUILD/'renders/picture.mp4'));p.add_argument('--proof',action='store_true');p.add_argument('--trace',action='store_true');p.add_argument('--crf',type=int,default=16);p.add_argument('--preset',default='fast');p.add_argument('--prores')
 render(p.parse_args())
