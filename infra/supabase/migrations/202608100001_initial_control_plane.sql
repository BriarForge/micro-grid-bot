create type public.bot_mode as enum ('paper', 'demo', 'live');
create type public.bot_status as enum ('offline', 'starting', 'running', 'paused', 'degraded', 'stopping');
create type public.membership_role as enum ('viewer', 'operator', 'owner');
create type public.command_status as enum ('pending', 'claimed', 'succeeded', 'rejected', 'failed', 'expired');

create table public.bot_instances (
  id uuid primary key default gen_random_uuid(),
  name text not null check (char_length(name) between 1 and 80),
  mode public.bot_mode not null default 'paper',
  status public.bot_status not null default 'offline',
  version bigint not null default 0 check (version >= 0),
  last_heartbeat_at timestamptz,
  created_at timestamptz not null default now()
);

create table public.bot_memberships (
  bot_id uuid not null references public.bot_instances(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  role public.membership_role not null default 'viewer',
  created_at timestamptz not null default now(),
  primary key (bot_id, user_id)
);
create index bot_memberships_user_id_idx on public.bot_memberships(user_id, bot_id);

create table public.bot_commands (
  id uuid primary key default gen_random_uuid(),
  bot_id uuid not null references public.bot_instances(id) on delete cascade,
  type text not null check (type in ('pause', 'resume', 'recenter', 'rescale', 'emergency_stop')),
  payload jsonb not null default '{}'::jsonb check (jsonb_typeof(payload) = 'object'),
  requested_by uuid not null references auth.users(id),
  idempotency_key uuid not null,
  status public.command_status not null default 'pending',
  expires_at timestamptz not null,
  claimed_at timestamptz,
  completed_at timestamptz,
  error_code text,
  created_at timestamptz not null default now(),
  unique (bot_id, idempotency_key)
);
create index bot_commands_pending_idx on public.bot_commands(bot_id, created_at) where status = 'pending';
create index bot_commands_requested_by_idx on public.bot_commands(requested_by, created_at desc);

create table public.bot_events (
  id bigint generated always as identity primary key,
  bot_id uuid not null references public.bot_instances(id) on delete cascade,
  sequence bigint not null check (sequence >= 0),
  type text not null,
  correlation_id uuid,
  data jsonb not null default '{}'::jsonb,
  occurred_at timestamptz not null default now(),
  unique (bot_id, sequence)
);
create index bot_events_recent_idx on public.bot_events(bot_id, occurred_at desc);

alter table public.bot_instances enable row level security;
alter table public.bot_memberships enable row level security;
alter table public.bot_commands enable row level security;
alter table public.bot_events enable row level security;

revoke all on public.bot_instances, public.bot_memberships, public.bot_commands, public.bot_events from anon, authenticated;
grant select on public.bot_instances, public.bot_memberships, public.bot_commands, public.bot_events to authenticated;
grant insert (bot_id, type, payload, requested_by, idempotency_key, expires_at) on public.bot_commands to authenticated;

create policy "members can read bot instances" on public.bot_instances for select to authenticated
using (id in (select bot_id from public.bot_memberships where user_id = (select auth.uid())));

create policy "members can read own memberships" on public.bot_memberships for select to authenticated
using (user_id = (select auth.uid()));

create policy "members can read bot commands" on public.bot_commands for select to authenticated
using (bot_id in (select bot_id from public.bot_memberships where user_id = (select auth.uid())));

create policy "operators can request commands" on public.bot_commands for insert to authenticated
with check (
  requested_by = (select auth.uid())
  and status = 'pending'
  and expires_at > now()
  and bot_id in (
    select bot_id from public.bot_memberships
    where user_id = (select auth.uid()) and role in ('operator', 'owner')
  )
);

create policy "members can read bot events" on public.bot_events for select to authenticated
using (bot_id in (select bot_id from public.bot_memberships where user_id = (select auth.uid())));

alter publication supabase_realtime add table public.bot_instances, public.bot_commands, public.bot_events;
