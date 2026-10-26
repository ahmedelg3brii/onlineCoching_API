-- Coaching SaaS — importer-friendly DDL (tables, keys, relationships only).
-- Full production schema with RLS, triggers and checks: schema.sql

CREATE TABLE audit_logs (
  id bigint PRIMARY KEY,
  tenant_id uuid NOT NULL,
  actor_user_id uuid,
  action text NOT NULL,
  entity_type text NOT NULL,
  entity_id text NOT NULL,
  changes jsonb,
  ip_address inet,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE body_measurements (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  check_in_id uuid,
  measured_on date NOT NULL,
  type text NOT NULL,
  value_cm numeric(5,1) NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE check_ins (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  subscription_id uuid,
  check_in_date date NOT NULL,
  status text NOT NULL DEFAULT 'submitted',
  weight_kg numeric(5,2),
  body_fat_pct numeric(4,1),
  energy_level smallint,
  sleep_quality smallint,
  nutrition_adherence_pct smallint,
  training_adherence_pct smallint,
  client_notes text,
  coach_score smallint,
  coach_feedback text,
  reviewed_by_member_id uuid,
  reviewed_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE client_subscriptions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  package_id uuid NOT NULL,
  status text NOT NULL DEFAULT 'pending_payment',
  includes_nutrition boolean NOT NULL,
  includes_training boolean NOT NULL,
  price numeric(12,2) NOT NULL,
  currency char(3) NOT NULL,
  starts_at timestamptz,
  ends_at timestamptz,
  paused_at timestamptz,
  cancelled_at timestamptz,
  auto_renew boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE clients (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  user_id uuid NOT NULL,
  coach_member_id uuid,
  status text NOT NULL DEFAULT 'lead',
  gender text,
  birth_date date,
  height_cm numeric(5,1),
  starting_weight_kg numeric(5,2),
  goal text,
  activity_level text,
  medical_notes text,
  coach_private_notes text,
  onboarded_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE conversation_read_states (
  tenant_id uuid NOT NULL,
  conversation_id uuid NOT NULL,
  user_id uuid NOT NULL,
  last_read_message_id bigint,
  last_read_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (conversation_id, user_id)
);

CREATE TABLE conversations (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  last_message_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE daily_logs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  log_date date NOT NULL,
  weight_kg numeric(5,2),
  steps integer,
  water_ml integer,
  sleep_hours numeric(3,1),
  mood smallint,
  notes text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE exercises (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid,
  name text NOT NULL,
  name_ar text,
  primary_muscle text NOT NULL,
  secondary_muscles text[] NOT NULL DEFAULT '{}',
  equipment text,
  video_url text,
  instructions text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE foods (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid,
  name text NOT NULL,
  name_ar text,
  brand text,
  calories_per_100g numeric(7,2) NOT NULL,
  protein_per_100g numeric(6,2) NOT NULL DEFAULT 0,
  carbs_per_100g numeric(6,2) NOT NULL DEFAULT 0,
  fat_per_100g numeric(6,2) NOT NULL DEFAULT 0,
  fiber_per_100g numeric(6,2) NOT NULL DEFAULT 0,
  default_serving_g numeric(7,2) NOT NULL DEFAULT 100,
  serving_label text,
  is_verified boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE media_files (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  uploaded_by_user_id uuid NOT NULL,
  purpose text NOT NULL,
  storage_key text NOT NULL UNIQUE,
  content_type text NOT NULL,
  size_bytes bigint NOT NULL,
  width integer,
  height integer,
  created_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE messages (
  id bigint PRIMARY KEY,
  tenant_id uuid NOT NULL,
  conversation_id uuid NOT NULL,
  sender_user_id uuid NOT NULL,
  kind text NOT NULL DEFAULT 'text',
  body text,
  media_file_id uuid,
  reply_to_message_id bigint,
  created_at timestamptz NOT NULL DEFAULT now(),
  edited_at timestamptz,
  deleted_at timestamptz
);

CREATE TABLE notifications (
  id bigint PRIMARY KEY,
  tenant_id uuid NOT NULL,
  user_id uuid NOT NULL,
  type text NOT NULL,
  title text NOT NULL,
  body text,
  data jsonb NOT NULL DEFAULT '{}',
  read_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE nutrition_logs (
  id bigint PRIMARY KEY,
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  log_date date NOT NULL,
  plan_meal_id uuid,
  food_id uuid,
  custom_name text,
  quantity_g numeric(7,2) NOT NULL,
  calories numeric(7,2) NOT NULL,
  protein_g numeric(6,2) NOT NULL DEFAULT 0,
  carbs_g numeric(6,2) NOT NULL DEFAULT 0,
  fat_g numeric(6,2) NOT NULL DEFAULT 0,
  logged_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE nutrition_plan_meal_items (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  meal_id uuid NOT NULL,
  food_id uuid,
  custom_name text,
  quantity_g numeric(7,2) NOT NULL,
  calories numeric(7,2) NOT NULL,
  protein_g numeric(6,2) NOT NULL DEFAULT 0,
  carbs_g numeric(6,2) NOT NULL DEFAULT 0,
  fat_g numeric(6,2) NOT NULL DEFAULT 0,
  alternative_group smallint,
  sort_order integer NOT NULL DEFAULT 0
);

CREATE TABLE nutrition_plan_meals (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  plan_id uuid NOT NULL,
  name text NOT NULL,
  sort_order integer NOT NULL DEFAULT 0,
  target_time time,
  notes text
);

CREATE TABLE nutrition_plans (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid,
  is_template boolean NOT NULL DEFAULT false,
  created_by_member_id uuid,
  name text NOT NULL,
  status text NOT NULL DEFAULT 'draft',
  starts_on date,
  ends_on date,
  target_calories integer NOT NULL,
  protein_g integer NOT NULL,
  carbs_g integer NOT NULL,
  fat_g integer NOT NULL,
  fiber_g integer,
  water_ml integer,
  macro_calories integer,
  protein_pct numeric(5,1),
  carbs_pct numeric(5,1),
  fat_pct numeric(5,1),
  notes text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE packages (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  description text,
  includes_nutrition boolean NOT NULL DEFAULT true,
  includes_training boolean NOT NULL DEFAULT false,
  duration_days integer NOT NULL,
  price numeric(12,2) NOT NULL,
  currency char(3) NOT NULL DEFAULT 'EGP',
  check_in_interval_days integer NOT NULL DEFAULT 7,
  onboarding_questionnaire_id uuid,
  is_active boolean NOT NULL DEFAULT true,
  sort_order integer NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE payment_webhook_events (
  id bigint PRIMARY KEY,
  provider text NOT NULL,
  event_id text NOT NULL,
  event_type text,
  payload jsonb NOT NULL,
  received_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz,
  error text
);

CREATE TABLE payments (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  subscription_id uuid,
  amount numeric(12,2) NOT NULL,
  platform_fee numeric(12,2) NOT NULL DEFAULT 0,
  currency char(3) NOT NULL,
  provider text NOT NULL,
  provider_payment_id text,
  idempotency_key text NOT NULL UNIQUE,
  status text NOT NULL DEFAULT 'pending',
  refunded_amount numeric(12,2) NOT NULL DEFAULT 0,
  failure_reason text,
  paid_at timestamptz,
  metadata jsonb NOT NULL DEFAULT '{}',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE platform_plans (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  code text NOT NULL UNIQUE,
  name text NOT NULL,
  monthly_price numeric(12,2) NOT NULL,
  currency char(3) NOT NULL DEFAULT 'USD',
  max_clients integer,
  max_coaches integer,
  features jsonb NOT NULL DEFAULT '{}',
  is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE progress_photos (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  check_in_id uuid,
  media_file_id uuid NOT NULL,
  pose text NOT NULL,
  taken_on date NOT NULL,
  coach_comment text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE questionnaire_answers (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  response_id uuid NOT NULL,
  question_id uuid NOT NULL,
  prompt_snapshot text NOT NULL,
  value jsonb,
  media_file_id uuid,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE questionnaire_questions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  questionnaire_id uuid NOT NULL,
  prompt text NOT NULL,
  help_text text,
  type text NOT NULL,
  options jsonb,
  validation jsonb,
  is_required boolean NOT NULL DEFAULT false,
  sort_order integer NOT NULL DEFAULT 0,
  archived_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE questionnaire_responses (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  questionnaire_id uuid NOT NULL,
  client_id uuid NOT NULL,
  check_in_id uuid,
  status text NOT NULL DEFAULT 'in_progress',
  submitted_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE questionnaires (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  title text NOT NULL,
  description text,
  kind text NOT NULL,
  is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE tenant_members (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  user_id uuid NOT NULL,
  role text NOT NULL,
  status text NOT NULL DEFAULT 'active',
  bio text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE tenant_payment_accounts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  provider text NOT NULL,
  provider_account_id text,
  status text NOT NULL DEFAULT 'pending',
  is_default boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE tenant_plan_subscriptions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  plan_id uuid NOT NULL,
  status text NOT NULL,
  current_period_start timestamptz NOT NULL,
  current_period_end timestamptz NOT NULL,
  provider text,
  provider_subscription_id text,
  cancel_at_period_end boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE tenants (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name text NOT NULL,
  slug varchar(255) NOT NULL UNIQUE,
  status text NOT NULL DEFAULT 'trial',
  plan_id uuid,
  default_currency char(3) NOT NULL DEFAULT 'EGP',
  timezone text NOT NULL DEFAULT 'Africa/Cairo',
  default_locale text NOT NULL DEFAULT 'ar',
  branding jsonb NOT NULL DEFAULT '{}',
  settings jsonb NOT NULL DEFAULT '{}',
  trial_ends_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE users (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  email varchar(255) NOT NULL UNIQUE,
  email_verified_at timestamptz,
  phone text,
  password_hash text,
  full_name text NOT NULL,
  avatar_url text,
  locale text NOT NULL DEFAULT 'ar',
  is_platform_admin boolean NOT NULL DEFAULT false,
  last_login_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  deleted_at timestamptz
);

CREATE TABLE workout_day_exercises (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  workout_day_id uuid NOT NULL,
  exercise_id uuid NOT NULL,
  sort_order integer NOT NULL DEFAULT 0,
  superset_group smallint,
  sets smallint NOT NULL,
  reps_min smallint,
  reps_max smallint,
  target_weight_kg numeric(6,2),
  target_rpe numeric(3,1),
  rest_seconds integer,
  tempo text,
  notes text
);

CREATE TABLE workout_days (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  program_id uuid NOT NULL,
  name text NOT NULL,
  day_index smallint NOT NULL,
  week_number smallint,
  notes text
);

CREATE TABLE workout_programs (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid,
  is_template boolean NOT NULL DEFAULT false,
  created_by_member_id uuid,
  name text NOT NULL,
  goal text,
  status text NOT NULL DEFAULT 'draft',
  starts_on date,
  ends_on date,
  duration_weeks smallint,
  notes text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE workout_sessions (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  client_id uuid NOT NULL,
  workout_day_id uuid,
  started_at timestamptz NOT NULL DEFAULT now(),
  completed_at timestamptz,
  perceived_effort smallint,
  client_notes text,
  coach_comment text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE workout_set_logs (
  id bigint PRIMARY KEY,
  tenant_id uuid NOT NULL,
  session_id uuid NOT NULL,
  day_exercise_id uuid,
  exercise_id uuid NOT NULL,
  set_number smallint NOT NULL,
  reps smallint,
  weight_kg numeric(6,2),
  rpe numeric(3,1),
  is_warmup boolean NOT NULL DEFAULT false,
  notes text,
  logged_at timestamptz NOT NULL DEFAULT now()
);

-- Relationships
ALTER TABLE audit_logs ADD FOREIGN KEY (actor_user_id) REFERENCES users (id);
ALTER TABLE audit_logs ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE body_measurements ADD FOREIGN KEY (check_in_id) REFERENCES check_ins (id) ON DELETE SET NULL;
ALTER TABLE body_measurements ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE body_measurements ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE check_ins ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE check_ins ADD FOREIGN KEY (reviewed_by_member_id) REFERENCES tenant_members (id) ON DELETE SET NULL;
ALTER TABLE check_ins ADD FOREIGN KEY (subscription_id) REFERENCES client_subscriptions (id) ON DELETE SET NULL;
ALTER TABLE check_ins ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE client_subscriptions ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE client_subscriptions ADD FOREIGN KEY (package_id) REFERENCES packages (id);
ALTER TABLE client_subscriptions ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE clients ADD FOREIGN KEY (coach_member_id) REFERENCES tenant_members (id) ON DELETE SET NULL;
ALTER TABLE clients ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE clients ADD FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE conversation_read_states ADD FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE;
ALTER TABLE conversation_read_states ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE conversation_read_states ADD FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE conversations ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE conversations ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE daily_logs ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE daily_logs ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE exercises ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE foods ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE media_files ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE media_files ADD FOREIGN KEY (uploaded_by_user_id) REFERENCES users (id);
ALTER TABLE messages ADD FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE;
ALTER TABLE messages ADD FOREIGN KEY (media_file_id) REFERENCES media_files (id);
ALTER TABLE messages ADD FOREIGN KEY (reply_to_message_id) REFERENCES messages (id) ON DELETE SET NULL;
ALTER TABLE messages ADD FOREIGN KEY (sender_user_id) REFERENCES users (id);
ALTER TABLE messages ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE notifications ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE notifications ADD FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE nutrition_logs ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE nutrition_logs ADD FOREIGN KEY (food_id) REFERENCES foods (id);
ALTER TABLE nutrition_logs ADD FOREIGN KEY (plan_meal_id) REFERENCES nutrition_plan_meals (id) ON DELETE SET NULL;
ALTER TABLE nutrition_logs ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plan_meal_items ADD FOREIGN KEY (food_id) REFERENCES foods (id);
ALTER TABLE nutrition_plan_meal_items ADD FOREIGN KEY (meal_id) REFERENCES nutrition_plan_meals (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plan_meal_items ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plan_meals ADD FOREIGN KEY (plan_id) REFERENCES nutrition_plans (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plan_meals ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plans ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE nutrition_plans ADD FOREIGN KEY (created_by_member_id) REFERENCES tenant_members (id) ON DELETE SET NULL;
ALTER TABLE nutrition_plans ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE packages ADD FOREIGN KEY (onboarding_questionnaire_id) REFERENCES questionnaires (id) ON DELETE SET NULL;
ALTER TABLE packages ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE payments ADD FOREIGN KEY (client_id) REFERENCES clients (id);
ALTER TABLE payments ADD FOREIGN KEY (subscription_id) REFERENCES client_subscriptions (id) ON DELETE SET NULL;
ALTER TABLE payments ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE progress_photos ADD FOREIGN KEY (check_in_id) REFERENCES check_ins (id) ON DELETE SET NULL;
ALTER TABLE progress_photos ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE progress_photos ADD FOREIGN KEY (media_file_id) REFERENCES media_files (id);
ALTER TABLE progress_photos ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_answers ADD FOREIGN KEY (media_file_id) REFERENCES media_files (id);
ALTER TABLE questionnaire_answers ADD FOREIGN KEY (question_id) REFERENCES questionnaire_questions (id);
ALTER TABLE questionnaire_answers ADD FOREIGN KEY (response_id) REFERENCES questionnaire_responses (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_answers ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_questions ADD FOREIGN KEY (questionnaire_id) REFERENCES questionnaires (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_questions ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_responses ADD FOREIGN KEY (check_in_id) REFERENCES check_ins (id) ON DELETE SET NULL;
ALTER TABLE questionnaire_responses ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE questionnaire_responses ADD FOREIGN KEY (questionnaire_id) REFERENCES questionnaires (id);
ALTER TABLE questionnaire_responses ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE questionnaires ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE tenant_members ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE tenant_members ADD FOREIGN KEY (user_id) REFERENCES users (id);
ALTER TABLE tenant_payment_accounts ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE tenant_plan_subscriptions ADD FOREIGN KEY (plan_id) REFERENCES platform_plans (id);
ALTER TABLE tenant_plan_subscriptions ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE tenants ADD FOREIGN KEY (plan_id) REFERENCES platform_plans (id);
ALTER TABLE workout_day_exercises ADD FOREIGN KEY (exercise_id) REFERENCES exercises (id);
ALTER TABLE workout_day_exercises ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE workout_day_exercises ADD FOREIGN KEY (workout_day_id) REFERENCES workout_days (id) ON DELETE CASCADE;
ALTER TABLE workout_days ADD FOREIGN KEY (program_id) REFERENCES workout_programs (id) ON DELETE CASCADE;
ALTER TABLE workout_days ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE workout_programs ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE workout_programs ADD FOREIGN KEY (created_by_member_id) REFERENCES tenant_members (id) ON DELETE SET NULL;
ALTER TABLE workout_programs ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE workout_sessions ADD FOREIGN KEY (client_id) REFERENCES clients (id) ON DELETE CASCADE;
ALTER TABLE workout_sessions ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
ALTER TABLE workout_sessions ADD FOREIGN KEY (workout_day_id) REFERENCES workout_days (id) ON DELETE SET NULL;
ALTER TABLE workout_set_logs ADD FOREIGN KEY (day_exercise_id) REFERENCES workout_day_exercises (id) ON DELETE SET NULL;
ALTER TABLE workout_set_logs ADD FOREIGN KEY (exercise_id) REFERENCES exercises (id);
ALTER TABLE workout_set_logs ADD FOREIGN KEY (session_id) REFERENCES workout_sessions (id) ON DELETE CASCADE;
ALTER TABLE workout_set_logs ADD FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE;
