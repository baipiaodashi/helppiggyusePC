const { chromium } = require('playwright');
const fs=require('fs');
(async()=>{
 const svgs=JSON.parse(fs.readFileSync(process.argv[2]));
 const outDir=process.argv[3]; const size=+process.argv[4]||480;
 const b=await chromium.launch(); const p=await b.newPage({viewport:{width:size,height:size}});
 for(const [k,s] of Object.entries(svgs)){
   await p.setContent(`<html><body style="margin:0;background:transparent">${s.replace('<svg ','<svg width="'+size+'" height="'+size+'" ')}</body></html>`);
   await p.screenshot({path:`${outDir}/${k}.png`,omitBackground:true,clip:{x:0,y:0,width:size,height:size}});
 }
 // contact sheet
 const imgs=Object.values(svgs).map(s=>`<div style="width:220px;height:220px;display:inline-block;margin:6px;background:#FFF4F6;border-radius:20px">${s.replace('<svg ','<svg width="220" height="220" ')}</div>`).join('');
 await p.setViewportSize({width:960,height:500});
 await p.setContent(`<body style="margin:0;background:#fff">${imgs}</body>`);
 await p.screenshot({path:`${outDir}/_sheet.png`});
 await b.close();
})();
