"""Build the Korean system guide from locally verified source notes.

Run with the Codex bundled Python runtime. Uses reportlab and Windows Malgun Gothic.
"""
from pathlib import Path
import json, math
from xml.sax.saxutils import escape
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, Color, white, black
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph, Table, TableStyle

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
OUT = ROOT / 'output' / 'pdf' / 'FourTigerSword_GameSystems_20261104.pdf'
OUT.parent.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('MG', 'C:/Windows/Fonts/malgun.ttf'))
pdfmetrics.registerFont(TTFont('MGB', 'C:/Windows/Fonts/malgunbd.ttf'))
pdfmetrics.registerFontFamily('MG', normal='MG', bold='MGB')
W, H = 595.276, 841.89
M = 48
CW = W - 2*M
INK = HexColor('#172531')
MUTED = HexColor('#56636D')
BLUE = HexColor('#245374')
TEAL = HexColor('#247F7B')
PALE = HexColor('#EDF4F7')
GOLD = HexColor('#986A29')
LINE = HexColor('#D9D9D9')
styles = {
 'body': ParagraphStyle('body', fontName='MG', fontSize=10.4, leading=16.7, textColor=INK, wordWrap='CJK', spaceAfter=8),
 'small': ParagraphStyle('small', fontName='MG', fontSize=8.2, leading=12.1, textColor=MUTED, wordWrap='CJK'),
 'h2': ParagraphStyle('h2', fontName='MGB', fontSize=12.6, leading=18.2, textColor=black, wordWrap='CJK'),
 'cell': ParagraphStyle('cell', fontName='MG', fontSize=9.15, leading=14, textColor=INK, wordWrap='CJK'),
 'head': ParagraphStyle('head', fontName='MGB', fontSize=9.15, leading=14, textColor=white, wordWrap='CJK'),
 'node': ParagraphStyle('node', fontName='MGB', fontSize=10, leading=14.5, textColor=INK, alignment=1, wordWrap='CJK'),
}

def p(text, style='body'):
    return Paragraph(escape(text).replace('\n', '<br/>'), styles[style])

