import math, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont
S=2048
OUT=(43,27,14)          # dark outline (date brown-black)
import os
FONT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','Assets','Resources','Fonts','Tajawal-Bold.ttf')
def bez(p0,p1,p2,p3,n=60):
    return [((1-t)**3*p0[0]+3*(1-t)**2*t*p1[0]+3*(1-t)*t*t*p2[0]+t**3*p3[0],(1-t)**3*p0[1]+3*(1-t)**2*t*p1[1]+3*(1-t)*t*t*p2[1]+t**3*p3[1]) for t in [i/n for i in range(n+1)]]
def grad(w,h,top,bot):
    g=Image.new('RGBA',(w,h)); d=ImageDraw.Draw(g)
    for y in range(h):
        t=y/max(1,h-1); d.line([(0,y),(w,y)],fill=tuple(int(top[i]*(1-t)+bot[i]*t) for i in range(3))+(255,))
    return g
def outlined_poly(d,pts,fill,w=26):
    d.polygon(pts,fill=fill); d.line(pts+[pts[0]],fill=OUT,width=w,joint='curve')
def emblem(size):
    im=grad(S,S,(64,196,214),(255,214,140))          # turquoise sky -> warm horizon
    d=ImageDraw.Draw(im)
    # sun with rays
    sx,sy,sr=1360,640,300
    for k in range(12):
        a=math.radians(k*30+8); a2=a+math.radians(9)
        pts=[(sx+math.cos(a)*(sr+40),sy+math.sin(a)*(sr+40)),(sx+math.cos(a+math.radians(4.5))*(sr+190),sy+math.sin(a+math.radians(4.5))*(sr+190)),(sx+math.cos(a2)*(sr+40),sy+math.sin(a2)*(sr+40))]
        d.polygon(pts,fill=(255,236,120))
    d.ellipse([sx-sr,sy-sr,sx+sr,sy+sr],fill=(255,200,40),outline=OUT,width=26)
    d.ellipse([sx-sr+60,sy-sr+50,sx-sr+170,sy-sr+130],fill=(255,240,170))   # shine
    # back dune
    back=bez((-60,1330),(500,1120),(1100,1300),(2110,1150))+[(2110,S),(-60,S)]
    outlined_poly(d,back,(240,190,110))
    # palm trunk
    trunk=bez((700,1330),(690,1120),(760,930),(820,760),30)
    L=[];R=[]
    for i,(x,y) in enumerate(trunk):
        w=62-26*i/len(trunk); L.append((x-w,y)); R.append((x+w,y))
    outlined_poly(d,L+R[::-1],(150,96,52))
    for i in range(3,len(trunk)-2,4):
        x,y=trunk[i]; w=62-26*i/len(trunk)
        d.arc([x-w,y-30,x+w,y+30],200,340,fill=OUT,width=14)
    cx,cy=820,760
    # plump fronds
    for ang,ln in [(-160,470),(-125,440),(-90,380),(-55,440),(-20,470),(15,430),(195,430)]:
        r=math.radians(ang); ex=cx+math.cos(r)*ln; ey=cy+math.sin(r)*ln*0.62+ln*0.35
        tip=(ex,ey); mid=(cx+math.cos(r)*ln*0.55, cy+math.sin(r)*ln*0.55-90)
        sp=bez((cx,cy),mid,mid,tip,40); A=[];B=[]
        for i,(x,y) in enumerate(sp):
            t=i/40; w=95*math.sin(math.pi*min(1,t*1.05))+6
            nx,ny=sp[min(i+1,40)]; px,py=sp[max(i-1,0)]; dx,dy=nx-px,ny-py; l=math.hypot(dx,dy) or 1
            A.append((x-dy/l*w,y+dx/l*w)); B.append((x+dy/l*w*0.3,y-dx/l*w*0.3))
        outlined_poly(d,A+B[::-1],(76,170,72),22)
        d.line(sp,fill=(46,120,50),width=12)
    # dates
    rnd=random.Random(3)
    for k in range(18):
        x=cx+rnd.uniform(-70,70); y=cy+50+rnd.uniform(0,140)
        d.ellipse([x-26,y-32,x+26,y+32],fill=(214,96,40) if k%3 else (240,140,50),outline=OUT,width=10)
    # front dune with field rows
    front=bez((-60,1520),(600,1380),(1300,1600),(2110,1440))+[(2110,S),(-60,S)]
    outlined_poly(d,front,(255,214,120))
    for j in range(1,5):
        row=bez((-60,1520+j*95),(600,1380+j*95),(1300,1600+j*95),(2110,1440+j*95))
        d.line(row,fill=(120,190,70),width=34)
        d.line(row,fill=(70,140,50),width=8)
    # little red tractor on the back dune
    tx,ty=1330,1210
    outlined_poly(d,[(tx-150,ty),(tx+60,ty),(tx+60,ty-90),(tx+150,ty-90),(tx+150,ty+40),(tx-150,ty+40)],(226,50,46),18)
    outlined_poly(d,[(tx-120,ty),(tx-120,ty-170),(tx+20,ty-170),(tx+20,ty)],(226,50,46),18)
    d.rectangle([tx-95,ty-145,tx-5,ty-60],fill=(180,232,255),outline=OUT,width=14)
    d.line([(tx+110,ty-90),(tx+110,ty-190)],fill=OUT,width=22)
    for (wx,wr) in ((tx-90,95),(tx+110,62)):
        d.ellipse([wx-wr,ty+40-wr+40,wx+wr,ty+40+wr+40],fill=(40,40,40),outline=OUT,width=16)
        d.ellipse([wx-wr*0.45,ty+80-wr*0.45,wx+wr*0.45,ty+80+wr*0.45],fill=(255,200,40))
    # badge mask + thick frame
    m=Image.new('L',(S,S),0); ImageDraw.Draw(m).rounded_rectangle([0,0,S-1,S-1],radius=460,fill=255)
    im.putalpha(m)
    m=Image.new('L',(S,S),0); ImageDraw.Draw(m).rounded_rectangle([20,20,S-21,S-21],radius=440,fill=255)
    im.putalpha(m)
    fr=Image.new('RGBA',(S,S),(0,0,0,0)); fd=ImageDraw.Draw(fr)
    fd.rounded_rectangle([20,20,S-21,S-21],radius=440,outline=OUT,width=44)
    fd.rounded_rectangle([60,60,S-61,S-61],radius=400,outline=(255,255,255,120),width=14)
    im=Image.alpha_composite(im,fr)
    return im.resize((size,size),Image.LANCZOS)

