"""Create the bundled catalogue from KelontongAI's public integration examples."""
import html, json, pathlib, re, urllib.request
root=pathlib.Path(__file__).resolve().parents[1]
with urllib.request.urlopen('https://kelontongai.id/docs',timeout=30) as response:
    text=html.unescape(response.read().decode())
ids=set(re.findall(r'"name"\s*:\s*"([a-z0-9][a-z0-9.-]+)"',text))
ids.add('deepseek-v4.1-flash')
def provider(model):
    prefix=model.split('-')[0]
    return {'claude':'Claude','gpt':'GPT','deepseek':'Deepseek','glm':'GLM','kimi':'Kimi','qwen3.8':'Qwen','gemini':'Gemini','muse':'Meta','minimax':'MiniMax'}.get(prefix,prefix.title())
entries=[{'id':model,'provider':provider(model)} for model in sorted(ids) if model!='kelontongai']
if len(entries)<10: raise RuntimeError('Catalogue parsing failed; existing file preserved')
(root/'src/CodingFlashcards/ModelCatalog.json').write_text(json.dumps(entries,indent=2),encoding='utf-8')
print('Bundled model catalogue:',len(entries),'models')