class Guide:
    def __init__(self, pages):
        self.pages = pages
        self.c = canvas.Canvas(str(OUT), pagesize=(W,H), pageCompression=1)
        self.c.setTitle('Four Tiger Sword 게임 시스템 문서')
        self.c.setAuthor('Four Tiger Sword 프로젝트')
        self.c.setSubject('2026년 11월 4일 구현과 자산 설정 기준 게임 시스템 설명 및 구조도')
        self.metrics = []

    def para(self, text, style='body', after=8):
        q = p(text, style)
        _, ht = q.wrap(CW, 1000)
        q.drawOn(self.c, M, self.y-ht)
        self.y -= ht+after

    def table(self, columns, rows, widths=None):
        widths = [CW/len(columns)]*len(columns) if widths is None else [CW*v/sum(widths) for v in widths]
        data = [[p(v,'head') for v in columns]] + [[p(str(v),'cell') for v in row] for row in rows]
        t = Table(data, colWidths=widths, hAlign='LEFT')
        cmds = [('BACKGROUND',(0,0),(-1,0),BLUE),('GRID',(0,0),(-1,-1),0.5,LINE),('VALIGN',(0,0),(-1,-1),'MIDDLE'),
                ('LEFTPADDING',(0,0),(-1,-1),8),('RIGHTPADDING',(0,0),(-1,-1),8),('TOPPADDING',(0,0),(-1,-1),7),('BOTTOMPADDING',(0,0),(-1,-1),7)]
        for i in range(2,len(data),2): cmds.append(('BACKGROUND',(0,i),(-1,i),HexColor('#F4F7F9')))
        t.setStyle(TableStyle(cmds))
        _,ht=t.wrap(CW,1000)
        t.drawOn(self.c,M,self.y-ht)
        self.y-=ht+13

    def box(self,x,y,w,h,text,color=PALE):
        self.c.setFillColor(color); self.c.setStrokeColor(HexColor('#AEBFC8')); self.c.setLineWidth(.7)
        self.c.roundRect(x,y,w,h,7,fill=1,stroke=1)
        q=p(text,'node'); _,ht=q.wrap(w-12,h)
        if ht>h-8: raise ValueError('Diagram node too tall: '+text)
        q.drawOn(self.c,x+6,y+(h-ht)/2)

    def arrow(self,x1,y1,x2,y2,color=TEAL):
        c=self.c; c.setStrokeColor(color); c.setFillColor(color); c.setLineWidth(1.3); c.line(x1,y1,x2,y2)
        a=math.atan2(y2-y1,x2-x1); s=5.3
        path=c.beginPath(); path.moveTo(x2,y2)
        path.lineTo(x2-s*math.cos(a-.45),y2-s*math.sin(a-.45)); path.lineTo(x2-s*math.cos(a+.45),y2-s*math.sin(a+.45)); path.close()
        c.drawPath(path,fill=1,stroke=0)

    def flow(self, rows, caption, connect_rows=True):
        rowh=53; gap=24
        total=len(rows)*rowh+(len(rows)-1)*gap+6
        top=self.y
        for r,row in enumerate(rows):
            bw=(CW-24*(len(row)-1))/len(row); yy=top-rowh-r*(rowh+gap)
            for j,label in enumerate(row):
                x=M+j*(bw+24); self.box(x,yy,bw,rowh,label)
                if j: self.arrow(x-21,yy+rowh/2,x-3,yy+rowh/2)
            if connect_rows and r<len(rows)-1:
                # Connect the last step to the first step in the following row.
                # A central vertical connector would silently skip sequence steps.
                next_bw=(CW-24*(len(rows[r+1])-1))/len(rows[r+1])
                start_x=M+(len(row)-1)*(bw+24)+bw/2
                end_x=M+next_bw/2
                mid_y=yy-gap/2
                self.c.setStrokeColor(TEAL);self.c.setLineWidth(1.3)
                path=self.c.beginPath();path.moveTo(start_x,yy-2);path.lineTo(start_x,mid_y);path.lineTo(end_x,mid_y)
                self.c.drawPath(path)
                self.arrow(end_x,mid_y,end_x,yy-gap+2)
        self.y-=total
        self.para(caption,'small',12)

    def cycle(self, caption):
        # Current asset's 1.5x attacking advantage, not the traditional generating cycle.
        top=self.y; cy=top-110; cx=W/2; radius=85
        nodes=[('목','취약·침묵'),('토','가드·보호막'),('수','유수·강공격'),('화','스택·흡혈'),('금','방어 감소')]
        coords=[]
        fills=['#E5F2E8','#F3EBD6','#E3F0F8','#FAE9E3','#ECEFF3']
        for i,(name,role) in enumerate(nodes):
            a=math.radians(90-i*72); x=cx+140*math.cos(a); y=cy+radius*math.sin(a)
            coords.append((x,y)); self.box(x-50,y-22,100,44,name+'\n'+role,HexColor(fills[i]))
        for i in range(5):
            x1,y1=coords[i]; x2,y2=coords[(i+1)%5]; dx=x2-x1;dy=y2-y1
            edge=max(abs(dx)/54,abs(dy)/26); t=1/edge
            self.arrow(x1+dx*t,y1+dy*t,x2-dx*t,y2-dy*t)
        self.y=top-220
        self.para(caption,'small',12)

    def mapfigure(self,caption):
        top=self.y; y=top-60
        # This schematic deliberately avoids claiming to be a seeded generated layout.
        positions=[(M+2,y,'시작방'),(M+132,y,'전투방 A'),(M+262,y,'전투방 B'),(M+392,y,'보스방')]
        for x,yy,l in positions: self.box(x,yy,104,50,l)
        for i in range(3): self.arrow(positions[i][0]+107,y+25,positions[i+1][0]-3,y+25)
        self.box(M+132,y-82,104,50,'상점',HexColor('#F7EEDB'))
        self.arrow(M+184,y-3,M+184,y-29)
        self.c.setFont('MG',9); self.c.setFillColor(MUTED); self.c.drawString(M+264,y-62,'방 연결과 역할을 설명하는 예시')
        self.y=top-159; self.para(caption,'small',12)

    def timeline(self,caption):
        top=self.y; x=M; yy=top-58
        widths=[120,142,CW-262]
        labels=['준비 동작','유효 판정','회수 동작']
        for i,(ww,lab) in enumerate(zip(widths,labels)):
            self.box(x,yy,ww-4,42,lab,HexColor(['#ECF0F4','#E1F1EA','#F5EDDD'][i]));x+=ww
        self.arrow(M+4,yy-21,M+CW-4,yy-21)
        self.c.setFont('MG',8.5); self.c.setFillColor(MUTED)
        self.c.drawString(M,yy-37,'입력 버퍼 / 이동 / 타격 시점 / 취소 가능 구간을 공격 데이터로 조절')
        self.y=top-108; self.para(caption,'small',12)

    def render(self):
        for n,page in enumerate(self.pages,1):
            self.y=H-72
            c=self.c
            c.setFont('MG',8);c.setFillColor(MUTED)
            c.drawString(M,H-32,'FOUR TIGER SWORD   /   SYSTEM GUIDE')
            c.drawRightString(W-M,H-32,'2026.11.04')
            c.bookmarkPage('p'+str(n));c.addOutlineEntry(page['title'],'p'+str(n),level=0,closed=False)
            c.setFont('MGB',22 if n==1 else 19);c.setFillColor(black)
            c.drawString(M,self.y,page['title']);self.y-=29
            if page.get('subtitle'):self.para(page['subtitle'],'small',16)
            for el in page['content']:
                kind=el['type']
                if kind=='p':self.para(el['text'])
                elif kind=='h':self.y-=5;self.para(el['text'],'h2',7)
                elif kind=='table':self.table(el['columns'],el['rows'],el.get('widths'))
                elif kind=='flow':self.flow(el['rows'],el['caption'],el.get('connect_rows',True))
                elif kind=='cycle':self.cycle(el['caption'])
                elif kind=='map':self.mapfigure(el['caption'])
                elif kind=='timeline':self.timeline(el['caption'])
            source='근거  '+page.get('sources','')
            q=p(source,'small');_,sh=q.wrap(CW,1000)
            source_y=49+sh
            if self.y < source_y+13: raise ValueError(f'Page {n} overflow {source_y+13-self.y:.1f}pt: {page["title"]}')
            q.drawOn(c,M,49)
            c.setFont('MG',8);c.setFillColor(MUTED);c.drawRightString(W-M,27,f'{n:02d} / {len(self.pages):02d}')
            self.metrics.append({'page':n,'title':page['title'],'content_bottom':round(self.y,1),'source_top':round(source_y,1),'free_gap':round(self.y-source_y,1)})
            c.showPage()
        c.save()
        (HERE/'layout_metrics.json').write_text(json.dumps(self.metrics,ensure_ascii=False,indent=2),encoding='utf-8')
        print(str(OUT))
        print(f'{len(self.pages)} pages; min gap {min(v["free_gap"] for v in self.metrics)} pt')

if __name__=='__main__':
    from guide_content import pages
    Guide(pages).render()