def word(text, size, rtl=False):
    kw=dict(direction='rtl',language='ar') if rtl else {}
    f=ImageFont.truetype(FONT,size,layout_engine=ImageFont.Layout.RAQM)
    st=int(size*0.09)
    tmp=ImageDraw.Draw(Image.new('L',(10,10)))
    b=tmp.textbbox((0,0),text,font=f,stroke_width=st,**kw)
    w,h=b[2]-b[0]+40,b[3]-b[1]+60
    ox,oy=20-b[0],20-b[1]
    mask=Image.new('L',(w,h),0); ImageDraw.Draw(mask).text((ox,oy),text,font=f,fill=255,**kw)
    outline=Image.new('L',(w,h),0); ImageDraw.Draw(outline).text((ox,oy),text,font=f,fill=255,stroke_width=st,stroke_fill=255,**kw)
    im=Image.new('RGBA',(w,h),(0,0,0,0))
    sh=Image.new('RGBA',(w,h),(0,0,0,0)); sh.paste((0,0,0,110),(0,0),outline.point(lambda v:v))
    im.alpha_composite(sh,(0,int(size*0.06)) if False else (0,0)); im=Image.new('RGBA',(w,h),(0,0,0,0))
    shadow=Image.new('RGBA',(w,h),(0,0,0,0)); shadow.paste((43,27,14,140),mask=outline)
    im.alpha_composite(shadow.transform((w,h),Image.AFFINE,(1,0,0,0,1,-int(size*0.07))))
    ol=Image.new('RGBA',(w,h),OUT+(255,)); im.paste(ol,mask=outline)
    g=grad(w,h,(255,236,120),(255,140,40)); im.paste(g,mask=mask)
    hl=Image.new('L',(w,h),0); ImageDraw.Draw(hl).rectangle([0,0,w,oy+(b[3]-b[1])*0.35],fill=90)
    hlm=Image.composite(hl,Image.new('L',(w,h),0),mask)
    im.paste((255,255,255,255),mask=hlm.point(lambda v:v))
    return im

