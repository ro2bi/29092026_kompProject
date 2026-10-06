'use strict';
const $ = id => document.getElementById(id);
let state = null, busy = false, configured = false;
const names = ['Знайди свій спосіб', 'Закріпи навичку', 'Спробуй самостійно'];
async function request(path, body) {
  const response = await fetch('/api/' + path, body === undefined ? {} : {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  if (!response.ok) {
    if (response.status === 404) throw new Error('Сесію завершено або видалено. Натисни «Почати тренування» чи онови сторінку.');
    if (response.status === 429) throw new Error('Забагато запитів за хвилину. Зачекай трохи й повтори.');
    if (response.status === 409) throw new Error('Крок змінився в іншій вкладці. Онови сторінку.');
    throw new Error('Не вдалося виконати дію. Перевір, чи працює сервер, і спробуй ще раз.');
  }
  return response.json();
}
function mode() {
  $('mode').textContent = configured && $('api-consent').checked ? 'API увімкнено' : 'Локальний ШІ';
}
async function action(fn) {
  if(busy) return;
  busy=true; $('error').hidden=true;
  document.querySelectorAll('button').forEach(b => b.disabled=true);
  try { await fn(); } catch(e) { $('error').textContent=e.message; $('error').hidden=false; }
  finally { busy=false; document.querySelectorAll('button').forEach(b=>b.disabled=false); }
}
function fraction(n,d) {
  const f=document.createElement('span');f.className='fraction';
  for(const value of [n,d]) { const s=document.createElement('span');s.textContent=value;f.append(s); }
  return f;
}
function draw() {
  $('welcome').hidden=!!state; $('lesson').hidden=!state || state.finished; $('summary').hidden=!state?.finished;
  $('grade-badge').textContent=state ? `${state.grade} КЛАС` : '5–9 КЛАС';
  if(!state) return;
  $('grade').value=state.grade;
  const e=state.exercise;
  $('round-name').textContent=`ВПРАВА 0${state.round+1} / 03`;
  $('phase').textContent=state.transfer?'Самостійно':'Разом із помічником';
  $('exercise-title').textContent=names[state.round];
  $('context').textContent=e.context;
  $('expression').replaceChildren(fraction(e.a,e.b),document.createTextNode('+'),fraction(e.c,e.d),document.createTextNode('= ?'));
  $('expression').setAttribute('aria-label',e.expression+' — знайди суму');
  $('fraction-bars').replaceChildren();
  for(const [n,d] of [[e.a,e.b],[e.c,e.d]]) {
    const group=document.createElement('div');group.className='bar-group';
    const bar=document.createElement('div');bar.className='bar';bar.setAttribute('aria-hidden','true');
    for(let i=0;i<d;i++){const piece=document.createElement('i');if(i<Math.abs(n))piece.className='filled';bar.append(piece);}
    const caption=document.createElement('small');caption.textContent=`${n < 0 ? "Мінус " : ""}${Math.abs(n)} із ${d} рівних частин${n < 0 ? " (показано модуль)" : ""}`;group.append(bar,caption);$('fraction-bars').append(group);
  }
  [...$('steps').children].forEach((s,i)=>s.classList.toggle('current',i===state.stage));
  $('steps').hidden=state.transfer;
  [...$('route').children].forEach((s,i)=>{s.classList.toggle('current',i===state.round);if(i===state.round)s.setAttribute('aria-current','step');else s.removeAttribute('aria-current');});
  $('work').replaceChildren(...state.work.map(w=>{const p=document.createElement('p');p.textContent='✓ '+w;return p;}));
  $('question').textContent=state.question;
  $('feedback').textContent=state.message;
  $('feedback').parentElement.classList.toggle('success',state.tone==='success');
  $('model-info').textContent=`Джерело: ${state.modelSource}. ${state.modelLabel ? 'Припущення ШІ: '+state.modelLabel+'.' : 'Додай пояснення, щоб помічник міг розпізнати твій спосіб.'}`;
  $('correct').textContent=state.correctSteps; $('hints').textContent=state.hints;
  $('progress-bar').style.width=(state.correctSteps/7*100)+'%';
  $('answer-form').hidden=state.complete; $('next').hidden=!state.complete;
  $('next').textContent=state.transfer?'Показати мій результат →':'Наступна вправа →';
  $('hint').hidden=state.transfer;
  $('optional').textContent=state.transfer?'Потрібно для завершення':'Необов’язково';
  $('answer').placeholder=state.transfer || state.stage===2?'Наприклад: 3/5':state.stage===1?'Два чисельники через пробіл':'Напиши число';
  $('input-help').textContent=state.transfer || state.stage===2?'Формат: чисельник/знаменник. Рівні дроби теж приймаються.':state.stage===1?'Спочатку чисельник першого дробу, потім другого.':'Обери додатний спільний знаменник, не більше 120.';
  if(state.finished) {
    $('summary-stats').textContent=`3 вправи завершено · ${state.correctSteps} правильних кроків · ${state.attempts} спроб · ${state.hints} підказок. Самостійну задачу перевірено обчисленням.`;
    $('plan').replaceChildren(...state.plan.map(text=>{const li=document.createElement('li');li.textContent=text;return li;}));
  }
}
async function start(){state=await request('start',{grade:Number($('grade').value)});$('answer').value='';$('reasoning').value='';draw();$('answer').focus();}
$('start').addEventListener('click',()=>action(start));
$('again').addEventListener('click',()=>action(start));
$('answer-form').addEventListener('submit',event=>{event.preventDefault();action(async()=>{
  const oldStage=state.stage;
  state=await request('answer',{answer:$('answer').value,reasoning:$('reasoning').value,apiConsent:$('api-consent').checked,revision:state.revision});
  if(state.stage!==oldStage){$('answer').value='';$('reasoning').value='';}
  draw();$('feedback').focus();
});});
$('hint').addEventListener('click',()=>action(async()=>{state=await request('hint',{revision:state.revision});draw();$('feedback').focus();}));
$('next').addEventListener('click',()=>action(async()=>{state=await request('next',{revision:state.revision});$('answer').value='';$('reasoning').value='';draw();if(!state.finished)$('answer').focus();else $('summary').scrollIntoView({behavior:'smooth'});}));
function erase(){return action(async()=>{await request('reset',{});state=null;$('answer').value='';$('reasoning').value='';$('api-consent').checked=false;mode();draw();$('start').focus();});}
$('choose-level').addEventListener('click',erase);
$('delete').addEventListener('click',erase);
$('delete-mobile').addEventListener('click',erase);
$('api-consent').addEventListener('change',mode);
$('download').addEventListener('click',()=>{
  if(!state?.finished)return;
  const a=document.createElement('a');a.href='/api/plan';a.download='DribKrok-plan.txt';a.click();
});
action(async()=>{
  const health=await request('health');configured=health.apiConfigured;
  $('api-consent').disabled=!configured;
  $('api-note').textContent=configured?'Надсилається лише пояснення. Дозвіл можна вимкнути будь-коли.':'API ще не налаштовано. Локальна модель вже працює без ключа.';
  mode();
  const response=await fetch('/api/state');if(response.ok)state=await response.json();draw();
});
