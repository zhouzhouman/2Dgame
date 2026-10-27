"""构建序章地图编辑器（单文件 HTML，离线可用）
用法：python tools/build_editor.py
产出：tools/map_editor.html
"""
import csv, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_prologue_data as G   # 复用同一份地形，保证编辑器与数据表一致

ROOT = os.path.dirname(HERE)
CSV = os.path.join(ROOT, '举火_序章数据_全关卡_v1.1.csv')

HUBS = {
    'hub1': dict(name='城南小院', npcs=[], patrol=[]),
    'hub2': dict(name='逃亡路·废驿', npcs=[
        dict(id='A', name='沈砚', x=12, y=4),
        dict(id='B', name='老仆', x=5, y=7)]),
    'hub3': dict(name='白幡村', npcs=[
        dict(id='A', name='陈三', x=6, y=14),
        dict(id='B', name='老妇', x=13, y=10),
        dict(id='C', name='游医', x=18, y=10),
        dict(id='D', name='病人一', x=8, y=14),
        dict(id='E', name='病人二', x=24, y=15),
        dict(id='F', name='病人三', x=11, y=16),
        dict(id='G', name='衙役甲', x=16, y=19),
        dict(id='H', name='衙役乙', x=17, y=18)],
        patrol=[dict(id='G', loop=True, wp=[[16, 19], [32, 19], [32, 6], [2, 6], [2, 19]]),
                dict(id='H', loop=True, wp=[[17, 18], [17, 13]])]),
    'hub4': dict(name='县城', npcs=[
        dict(id='A', name='米铺老板', x=7, y=5),
        dict(id='B', name='布商', x=14, y=5),
        dict(id='C', name='县丞', x=25, y=5),
        dict(id='D', name='师爷', x=32, y=6),
        dict(id='E', name='掮客', x=25, y=18),
        dict(id='F', name='乞丐', x=20, y=20),
        dict(id='G', name='客栈掌柜', x=9, y=16),
        dict(id='H', name='卖艺的', x=12, y=13)]),
    'hub5': dict(name='山神庙', npcs=[
        dict(id='A', name='老鸦', x=14, y=6),
        dict(id='B', name='庙祝', x=18, y=9)]),
    'hub6': dict(name='官仓+破庙', npcs=[
        dict(id='A', name='使者·招安', x=21, y=7),
        dict(id='B', name='使者·结盟', x=24, y=7),
        dict(id='C', name='使者·合流', x=27, y=7)]),
    'hub7': dict(name='城下营地', npcs=[
        dict(id='A', name='阿禾', x=6, y=6),
        dict(id='B', name='沈砚', x=12, y=6),
        dict(id='C', name='老鸦', x=5, y=9),
        dict(id='D', name='柳三娘', x=14, y=9),
        dict(id='E', name='玄真', x=8, y=10),
        dict(id='F', name='裴无咎', x=11, y=10),
        dict(id='G', name='小满', x=15, y=6)]),
}

def build_data():
    data = {}
    for hub, meta in HUBS.items():
        m, w, h = G.MAPS[hub]
        rows = [''.join(r) for r in m.g]
        data[hub] = dict(name=meta['name'], w=w, h=h, rows=rows,
                         npcs=meta.get('npcs', []), patrol=meta.get('patrol', []))
    return data

def load_points():
    with open(CSV, encoding='utf-8-sig') as f:
        rd = list(csv.DictReader(f))
    out = []
    for r in rd:
        r['x'] = int(r['x']); r['y'] = int(r['y']); r['cost'] = int(r['cost'] or 1)
        out.append(r)
    return out

