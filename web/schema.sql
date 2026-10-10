-- schema.sql: creates the Ghost Hands tables in Neon Postgres.
-- lessons (lesson JSON) · videos (uploaded expert videos) · sessions (practice runs) · step_results (per step).
-- Safe to run more than once: "if not exists" skips tables that already exist.

-- One row per lesson. "body" holds the full lesson JSON (schema v2).
create table if not exists lessons (
  lesson_id      text primary key,
  title          text not null,
  hands          text[] not null,
  schema_version int not null,
  body           jsonb not null,
  updated_at     timestamptz not null default now()
);

-- One row per uploaded expert video.
-- uploading → pending (server confirmed the file is in storage) → processing (Dominick's script) → done/failed.
create table if not exists videos (
  id           serial primary key,
  file_name    text not null,
  storage_key  text not null unique,
  status       text not null default 'uploading'
               constraint videos_status_check
               check (status in ('uploading', 'pending', 'processing', 'done', 'failed')),
  lesson_id    text references lessons(lesson_id) on delete set null,
  error        text,
  uploaded_at  timestamptz not null default now()
);

-- One row per practice run on the headset.
create table if not exists sessions (
  id             serial primary key,
  lesson_id      text not null references lessons(lesson_id) on delete cascade,
  tester         text,
  device         text,
  started_at     timestamptz not null default now(),
  completed      boolean not null default false,
  recall_passed  boolean
);

-- One row per step in a practice run.
create table if not exists step_results (
  id                serial primary key,
  session_id        int not null references sessions(id) on delete cascade,
  step_id           text not null,
  time_to_match_ms  int,
  retries           int not null default 0,
  tracking_losses   int not null default 0
);

-- Speeds up the two lookups the server does most.
create index if not exists videos_status_idx on videos (status);
create index if not exists step_results_session_idx on step_results (session_id);

-- Changes after the first version (safe to re-run; bring an existing database up to date).

-- Videos start as 'uploading' until the server confirms the file is in storage.
alter table videos drop constraint if exists videos_status_check;
alter table videos add constraint videos_status_check
  check (status in ('uploading', 'pending', 'processing', 'done', 'failed'));
alter table videos alter column status set default 'uploading';
