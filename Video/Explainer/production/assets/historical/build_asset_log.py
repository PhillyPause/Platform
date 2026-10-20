from pathlib import Path
from PIL import Image,ImageDraw
import json,hashlib
root=Path(__file__).resolve().parent
records=[]
common='Public-domain historical artwork, published before 1929; faithful digitization supplied by the institution.'
for stem,ident,authors,use,priority in [
 ('franklin_hall_1868','2004671903','H. B. Hall, engraver, after Joseph-Siffred Duplessis (portrait from 1783)', ['Recognizable bald head, side curls, face, shoulders and coat for a Franklin puppet','Fine engraved hatch marks for original faces and cloth shading'], 'primary'),
 ('philadelphia_1768','2004672470','Thomas Jefferys, engraver, after George Heap under Nicholas Scull', ['Long Philadelphia waterfront skyline for distant parallax layer','Sailing ships and separate church towers','Frontal State House inset for a clean colonial facade','Period street grid for map and ledger props','Unprinted beige paper around edges as scanned-paper texture'], 'primary'),
 ('state_house_1787','2004671521','James Trenchard, engraver, after Charles Willson Peale', ['Large Independence Hall silhouette, clock face and roofs','Engraved clouds for a separate moving sky layer','Foreground tree silhouettes and tiny townspeople','Worn paper margins and real plate impression'], 'primary'),
 ('franklin_1780','2003675417','Unknown engraver; published by R. Faulder in An impartial history of the war in America (1780)', ['Period coat, waistcoat, articulated forearms and chair; optional donor for an original rig','Not selected as main face because this wigged depiction is less recognizable to modern viewers'], 'optional alternate')]:
 f=root/(stem+'.tif')
 if not f.exists():continue
 d=json.loads((root/f'metadata_{ident}.json').read_text());item=d['item'];im=Image.open(f)
 jpg=root/(stem+'.jpg')
 if not jpg.exists():im.convert('RGB').save(jpg,quality=95,subsampling=0)
 url=next(x['url'] for r in d['resources'] for seq in r.get('files',[]) for x in seq if x['url'].endswith(f.name.replace(stem,'3a26485')) ) if False else None
 # Keep the exact retrieval URL, rather than infer it from a filename.
 urls={'franklin_hall_1868':'https://tile.loc.gov/storage-services/master/pnp/cph/3a20000/3a26000/3a26400/3a26485u.tif','philadelphia_1768':'https://tile.loc.gov/storage-services/master/pnp/pga/01600/01698u.tif','state_house_1787':'https://tile.loc.gov/storage-services/master/pnp/ppmsca/30500/30579u.tif','franklin_1780':'https://tile.loc.gov/storage-services/master/pnp/cph/3a40000/3a45000/3a45400/3a45459u.tif'}
 records.append({'id':stem,'selection':priority,'title':item.get('title'),'author':authors,'date':item.get('created_published_date',item.get('date')),'institution':'Library of Congress','source_page':f'https://www.loc.gov/item/{ident}/','download_url':urls[stem],'rights_statement':item.get('rights_advisory'),'public_domain_basis':common,'raw_path':str(f),'production_path':str(jpg),'width':im.width,'height':im.height,'raw_bytes':f.stat().st_size,'sha256_raw':hashlib.sha256(f.read_bytes()).hexdigest(),'modifications':'Full-resolution TIFF-to-JPEG format conversion only; no creative alteration.','useful_elements':use})
for stem,title,author,date,source,download,rights,use in [
 ('racknitz_1789','Racknitz — The Turk, plate 3','Joseph Friedrich von Racknitz','1789','https://commons.wikimedia.org/wiki/File:Racknitz_-_The_Turk_3.jpg','https://upload.wikimedia.org/wikipedia/commons/6/6e/Racknitz_-_The_Turk_3.jpg','Commons PD-Art / PD-old-100; faithful reproduction of a public-domain 1789 engraving. US copyright expired.',['Cabinet gears, cranks, rods, brass axles and mechanical textures for an ORIGINAL automaton template','Jointed arm and clockwork hand geometry; do not duplicate the historic costume as the AI swarm','Engraved floor and wall linework for small workshop set inserts']),
 ('linen_18th_century','Textile, 18th century (Cooper Hewitt 1955-45-1-a/c)','Unknown textile maker; image from Cooper Hewitt, Smithsonian Design Museum','18th century; modern museum digitization','https://commons.wikimedia.org/wiki/File:Textile,_18th_century_(CH_18404637).jpg','https://upload.wikimedia.org/wikipedia/commons/e/eb/Textile%2C_18th_century_%28CH_18404637%29.jpg','Commons PD-old-100-expired / Public Domain Mark. Historical two-dimensional textile reproduced by Cooper Hewitt; Smithsonian catalog search result also labels media CC0.',['Actual quilted linen fibres and seam lines','Cream and oxblood floral textile for waistcoats, skirt panels or curtain pieces','Photographed artefact texture; NOT represented as a personally scanned thrift-store fabric'])]:
 f=root/(stem+'.jpg');im=Image.open(f)
 records.append({'id':stem,'selection':'primary','title':title,'author':author,'date':date,'source_page':source,'download_url':download,'rights_statement':rights,'public_domain_basis':rights,'raw_path':str(f),'production_path':str(f),'width':im.width,'height':im.height,'raw_bytes':f.stat().st_size,'sha256_raw':hashlib.sha256(f.read_bytes()).hexdigest(),'modifications':'None.','useful_elements':use})
log={'created':'2026-10-19','scope':'Five principal public-domain image sources plus one optional period portrait donor. No Lemony Snicket imagery or footage.','assets':records}
(root/'historical_asset_log.json').write_text(json.dumps(log,indent=2,ensure_ascii=False)+'\n')
primary=[r for r in records if r['selection']=='primary']
can=Image.new('RGB',(1500,960),'#eee5d4');dr=ImageDraw.Draw(can)
for i,r in enumerate(primary):
 im=Image.open(r['production_path']).convert('RGB');im.thumbnail((480,420));x=(i%3)*500+(500-im.width)//2;y=(i//3)*480
 can.paste(im,(x,y+30));dr.text(((i%3)*500+12,y+8),r['id'],fill='black')
can.save(root/'historical_contact_sheet.jpg',quality=90)
for r in records:print(r['id'],str(r['width'])+'x'+str(r['height']),r['selection'])
