-- ============================================================================
-- SCLOC-Verse 1.0.0.1 — Production DB Verification Script
-- ============================================================================
-- Призначення:  Підтвердження, що БД абсолютно чиста після cleanup.
-- Проєкт:       nrytczdbhehiotflaagl
-- Дата:         2026-07-04
-- Режим:        Лише читання. Не модифікує дані.
--
-- ОЧІКУВАНИЙ РЕЗУЛЬТАТ після cleanup:
--   - telemetry_events:            0
--   - telemetry_incidents:         0
--   - incident_status_log:         0
--   - incident_notes:              0
--   - knowledge_entries:           0
--   - knowledge_references:        0
--   - knowledge_version_history:   0
--   - notification_queue:          0
--   - notification_attempts:       0
--   - app_installations:           0
--   - knowledge_coverage:          0.0% (0 / 0)
--   - incident_policy:             5 (ЗБЕРЕЖЕНО — production config)
--   - тестових INC-2026-0000X:     0
--   - тестових fingerprint abc123: 0
-- ============================================================================

-- ── 1. Підрахунок записів observability таблиць (усі мають бути 0) ──────────
SELECT 'observability_tables' AS check_name,
       (SELECT count(*) FROM public.telemetry_events) AS telemetry_events,
       (SELECT count(*) FROM public.telemetry_incidents) AS telemetry_incidents,
       (SELECT count(*) FROM public.incident_status_log) AS incident_status_log,
       (SELECT count(*) FROM public.incident_notes) AS incident_notes,
       (SELECT count(*) FROM public.knowledge_entries) AS knowledge_entries,
       (SELECT count(*) FROM public.knowledge_references) AS knowledge_references,
       (SELECT count(*) FROM public.knowledge_version_history) AS knowledge_version_history,
       (SELECT count(*) FROM public.notification_queue) AS notification_queue,
       (SELECT count(*) FROM public.notification_attempts) AS notification_attempts,
       (SELECT count(*) FROM public.app_installations) AS app_installations;

-- ── 2. Відсутність тестових інцидентів (INC-2026-0000X) ─────────────────────
SELECT 'test_incidents_check' AS check_name, count(*) AS found
FROM public.telemetry_incidents
WHERE id::text ~ '^[0-9]+$' AND id <= 1000000;
-- Очікування: 0

-- ── 3. Відсутність тестових fingerprint (abc123) ────────────────────────────
SELECT 'test_fingerprint_check' AS check_name, count(*) AS found
FROM public.telemetry_incidents
WHERE fingerprint_hash = 'abc123';
-- Очікування: 0

-- ── 4. Відсутність тестових install_id (install-1, sim-install-*) ───────────
SELECT 'test_install_id_check' AS check_name, count(*) AS found
FROM public.app_installations
WHERE install_id = 'install-1'
   OR install_id LIKE 'sim-install-%';
-- Очікування: 0

-- ── 5. Відсутність тестових app_version (1.0.0 — не 1.0.0.0) ────────────────
SELECT 'test_version_check' AS check_name, count(*) AS found
FROM public.telemetry_events
WHERE app_version = '1.0.0';
-- Очікування: 0

-- ── 6. Knowledge Coverage (має бути 0.0%) ───────────────────────────────────
SELECT 'knowledge_coverage' AS check_name,
       total_fingerprints,
       covered_fingerprints,
       uncovered_fingerprints,
       coverage_pct
FROM control_center.knowledge_coverage;
-- Очікування: total=0, covered=0, uncovered=0, coverage=0.0

-- ── 7. Production config ЗБЕРЕЖЕНО (incident_policy) ───────────────────────
SELECT 'production_config_check' AS check_name, count(*) AS policies_preserved
FROM public.incident_policy;
-- Очікування: 5 (НЕ 0 — це production config, має залишитись)

-- ── 8. Auth користувачі ЗБЕРЕЖЕНІ ───────────────────────────────────────────
SELECT 'auth_users_check' AS check_name, count(*) AS users_preserved
FROM auth.users;
-- Очікування: >0 (користувачі Supabase Auth збережені)

-- ============================================================================
-- ПІДСУМКОВИЙ ВЕРДИКТ
-- ============================================================================
-- Успіх, ЯКЩО:
--   observability_tables → усі колонки = 0
--   test_incidents_check → found = 0
--   test_fingerprint_check → found = 0
--   test_install_id_check → found = 0
--   test_version_check → found = 0
--   knowledge_coverage → total_fingerprints = 0, coverage_pct = 0.0
--   production_config_check → policies_preserved = 5
--   auth_users_check → users_preserved > 0
--
-- Якщо ВСІ умови виконано — БД готова до релізу 1.0.0.1.
-- ============================================================================
