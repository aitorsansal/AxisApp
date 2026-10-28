import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const { PGlite } = await import(process.env.PGLITE_MODULE || '@electric-sql/pglite')
const db = new PGlite()
const account = '00000000-0000-0000-0000-000000000001'
const group = '00000000-0000-0000-0000-000000000002'
const ids = [3, 4, 5].map(n => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`)
await db.exec(`
  create role anon;
  create role authenticated;
  create schema auth;
  create function auth.uid() returns uuid language sql as $$ select nullif(current_setting('test.uid', true), '')::uuid $$;
  create table groups (id uuid primary key, created_by uuid);
  create table members (id uuid primary key, account_id uuid);
  create table group_members (group_id uuid, member_id uuid);
  create function public.is_group_member(g uuid) returns boolean language sql as $$
    select exists (select 1 from group_members gm join members m on m.id=gm.member_id where gm.group_id=g and m.account_id=auth.uid()) $$;
  create table expenses (
    id uuid primary key default gen_random_uuid(), group_id uuid, paid_by_member_id uuid,
    amount numeric(12,2) not null check(amount>0), currency text, description text, category text,
    occurred_at timestamptz default now(), receipt_path text, is_settlement boolean default false,
    event_id uuid, created_by uuid, created_at timestamptz default now());
  create table expense_shares (expense_id uuid references expenses(id), member_id uuid,
    share_amount numeric(12,2) not null, primary key(expense_id,member_id));
  create table recurring_expenses (
    id uuid primary key default gen_random_uuid(), group_id uuid, paid_by_member_id uuid,
    amount numeric(12,2) not null check(amount>0), currency text, description text, category text,
    frequency text, start_date date, created_by uuid, created_at timestamptz default now(),
    is_active boolean default true, last_processed_date date);
  create table recurring_expense_shares (recurring_expense_id uuid references recurring_expenses(id),
    member_id uuid, share_amount numeric(12,2) not null, primary key(recurring_expense_id,member_id));
  insert into groups values ('${group}','${account}');
  insert into members values ('${ids[0]}','${account}'),('${ids[1]}',null),('${ids[2]}',null);
  insert into group_members values ('${group}','${ids[0]}'),('${group}','${ids[1]}'),('${group}','${ids[2]}');
  select set_config('test.uid','${account}',false);
  insert into expenses(group_id,paid_by_member_id,amount,currency,description,created_by)
    values ('${group}','${ids[0]}',10,'EUR','legacy','${account}');
`)
const migration = readFileSync(new URL('../split_mode.sql', import.meta.url), 'utf8')
const schema = readFileSync(new URL('../schema.sql', import.meta.url), 'utf8')
for (const name of ['save_expense', 'save_recurring_expense', 'materialize_recurring_expenses']) {
  const pattern = new RegExp(`create or replace function public\\.${name}\\([\\s\\S]*?\\n\\$\\$;`)
  assert.equal(migration.match(pattern)?.[0], schema.match(pattern)?.[0], `Fresh schema matches migration: ${name}`)
}
await db.exec(migration)
assert.equal((await db.query("select split_mode from expenses where description='legacy'")).rows[0].split_mode, null)
const shares = amounts => amounts.map((share_amount,i) => ({member_id:ids[i],share_amount}))
const base = {group_id:group, paid_by_member_id:ids[0], amount:10, currency:'EUR', description:'test', category:'food', occurred_at:'2026-10-28T12:00:00Z', is_settlement:false}
async function save(payload, split, recurring=false) {
  const fn = recurring ? 'save_recurring_expense' : 'save_expense'
  return (await db.query(`select ${fn}($1::jsonb,$2::jsonb) as id`, [JSON.stringify(payload),JSON.stringify(split)])).rows[0].id
}
async function row(id,table='expenses') { return (await db.query(`select * from ${table} where id=$1`,[id])).rows[0] }
const equal = shares([3.33,3.34,3.33])
const id = await save({...base,split_mode:'equal'},equal)
assert.equal((await row(id)).split_mode,'equal')
await save({...base,id,description:'old-client note'},[...equal].reverse())
assert.equal((await row(id)).split_mode,'equal','Old client note-only edit preserves Equal')
await save({...base,id},shares([4,3,3]))
assert.equal((await row(id)).split_mode,null,'Old client changed shares invalidates intent')
assert.deepEqual((await db.query('select share_amount from expense_shares where expense_id=$1 order by member_id',[id])).rows.map(r=>r.share_amount),['4.00','3.00','3.00'])
await assert.rejects(save({...base,id,split_mode:'equal'},shares([4,3,3])),/at most one cent/)
assert.equal((await row(id)).split_mode,null,'Rejected metadata rolls back')
await save({...base,id,split_mode:'manual'},shares([5,5]))
assert.equal((await row(id)).split_mode,'manual','Equal-looking Manual remains Manual')
await save({...base,id,amount:12},shares([6,6]))
assert.equal((await row(id)).split_mode,'manual','Old client keeps explicit Manual')
const changedTotal = await save({...base,split_mode:'equal'},equal)
await save({...base,id:changedTotal,amount:12},shares([4,4,4]))
assert.equal((await row(changedTotal)).split_mode,null,'Old client amount change invalidates Equal')
const old = await save({...base},equal)
assert.equal((await row(old)).split_mode,null,'Old clients can still create expenses')
await assert.rejects(save({...base,split_mode:'invalid'},equal),/Invalid split mode/)
await assert.rejects(save({...base,split_mode:'equal'},shares([3,3,3])),/must add up/)
const settlement = await save({...base,is_settlement:true,split_mode:'equal'},shares([10]))
assert.equal((await row(settlement)).split_mode,null,'Repayments have no split intent')
await db.exec("select set_config('test.uid','00000000-0000-0000-0000-000000000099',false)")
await assert.rejects(save({...base,id,split_mode:'equal'},equal),/not a member/)
await db.exec("select set_config('test.uid','',false)")
await assert.rejects(save({...base,split_mode:'equal'},equal),/Not signed in/)
await db.exec(`select set_config('test.uid','${account}',false)`)
const template = {...base,split_mode:'equal',frequency:'monthly',start_date:new Date().toISOString().slice(0,10)}
const tid = await save(template,equal,true)
assert.equal((await row(tid,'recurring_expenses')).split_mode,'equal')
await db.exec('select materialize_recurring_expenses()')
assert.equal((await db.query("select count(*)::int as n from expenses where split_mode='equal'")).rows[0].n,1,'Generated expense inherits Equal')
const before = await row(tid,'recurring_expenses')
await db.query('update recurring_expenses set is_active=false where id=$1',[tid])
await save({...template,id:tid,description:'new note'},equal,true)
const after = await row(tid,'recurring_expenses')
assert.deepEqual(after.last_processed_date,before.last_processed_date)
assert.equal(after.is_active,false,'Editing does not resume a paused template')
assert.equal(after.created_at.toISOString(),before.created_at.toISOString())
const oldTemplate = {...template,id:tid}; delete oldTemplate.split_mode
await save(oldTemplate,[...equal].reverse(),true)
assert.equal((await row(tid,'recurring_expenses')).split_mode,'equal')
await save(oldTemplate,shares([4,3,3]),true)
assert.equal((await row(tid,'recurring_expenses')).split_mode,null)
await assert.rejects(save({...template,id:tid},shares([4,3,3]),true),/at most one cent/)
const snapshot = await db.query('select id,amount,description,split_mode from expenses order by id')
await db.exec(migration)
assert.deepEqual((await db.query('select id,amount,description,split_mode from expenses order by id')).rows,snapshot.rows,'Rerun preserves all data')
const grants = await db.query("select has_function_privilege('anon','save_expense(jsonb,jsonb)','execute') as anon, has_function_privilege('authenticated','save_expense(jsonb,jsonb)','execute') as app, has_function_privilege('authenticated','materialize_recurring_expenses()','execute') as cron")
assert.deepEqual(grants.rows[0],{anon:false,app:true,cron:false})
await db.close()
console.log('Split-mode SQL passed: idempotency, preserved legacy data, Equal/Manual, old writers, authorization, atomic rejection, recurrence and grants.')
