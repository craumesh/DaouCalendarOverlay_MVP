const fs=require("fs");const s=fs.readFileSync(process.argv[2],"utf8");
const body=s.replace(/<style>[\s\S]*?<\/style>/,"").replace(/<!doctype[^>]*>/i,"");
const VOID=new Set(["meta","img","br","hr","input","link"]);const st=[];const errs=[];
const re=/<(\/?)([a-zA-Z][a-zA-Z0-9]*)\b[^>]*?(\/?)>/g;let m;
const lineOf=i=>s.slice(0,s.indexOf(body.slice(i,i+80))).split("\n").length;
while((m=re.exec(body))){const [,close,t0,self]=m;const t=t0.toLowerCase();if(VOID.has(t)||self)continue;
 if(!close)st.push([t,m.index]);else{const top=st.pop();if(!top||top[0]!==t){errs.push(`mismatch </${t}> expected </${top?top[0]:"(none)"}> near line ${lineOf(m.index)}`);if(top)st.push(top);if(errs.length>10)break;}}}
st.forEach(([t,i])=>errs.push(`unclosed <${t}> near line ${lineOf(i)}`));
for(const pm of body.matchAll(/<pre><code>([\s\S]*?)<\/code><\/pre>/g)){const c=pm[1];if(/<(?!\/?(strong|em|span)\b)/.test(c))errs.push("unescaped < in pre near line "+lineOf(pm.index));if(/&(?!(lt|gt|amp|quot|nbsp|#[0-9]+|#x[0-9a-fA-F]+);)/.test(c))errs.push("unescaped & in pre near line "+lineOf(pm.index));}
const ids=[...s.matchAll(/\sid="([^"]+)"/g)].map(x=>x[1]);const dup=ids.filter((x,i)=>ids.indexOf(x)!==i);if(dup.length)errs.push("duplicate id: "+dup);
const nav=(s.match(/<nav>([\s\S]*?)<\/nav>/)||[])[1]||"";for(const h of nav.matchAll(/href="#([^"]+)"/g))if(!ids.includes(h[1]))errs.push("nav target missing: #"+h[1]);
const h2=[...s.matchAll(/<h2>(\d+)\./g)].map(x=>+x[1]);h2.forEach((n,i)=>{if(n!==i+1)errs.push(`h2 numbering break at index ${i}: ${n}`)});
console.log(errs.length?errs.join("\n"):`OK tags balanced, ids=${ids.length}, h2=1..${h2.length}, nav links=${(nav.match(/<a /g)||[]).length}`);process.exit(errs.length?1:0);
