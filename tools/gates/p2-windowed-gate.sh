#!/bin/zsh
# P2 windowed eye gate (plan §Gates.2): Continents view across the window —
#  (a) >= 3 distinct land masses per frame,
#  (b) consecutive-frame pixel diff >= 10%,
#  (c) land-mask pixel-area stability (total land area roughly conserved while positions change).
set -eu
cd /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot
SP="${GATE_OUT:-/tmp/fantasim-gates}"
mkdir -p "$SP"
LOG="$SP/p2-app-run.log"
VER=$(readlink build/_artifacts/latest)
EXE="build/_artifacts/$VER/godot/osx/complete-app.app/Contents/MacOS/complete-app"
test -x "$EXE" || { echo "no exe at $EXE"; exit 1; }

pkill -f 'complete-app.app/Contents/MacOS/complete-app' 2>/dev/null || true
sleep 2
export remote__enabled=true
"$EXE" > "$LOG" 2>&1 &
APP_PID=$!
echo "launched $VER pid $APP_PID"
for i in $(seq 1 60); do
  curl -s --max-time 1 http://127.0.0.1:19292/health >/dev/null 2>&1 && { echo "remote up after ${i}s"; break; }
  sleep 1
done
sleep 5
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":100000000}' >/dev/null
sleep 3
python3 tools/fantasim-cmd.py cmd timeline.select_layer '{"sphereId":"geosphere","layerId":"geosphere.plate"}' \
  | python3 -c "import sys,json;d=json.load(sys.stdin);print('select:', d.get('resultJson') or d.get('error'))"

capture_after_bind() {
  before=$(grep -c 'Planet plate surface bound' "$LOG" || true)
  python3 tools/fantasim-cmd.py cmd timeline.seek "{\"tick\":$1}" >/dev/null
  for i in $(seq 1 40); do
    now=$(grep -c 'Planet plate surface bound' "$LOG" || true)
    [ "$now" -gt "$before" ] && { sleep 1; break; }
    sleep 1
  done
  python3 tools/fantasim-cmd.py cmd render.screenshot "{\"path\":\"$2\"}" >/dev/null
  echo "captured $2"
}

for T in 100000000 105000000 110000000 115000000 120000000; do
  capture_after_bind $T "$SP/p2-gate-$T.png"
done

python3 - <<'EOF'
from PIL import Image
import sys
SP="${GATE_OUT:-/tmp/fantasim-gates}"
mkdir -p "$SP"
box=(1660,540,2560,1360)
ticks=[100000000,105000000,110000000,115000000,120000000]
# land tone from ContinentsPalette.Land (0.66,0.55,0.34) -> ~(168,140,87); frontier-darkened ~x0.68
def is_land(px):
    r,g,b = px
    return r>100 and g>80 and b<140 and r>b+30 and g>b+15  # warm tan family, incl. darkened frontier
def components(mask,w,h):
    seen=set(); comps=[]
    for start in mask:
        if start in seen: continue
        stack=[start]; seen.add(start); size=0
        while stack:
            x,y=stack.pop(); size+=1
            for dx,dy in ((2,0),(-2,0),(0,2),(0,-2)):
                n=(x+dx,y+dy)
                if n in mask and n not in seen:
                    seen.add(n); stack.append(n)
        comps.append(size)
    return sorted(comps, reverse=True)
imgs=[Image.open(f"{SP}/p2-gate-{t}.png").convert('RGB').crop(box) for t in ticks]
ok=True
areas=[]
for i,img in enumerate(imgs):
    px=img.load(); w,h=img.size
    mask={(x,y) for y in range(0,h,2) for x in range(0,w,2) if is_land(px[x,y])}
    comps=[c for c in components(mask,w,h) if c>=120]  # ignore specks
    areas.append(len(mask))
    n=len(comps)
    status="PASS" if n>=3 else "FAIL"
    if n<3: ok=False
    print(f"t={ticks[i]/1e6:.0f}M: landmasses(visible)={n} sizes={comps[:6]} land_px={len(mask)} [{status}]")
    img.save(f"{SP}/p2-globe-{ticks[i]}.png")
for i in range(len(imgs)-1):
    a,b=imgs[i].load(),imgs[i+1].load(); w,h=imgs[i].size
    ncount=diff=0
    for y in range(0,h,4):
        for x in range(0,w,4):
            d=sum(abs(a[x,y][c]-b[x,y][c]) for c in range(3)); ncount+=1
            if d>30: diff+=1
    pct=100*diff/ncount
    st="PASS" if pct>=10 else "FAIL"
    if pct<10: ok=False
    print(f"{ticks[i]/1e6:.0f}M->{ticks[i+1]/1e6:.0f}M: {pct:.1f}% changed [{st}]")
mn,mx=min(areas),max(areas)
stab = "PASS" if mn>0 and mx/max(mn,1)<=2.0 else "WARN"
print(f"land area range {mn}-{mx} px (visible-hemisphere variation) [{stab}]")
print("GATE:", "PASS" if ok else "FAIL")
EOF
echo "app left running (pid $APP_PID)"