TEMPLATE = r"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>举火 · 序章地图编辑器</title>
<style>
  :root{--bg:#f7f5f0;--panel:#fff;--line:#d8d3c7;--ink:#2c2c2a;--muted:#6b6a63;
        --amber:#ba7517;--teal:#0f6e56;--coral:#993c1d;--gray:#8a8880;}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--ink);
       font-family:-apple-system,"PingFang SC","Microsoft YaHei",system-ui,sans-serif;font-size:13px}
  header{display:flex;align-items:center;gap:12px;padding:10px 16px;background:#fff;
         border-bottom:1px solid var(--line);flex-wrap:wrap}
  h1{font-size:15px;font-weight:500;margin:0 12px 0 0}
  select,button,input,textarea{font:inherit;color:inherit}
  select,button{padding:4px 10px;border:1px solid var(--line);border-radius:6px;background:#fff;cursor:pointer}
  button:hover{border-color:var(--amber)}
  button.primary{background:var(--amber);color:#fff;border-color:var(--amber)}
  button.danger{color:var(--coral);border-color:#e8c9be}
  main{display:flex;gap:14px;padding:14px;align-items:flex-start}
  #stage{background:#fff;border:1px solid var(--line);border-radius:10px;padding:10px;overflow:auto;flex:1}
  #grid{display:grid;gap:0;user-select:none}
  .cell{width:20px;height:20px;display:flex;align-items:center;justify-content:center;
        font-size:11px;line-height:1;position:relative;cursor:pointer;color:#9a9790}
  .cell:hover{outline:1px solid var(--amber);outline-offset:-1px;z-index:2}
  .t-wall{background:#4a463c;color:#4a463c}
  .t-door{background:#c9a227;color:#c9a227}
  .t-road{background:#e8e2d2}
  .t-grass{background:#eef0e4}
  .t-field{background:#e3e9cf}
  .t-tree{background:#9fb08a}
  .t-water{background:#a9c6d8}
  .t-grave{background:#d6d2c6}
  .t-fence{background:#b0894f}
  .t-wall2{background:#8d8a80}
  .t-fire{background:#e08a3c}
  .mark{position:absolute;inset:1px;border-radius:3px;display:flex;align-items:center;
        justify-content:center;font-size:10px;font-weight:500;color:#fff;cursor:grab}
  .m-surface{background:var(--teal)}
  .m-close{background:#3d6fa8}
  .m-knowledge{background:#7a5aa8}
  .mark.sel{outline:2px solid var(--coral);outline-offset:1px}
  .npc{position:absolute;inset:1px;border-radius:50%;background:var(--amber);color:#fff;
       display:flex;align-items:center;justify-content:center;font-size:10px;font-weight:500;
       box-shadow:0 0 0 1px #fff}
  .wp{position:absolute;inset:7px;border-radius:50%;background:rgba(153,60,29,.55);pointer-events:none}
  aside{width:340px;background:#fff;border:1px solid var(--line);border-radius:10px;padding:12px}
  aside h2{font-size:13px;font-weight:500;margin:0 0 10px;color:var(--muted)}
  fieldset{border:0;padding:0;margin:0}
  label{display:block;margin:8px 0 3px;color:var(--muted);font-size:12px}
  input[type=text],input[type=number],textarea,select{width:100%;padding:5px 7px;
       border:1px solid var(--line);border-radius:6px;background:#fdfcf9}
  textarea{min-height:54px;resize:vertical;line-height:1.5}
  .row{display:flex;gap:8px}
  .row>*{flex:1}
  #issues{margin-top:10px;font-size:12px;color:var(--coral);max-height:110px;overflow:auto}
  #issues div{padding:2px 0}
  #status{padding:0 16px 16px;color:var(--muted);font-size:12px}
  .legend{display:flex;gap:10px;flex-wrap:wrap;color:var(--muted);font-size:11px;margin-top:8px}
  .legend i{display:inline-block;width:9px;height:9px;border-radius:2px;margin-right:3px}
  .hint{color:var(--muted);font-size:12px;line-height:1.6;margin-top:8px}
  kbd{background:#f1efe8;border:1px solid var(--line);border-radius:3px;padding:0 4px;font-size:11px}
</style>
</head>
<body>
<header>
  <h1>举火 · 序章地图编辑器</h1>
  <select id="hubSel"></select>
  <button id="addBtn">点击地图放置新点</button>
  <button id="valBtn">校验</button>
  <button id="expBtn" class="primary">导出 CSV</button>
  <span id="count" style="color:var(--muted)"></span>
</header>
<main>
  <div id="stage"><div id="grid"></div>
    <div class="legend">
      <span><i style="background:#0f6e56"></i>明面</span>
      <span><i style="background:#3d6fa8"></i>细看</span>
      <span><i style="background:#7a5aa8"></i>知识门</span>
      <span><i style="background:#ba7517;border-radius:50%"></i>NPC</span>
      <span><i style="background:#993c1d;border-radius:50%"></i>巡逻路点</span>
    </div>
    <div class="hint">
      <kbd>点击</kbd> 选中/放置 · <kbd>Delete</kbd> 删除选中点 · 改动即时校验，不会写回原文件
    </div>
  </div>
  <aside>
    <h2 id="inspTitle">未选中</h2>
    <fieldset id="insp" disabled>
      <div class="row">
        <div><label>id</label><input type="text" id="f_id"></div>
        <div><label>标记</label><input type="text" id="f_mark" maxlength="3"></div>
      </div>
      <div class="row">
        <div><label>x</label><input type="number" id="f_x"></div>
        <div><label>y</label><input type="number" id="f_y"></div>
        <div><label>刻</label><input type="number" id="f_cost" step="0.5"></div>
      </div>
      <label>层级</label>
      <select id="f_layer">
        <option value="surface">surface 明面</option>
        <option value="close">close 细看</option>
        <option value="knowledge">knowledge 知识门</option>
      </select>
      <label>名称（不进游戏）</label><input type="text" id="f_name">
      <label>前置旗标 requires</label><input type="text" id="f_requires">
      <label>首次文本 text_once</label><textarea id="f_once"></textarea>
      <label>重复文本 text_repeat（必填）</label><textarea id="f_repeat"></textarea>
      <div class="row">
        <div><label>产出 loot</label><input type="text" id="f_loot" placeholder="item:3|item2:1"></div>
      </div>
      <div class="row">
        <div><label>旗标 set_flag</label><input type="text" id="f_flag"></div>
        <div><label>监视 NPC</label><input type="text" id="f_watch" maxlength="1"></div>
      </div>
      <div style="margin-top:12px"><button id="delBtn" class="danger">删除此点</button></div>
    </fieldset>
    <div id="issues"></div>
  </aside>
</main>
<div id="status"></div>
<script>
const DATA = __DATA__;
const POINTS = __POINTS__;
const T = {'#':'t-wall','+':'t-door','.':'t-road',',':'t-grass',':':'t-field','T':'t-tree',
           '~':'t-water','^':'t-grave','=':'t-fence','W':'t-wall2','X':'t-fire'};
const WALK = new Set(['.',':',',','+','^']);

let hub = 'hub3';
let sel = null;      // point id
let addMode = false;

const $ = id => document.getElementById(id);

function initHubSel(){
  const s = $('hubSel');
  Object.entries(DATA).forEach(([k,v])=>{
    const o = document.createElement('option');
    o.value = k; o.textContent = k + ' ' + v.name + '（'+v.w+'×'+v.h+'）';
    s.appendChild(o);
  });
  s.value = hub;
  s.onchange = ()=>{ hub = s.value; sel = null; render(); };
}

function pointAt(x,y){
  return POINTS.find(p=>p.hub===hub && p.x===x && p.y===y);
}

function render(){
  const d = DATA[hub];
  const g = $('grid');
  g.style.gridTemplateColumns = `repeat(${d.w}, 20px)`;
  g.innerHTML = '';
  const wpSet = new Set();
  (d.patrol||[]).forEach(p=>p.wp.forEach(([x,y])=>wpSet.add(x+','+y)));
  const npcAt = {};
  (d.npcs||[]).forEach(n=>npcAt[n.x+','+n.y]=n);

  for(let y=1;y<=d.h;y++){
    for(let x=1;x<=d.w;x++){
      const ch = d.rows[y-1][x-1];
      const c = document.createElement('div');
      c.className = 'cell ' + (T[ch]||'t-grass');
      c.dataset.x = x; c.dataset.y = y;
      const p = pointAt(x,y);
      if(p){
        const m = document.createElement('span');
        m.className = 'mark m-' + p.layer + (sel===p.id?' sel':'');
        m.textContent = p.mark;
        c.appendChild(m);
      }
      if(npcAt[x+','+y]){
        const n = document.createElement('span');
        n.className='npc'; n.textContent = npcAt[x+','+y].id;
        c.appendChild(n);
      } else if(wpSet.has(x+','+y)){
        const w = document.createElement('span'); w.className='wp'; c.appendChild(w);
      }
      c.onclick = ()=>onCell(x,y,ch);
      g.appendChild(c);
    }
  }
  $('count').textContent = POINTS.filter(p=>p.hub===hub).length + ' 条 / 本关';
  validate();
  if(sel) fillInspector(POINTS.find(p=>p.id===sel)); else clearInspector();
}

function onCell(x,y,ch){
  const p = pointAt(x,y);
  if(p){ sel = p.id; render(); return; }
  if(!addMode) return;
  if(!WALK.has(ch)){ alert('这一格不可通行（'+ch+'），不能放点。'); return; }
  const n = POINTS.filter(q=>q.hub===hub).length + 1;
  const id = hub + '_n' + String(n).padStart(2,'0');
  const np = {hub, id, mark:'+', x, y, layer:'surface', name:'新点',
              requires:'', text_once:'', text_repeat:'', loot:'', set_flag:'', watch_npc:'', cost:1};
  POINTS.push(np); sel = id; addMode = false;
  $('addBtn').classList.remove('primary');
  render();
}

function clearInspector(){
  $('inspTitle').textContent = '未选中';
  $('insp').disabled = true;
  ['f_id','f_mark','f_x','f_y','f_cost','f_name','f_requires','f_once','f_repeat','f_loot','f_flag','f_watch']
    .forEach(k=>$(k).value='');
}
function fillInspector(p){
  if(!p){ clearInspector(); return; }
  $('inspTitle').textContent = p.id + '（' + p.layer + '）';
  $('insp').disabled = false;
  $('f_id').value=p.id; $('f_mark').value=p.mark;
  $('f_x').value=p.x; $('f_y').value=p.y; $('f_cost').value=p.cost;
  $('f_layer').value=p.layer; $('f_name').value=p.name;
  $('f_requires').value=p.requires; $('f_once').value=p.text_once;
  $('f_repeat').value=p.text_repeat; $('f_loot').value=p.loot;
  $('f_flag').value=p.set_flag; $('f_watch').value=p.watch_npc;
}

function bind(){
  const map = {f_id:'id',f_mark:'mark',f_x:'x',f_y:'y',f_cost:'cost',f_layer:'layer',
               f_name:'name',f_requires:'requires',f_once:'text_once',f_repeat:'text_repeat',
               f_loot:'loot',f_flag:'set_flag',f_watch:'watch_npc'};
  Object.entries(map).forEach(([el,key])=>{
    $(el).oninput = ()=>{
      const p = POINTS.find(q=>q.id===sel); if(!p) return;
      let v = $(el).value;
      if(['x','y','cost'].includes(key)) v = Number(v)||0;
      p[key] = v;
      if(key==='id'){ sel = v; }
      if(['x','y','mark','layer'].includes(key)){ render(); } else { validate(); }
    };
  });
  $('delBtn').onclick = ()=>{
    const i = POINTS.findIndex(q=>q.id===sel);
    if(i<0) return;
    if(!confirm('删除 ' + sel + '？')) return;
    POINTS.splice(i,1); sel=null; render();
  };
  $('addBtn').onclick = ()=>{
    addMode = !addMode;
    $('addBtn').classList.toggle('primary', addMode);
  };
  $('valBtn').onclick = validate;
  $('expBtn').onclick = exportCSV;
  document.addEventListener('keydown', e=>{
    if(e.key==='Delete' && sel){ $('delBtn').click(); }
  });
}

function validate(){
  const box = $('issues'); box.innerHTML='';
  const errs = [];
  const seen = new Set();
  POINTS.forEach(p=>{
    if(seen.has(p.id)) errs.push(p.id + '：id 重复');
    seen.add(p.id);
    const d = DATA[p.hub]; if(!d) return;
    if(p.x<1||p.x>d.w||p.y<1||p.y>d.h) errs.push(p.id+'：坐标越界');
    else if(!WALK.has(d.rows[p.y-1][p.x-1])) errs.push(p.id+'：落在不可通行格（'+(d.rows[p.y-1][p.x-1])+'）');
    if(!p.text_repeat || !p.text_repeat.trim()) errs.push(p.id+'：text_repeat 为空');
    if(p.layer==='knowledge' && !p.requires) errs.push(p.id+'：知识门缺 requires');
  });
  const pos = {};
  POINTS.forEach(p=>{
    const k = p.hub+':'+p.x+','+p.y;
    if(pos[k]) errs.push(p.id+'：与 '+pos[k]+' 同格');
    else pos[k] = p.id;
  });
  errs.forEach(t=>{ const d=document.createElement('div'); d.textContent='· '+t; box.appendChild(d); });
  $('status').textContent = errs.length ? ('校验：'+errs.length+' 个问题') : '校验通过：0 个问题';
  return errs;
}

function q(s){ return '"' + String(s==null?'':s).replace(/"/g,'""') + '"'; }
function exportCSV(){
  const errs = validate();
  if(errs.length && !confirm('还有 '+errs.length+' 个问题，仍要导出吗？')) return;
  const cols = ['hub','id','mark','x','y','layer','name','requires',
                'text_once','text_repeat','loot','set_flag','watch_npc','cost'];
  const lines = [cols.join(',')];
  POINTS.forEach(p=>{ lines.push(cols.map(c=>q(p[c])).join(',')); });
  const blob = new Blob(['\ufeff' + lines.join('\r\n')], {type:'text/csv;charset=utf-8'});
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = '举火_序章数据_全关卡_v1.2.csv';
  a.click();
  URL.revokeObjectURL(a.href);
}

initHubSel(); bind(); render();
</script>
</body>
</html>
"""

def main():
    data = build_data()
    pts = load_points()
    html = TEMPLATE.replace('__DATA__', json.dumps(data, ensure_ascii=False)) \
                   .replace('__POINTS__', json.dumps(pts, ensure_ascii=False))
    out = os.path.join(HERE, 'map_editor.html')
    with open(out, 'w', encoding='utf-8') as f:
        f.write(html)
    print('OK 写出:', out)
    print('地图数:', len(data), '｜ 数据点:', len(pts))
    print('大小: %.1f KB' % (len(html.encode('utf-8')) / 1024))

if __name__ == '__main__':
    main()
