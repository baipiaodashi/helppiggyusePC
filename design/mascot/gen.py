# 原创小猪：粗描边、圆胖身体、大鼻子、头顶一撮小卷毛
import sys, json
INK="#3B2A2E"; SKIN="#FBC0C0"; SNOUT="#F2929E"; BLUSH="#FF8FA3"; INNER="#F5A3AE"; SW=5.5

def S(extra=""): return f'stroke="{INK}" stroke-width="{SW}" stroke-linejoin="round" stroke-linecap="round" {extra}'

BODY="M 50 130 C 46 76, 82 46, 120 46 C 158 46, 194 76, 190 130 C 194 174, 186 204, 160 215 C 140 223, 100 223, 80 215 C 54 204, 46 174, 50 130 Z"
def ears():
    return (f'<path d="M 66 82 C 58 60, 62 42, 74 38 C 88 40, 98 52, 100 60" fill="{SKIN}" {S()}/>'
            f'<path d="M 174 82 C 182 60, 178 42, 166 38 C 152 40, 142 52, 140 60" fill="{SKIN}" {S()}/>'
            f'<path d="M 72 64 C 70 54, 72 48, 77 47 C 83 49, 88 54, 89 58" fill="{INNER}"/>'
            f'<path d="M 168 64 C 170 54, 168 48, 163 47 C 157 49, 152 54, 151 58" fill="{INNER}"/>')
def feet():
    return (f'<path d="M 92 210 L 92 226 Q 92 230 98 230 L 106 230 Q 112 230 112 226 L 112 212" fill="{SKIN}" {S()}/>'
            f'<path d="M 128 212 L 128 226 Q 128 230 134 230 L 142 230 Q 148 230 148 226 L 148 210" fill="{SKIN}" {S()}/>')
def body(): return f'<path d="{BODY}" fill="{SKIN}" {S()}/>' + '<ellipse cx="88" cy="70" rx="17" ry="8" fill="#fff" opacity=".45" transform="rotate(-28 88 70)"/>'
def tuft(): return f'<path d="M 116 45 C 112 34, 120 27, 127 31 C 132 35, 127 41, 123 37" fill="none" {S()}/>'
def face(eyes="dot", mouth="smile", blush=True):
    out=""
    if blush: out+=f'<ellipse cx="70" cy="122" rx="13" ry="7" fill="{BLUSH}" opacity=".55"/><ellipse cx="170" cy="122" rx="13" ry="7" fill="{BLUSH}" opacity=".55"/>'
    if eyes=="dot":
        out+=f'<ellipse cx="83" cy="98" rx="5.5" ry="6.5" fill="{INK}"/><ellipse cx="157" cy="98" rx="5.5" ry="6.5" fill="{INK}"/>'
        out+='<circle cx="85" cy="95.5" r="1.8" fill="#fff"/><circle cx="159" cy="95.5" r="1.8" fill="#fff"/>'
    elif eyes=="happy":  # 笑眯眯 ^ ^
        out+=f'<path d="M 75 100 Q 83 90 91 100" fill="none" {S()}/><path d="M 149 100 Q 157 90 165 100" fill="none" {S()}/>'
    elif eyes=="closed":
        out+=f'<path d="M 75 98 Q 83 104 91 98" fill="none" {S()}/><path d="M 149 98 Q 157 104 165 98" fill="none" {S()}/>'
    elif eyes=="look":  # 往上看（思考）
        out+=f'<ellipse cx="85" cy="94" rx="5.5" ry="6.5" fill="{INK}"/><ellipse cx="159" cy="94" rx="5.5" ry="6.5" fill="{INK}"/>'
        out+='<circle cx="87" cy="91.5" r="1.8" fill="#fff"/><circle cx="161" cy="91.5" r="1.8" fill="#fff"/>'
    # 大鼻子
    out+=f'<ellipse cx="120" cy="114" rx="33" ry="22" fill="{SNOUT}" {S()}/>'
    out+=f'<ellipse cx="109" cy="114" rx="4.6" ry="7.5" fill="{INK}"/><ellipse cx="131" cy="114" rx="4.6" ry="7.5" fill="{INK}"/>'
    out+='<ellipse cx="108" cy="103" rx="8" ry="3.2" fill="#fff" opacity=".5"/>'
    if mouth=="smile": out+=f'<path d="M 111 145 Q 120 152 129 145" fill="none" {S()}/>'
    elif mouth=="open": out+=f'<path d="M 108 142 Q 120 162 132 142 Z" fill="#C9505F" {S()}/><path d="M 114 151 Q 120 156 126 151" fill="#FF9AA8"/>'
    elif mouth=="o": out+=f'<ellipse cx="120" cy="148" rx="5" ry="6" fill="#C9505F" {S()}/>'
    elif mouth=="flat": out+=f'<path d="M 113 147 L 127 147" fill="none" {S()}/>'
    return out
def arm(x1,y1,x2,y2,w=17):
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{INK}" stroke-width="{w+2*SW}" stroke-linecap="round"/><line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{SKIN}" stroke-width="{w}" stroke-linecap="round"/>'
def spark(x,y,s=1,c="#FFC83D"):
    return f'<path transform="translate({x} {y}) scale({s})" d="M 0 -10 Q 1.5 -1.5 10 0 Q 1.5 1.5 0 10 Q -1.5 1.5 -10 0 Q -1.5 -1.5 0 -10 Z" fill="{c}"/>'
def shadow(): return '<ellipse cx="120" cy="232" rx="52" ry="6" fill="#3B2A2E" opacity=".10"/>'

