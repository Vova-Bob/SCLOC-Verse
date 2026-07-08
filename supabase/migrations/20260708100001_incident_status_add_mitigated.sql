-- Міграція 20260708100001: розширення chk_incident_status — додано стан Mitigated
--
-- Контекст (P0 bug F2, виявлено forensic-аудитом схеми 2026-07-08):
--   Міграція 00012 (incident_tables:28) створила CHECK:
--     chk_incident_status CHECK (status IN ('Active','Confirmed','Investigating','Resolved','Closed'))
--   — БЕЗ статусу 'Mitigated'.
--
--   Але статус Mitigated використовується всюди:
--     • transition_incident() (00015:57-64) — CASE містить 'Mitigated' => order 4
--       (forward-only валідація: Active/Confirmed/Investigating → Mitigated → Resolved/Closed).
--     • can_create_knowledge_for_incident() (00020:36) — вимагає
--       status IN ('Mitigated','Resolved','Closed') для створення knowledge.
--     • Control Center Incidents.razor StateFlow — пропонує перехід → Mitigated.
--
--   Наслідок (гарантований runtime-fail): клік оператора «→ Mitigated» у CC →
--   transition_incident UPDATE telemetry_incidents SET status='Mitigated' →
--   CheckViolationException (new row violates check constraint chk_incident_status).
--   Статус Mitigated фізично недосяжний → knowledge можна створити лише з
--   Resolved/Closed.
--
-- Фікс: additive DROP+ADD CHECK з доданим 'Mitigated' (єдина відсутня
--   значення, що реально використовується кодом/функціями/UI). Acknowledged
--   НЕ додається — він згадується лише в коментарях auto_close (05030000),
--   але не реалізований у transition_incident CASE (order невизначений →
--  ELSE 0). Додавання Acknowledged — окрема фіча (CASE + UI разом), не цей фікс.
--
-- Additive-only: розширення дозволених значень CHECK (5→6). Жоден існуючий
--   рядок не порушує новий CHECK. Відповідає API Freeze v1.0 (§13.8) —
--   розширення enum дозволене. Additive-only контракт схеми (Стаття 13).
--
-- Migration Review:
--   • ідемпотентність: DROP IF EXISTS + ADD (відповідає патерну 00017);
--   • конфліктів із FK/UNIQUE/іншими CHECK немає (status не має FK);
--   • VIEW не залежать від визначення CHECK (читають значення колонки);
--   • тип: text, без змін типу;
--   • rollback-скрипт відкочує до 5-значного CHECK (відновлює F2 — аварійно).

ALTER TABLE public.telemetry_incidents DROP CONSTRAINT IF EXISTS chk_incident_status;
ALTER TABLE public.telemetry_incidents
    ADD CONSTRAINT chk_incident_status CHECK (status IN
        ('Active','Confirmed','Investigating','Mitigated','Resolved','Closed'));
