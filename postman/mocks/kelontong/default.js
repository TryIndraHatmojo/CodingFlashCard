const http = require('http');
http.createServer(async (req, res) => {
  let body; try { body = typeof req.body === 'string' ? JSON.parse(req.body) : (req.body || {}); } catch { res.writeHead(400); res.end('{}'); return; }
  res.setHeader('Content-Type', 'application/json');
  if(req.method==='GET' && req.url==='/v1/models'){res.end(JSON.stringify({data:[{id:'deepseek-v4.1-flash'}]}));return;}
  if(req.method!=='POST' || req.url!=='/v1/chat/completions'){res.writeHead(501);res.end('{}');return;}
  const errors={unauthorized:401,missing:404,rate:429,server:500};
  if(errors[body.model]){res.writeHead(errors[body.model]);res.end('{}');return;}
  if(body.model==='slow')await new Promise(resolve=>setTimeout(resolve,2500));
  let content='{"ok":true}';
  const prompt=body.messages?.[1]?.content || '';
  if(prompt.includes('spesifikasi JSON berikut: ')) {
    const spec=JSON.parse(prompt.split('spesifikasi JSON berikut: ')[1].split('\nNonce:')[0]);
    const questions=spec.requests.map(({language,category},i)=>({language,category,difficulty:spec.difficulty==='Campuran'?'Sedang':spec.difficulty,
      prompt:`Soal ${i+1} ${prompt.split("Nonce: ")[1]?.split(".")[0]}: tentukan nilai hasil.`,code:language==='sql'?'CREATE TABLE items (value INTEGER);\nINSERT INTO items VALUES (1), (2);\nSELECT SUM(value) FROM items;':'// Fixture tampilan; bukan kode provider\nvalue = 1 + 2\n    // indentasi dipertahankan',
      options:['3','2','1','0'],answerIndex:0,explanation:'1 + 2 menghasilkan 3.'}));
    switch(body.model){
      case 'wrong-language':questions[0].language='unknown';break;
      case 'wrong-category':questions[0].category='unknown';break;
      case 'wrong-difficulty':questions[0].difficulty='Sulit';break;
      case 'missing-code':questions[0].code='';break;
      case 'bad-options':questions[0].options=['x','x','y','z'];break;
      case 'missing-answer':delete questions[0].answerIndex;break;
      case 'null-options':questions[0].options=null;break;
      case 'wrong-count':questions.pop();break;
      case 'duplicate':questions[1]={...questions[0]};break;
    }
    content=JSON.stringify({questions});
    if(body.model==='fenced')content='```json\n'+content+'\n```';
  }
  if(body.model==='malformed')content='invalid JSON';
  res.end(JSON.stringify({choices:[{message:{role:'assistant',content}}]}));
}).listen(process.env.PORT || 4010);