def base(eyes="dot", mouth="smile"):
    return shadow()+feet()+ears()+body()+tuft()+face(eyes,mouth)

P={}
# 1 招手（默认）
P["wave"]= base()+arm(58,158,86,178)+arm(184,136,210,94)+f'<path d="M 214 78 q 6 6 4 14 M 222 72 q 9 9 6 22" fill="none" {S("stroke-width=\'4\'")}/>'
# 2 欢呼（双手举高 + 星星）
P["cheer"]= base("happy","open")+arm(56,140,30,96)+arm(184,140,210,96)+spark(28,70,1.1)+spark(214,62,1.3)+spark(206,28,.7,"#FF8FA3")+spark(40,34,.8,"#7EC8FF")
# 3 扫帚（清理）
broom=(f'<g transform="translate(6 -4) rotate(24 196 130)">'
       f'<rect x="191" y="40" width="10" height="130" rx="5" fill="#C98B55" {S()}/>'
       f'<path d="M 178 168 L 214 168 L 224 222 Q 196 230 168 222 Z" fill="#FFD36B" {S()}/>'
       f'<path d="M 182 182 L 210 182" fill="none" {S("stroke-width=\'4\'")}/>'
       f'<path d="M 184 200 L 182 222 M 196 200 L 196 226 M 208 200 L 210 222" fill="none" {S("stroke-width=\'3.5\'")}/></g>')
P["broom"]= shadow()+feet()+ears()+body()+tuft()+face("happy","smile")+broom+arm(182,150,196,128)+arm(150,176,192,166)+f'<circle cx="30" cy="206" r="7" fill="#E7DCDC"/><circle cx="16" cy="214" r="4.5" fill="#E7DCDC"/><circle cx="42" cy="222" r="3.5" fill="#E7DCDC"/>'
# 4 思考（AI）
bubble=(f'<circle cx="198" cy="58" r="22" fill="#fff" {S()}/><circle cx="174" cy="88" r="5" fill="#fff" {S("stroke-width=\'3.5\'")}/>'
        f'<text x="198" y="70" font-family="Arial Rounded MT Bold, Arial, sans-serif" font-weight="900" font-size="32" text-anchor="middle" fill="#FF7EA8">?</text>')
P["think"]= base("look","o")+arm(58,162,84,180)+arm(182,176,150,148)+bubble
# 5 放大镜（找东西）
glass=(f'<line x1="186" y1="176" x2="198" y2="154" stroke="{INK}" stroke-width="{11+2*SW}" stroke-linecap="round"/><line x1="186" y1="176" x2="198" y2="154" stroke="#C98B55" stroke-width="11" stroke-linecap="round"/>'
       f'<circle cx="204" cy="130" r="25" fill="#DDF1FF" fill-opacity=".9" {S("stroke-width='7'")}/><path d="M 191 122 Q 195 113 204 112" fill="none" stroke="#fff" stroke-width="5" stroke-linecap="round"/>')
P["search"]= base("dot","o")+arm(58,158,84,178)+glass+arm(178,156,188,176)
# 6 按键（快捷键）：举着一个大键帽
key=(f'<rect x="150" y="46" width="70" height="56" rx="12" fill="#E9E1F5" {S()}/><rect x="150" y="46" width="70" height="48" rx="12" fill="#fff" {S()}/>'
     f'<text x="185" y="80" font-family="Segoe UI, Arial, sans-serif" font-weight="800" font-size="22" text-anchor="middle" fill="{INK}">Ctrl</text>')
P["key"]= base("happy","open")+arm(58,158,84,178)+key+arm(184,140,190,104)+spark(36,60,.9)
# 9 抱着剪贴板（剪贴板历史）：夹子 + 一张写了字的小纸
clipboard=(f'<g transform="rotate(12 184 88)">'
           f'<rect x="150" y="40" width="68" height="90" rx="13" fill="#F7C873" {S()}/>'
           f'<rect x="160" y="55" width="48" height="64" rx="7" fill="#fff" {S("stroke-width=\'4\'")}/>'
           f'<rect x="170" y="31" width="28" height="17" rx="7" fill="#FF7EA8" {S()}/>'
           f'<path d="M 168 74 L 200 74 M 168 88 L 200 88 M 168 102 L 188 102" fill="none" stroke="#F5A3AE" stroke-width="5" stroke-linecap="round"/>'
           f'</g>')
P["clip"]= base("happy","smile")+arm(58,158,84,178)+clipboard+arm(186,156,172,126)+spark(38,60,.9)+spark(222,152,.6,"#7EC8FF")
# 7 只有脑袋（头像 / 图标）
P["face"]= ears()+f'<path d="M 48 128 C 46 76, 82 46, 120 46 C 158 46, 194 76, 192 128 C 194 170, 160 188, 120 188 C 80 188, 46 170, 48 128 Z" fill="{SKIN}" {S()}/>'+'<ellipse cx="88" cy="70" rx="17" ry="8" fill="#fff" opacity=".45" transform="rotate(-28 88 70)"/>'+tuft()+face()
# 8 睡觉（空状态 / 没东西）
P["sleep"]= base("closed","flat")+arm(60,166,98,184)+arm(180,166,142,184)+f'<text x="186" y="70" font-family="Arial" font-weight="900" font-size="26" fill="#9C8BB8">z</text><text x="204" y="46" font-family="Arial" font-weight="900" font-size="18" fill="#9C8BB8">z</text>'

VB={"face":"36 24 168 168"}
out={}
for k,v in P.items():
    vb=VB.get(k,"0 0 240 240")
    out[k]=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}">{v}</svg>'
json.dump(out,open("svgs.json","w"))
print(list(out))