def tagline(text, size, rtl=False):
    kw=dict(direction='rtl',language='ar') if rtl else {}
    f=ImageFont.truetype(FONT,size,layout_engine=ImageFont.Layout.RAQM)
    tmp=ImageDraw.Draw(Image.new('L',(10,10)))
    b=tmp.textbbox((0,0),text,font=f,**kw); w,h=b[2]-b[0]+90,b[3]-b[1]+50
    im=Image.new('RGBA',(w,h),(0,0,0,0)); d=ImageDraw.Draw(im)
    d.rounded_rectangle([0,0,w-1,h-1],radius=h//2,fill=(46,140,70),outline=OUT,width=10)
    d.text((45-b[0],25-b[1]),text,font=f,fill=(255,255,255),**kw)
    return im

if __name__ == '__main__':   # legacy export (make_brand_kit.py now writes every size and tile)
    E=emblem(1024); E.save('mazraa_icon_1024.png')
    # app / store icon sizes
    import os
    os.makedirs('out/msix',exist_ok=True)
    E.resize((512,512),Image.LANCZOS).save('out/mazraa_icon_512.png')
    E.save('out/mazraa_icon_1024.png')
    E.save('out/mazraa.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
    def sq(n,name): E.resize((n,n),Image.LANCZOS).save('out/msix/'+name)
    for base,n in (('Square44x44Logo',44),('Square150x150Logo',150),('Square310x310Logo',310),('StoreLogo',50),('Square71x71Logo',71)):
        sq(n,base+'.png'); sq(n*2,base+'.scale-200.png')
    for t in (16,24,32,48,256): sq(t,'Square44x44Logo.targetsize-%d.png'%t)
    def wide(w,h,name,emb):
        im=Image.new('RGBA',(w,h),(64,196,214,255)); g=grad(w,h,(64,196,214),(255,214,140)); im=g
        e=emblem(emb); im.alpha_composite(e,((w-emb)//2,(h-emb)//2)); im.save('out/msix/'+name)
    wide(310,150,'Wide310x150Logo.png',130); wide(620,300,'Wide310x150Logo.scale-200.png',260)
    wide(620,300,'SplashScreen.png',260); wide(1240,600,'SplashScreen.scale-200.png',520)
    def lockup(lang,bg):
        em=emblem(640)
        if lang=='en': wd=word("MAZRAA",300); tg=tagline("FARMING SIMULATOR",64)
        else: wd=word("مزرعة",320,True); tg=tagline("محاكي الزراعة",70,True)
        tw=max(wd.width,tg.width); W=60+640+50+tw+60; H=760
        im=Image.new('RGBA',(W,H),bg)
        if lang=='en':
            im.alpha_composite(em,(60,60)); x=750; top=(H-(wd.height+10+tg.height))//2
            im.alpha_composite(wd,(x,top)); im.alpha_composite(tg,(x+10,top+wd.height+10))
        else:
            im.alpha_composite(em,(W-700,60)); right=W-750; top=(H-(wd.height+10+tg.height))//2
            im.alpha_composite(wd,(right-wd.width,top)); im.alpha_composite(tg,(right-tg.width-10,top+wd.height+10))
        return im
    for lang in ('en','ar'):
        lockup(lang,(0,0,0,0)).save('mazraa_logo_%s.png'%lang)
        lockup(lang,(22,58,32,255)).save('mazraa_logo_%s_dark.png'%lang)
        lockup(lang,(255,248,232,255)).save('mazraa_logo_%s_light.png'%lang)
        lk=lockup(lang,(0,0,0,0)); lk.save('out/mazraa_logo_%s.png'%lang)
        r=512/lk.width; lk.resize((512,int(lk.height*r)),Image.LANCZOS).save('out/mazraa_logo_%s_512.png'%lang)
    print('ok')
