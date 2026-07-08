-- Міграція 20260708100002: фікс name-mismatch chk_notif_type / chk_notification_type
--
-- Контекст (P0 bug, виявлено forensic-аудитом схеми 2026-07-08):
--   Міграція 00016 (notification_engine:18) створила constraint з ім'ям
--     chk_notif_type  (3 значення: IncidentCreated/Escalated/Closed).
--
--   Міграція 00023 (knowledge_coverage:237-244) спробувала розширити enum до
--   7 значень, але використала ІНШЕ ім'я constraint:
--     DROP CONSTRAINT IF EXISTS chk_notification_type  ← ім'я НЕ збігається → no-op
--     ADD    CONSTRAINT chk_notification_type (7 знач.) ← створив ДРУГИЙ constraint
--   (загорнуто у DO $$ ... EXCEPTION WHEN OTHERS — пройшло без помилки).
--
--   Результат: одночасно існують ОБИДВА constraints:
--     chk_notif_type        (3 знач.) — від 00016
--     chk_notification_type (7 знач.) — від 00023
--   Ефективний дозволений набір = перетин обох = оригінальні 3 значення.
--   Розширення до 7 типів НЕ дало ефекту.
--
--   Зараз dormant (promote 00016 породжує лише 'IncidentCreated'). Але будь-яка
--   escalation/resolution-notification (IncidentMitigated / IncidentResolved)
--   впаде з CheckViolationException.
--
--   KB §3.2.2 стверджувала «chk_notification_type 00016→00023, 3→7 ✅» — ХИБНО
--   (зафіксовано forensic-аудитом; KB буде оновлено у Phase B після стабілізації).
--
-- Фікс: DROP обох можливих імен (IF EXISTS — безпечно для будь-якого поточного
--   стану production: чи один chk_notif_type, чи обидва), потім ADD єдиного
--   chk_notification_type з канонічним набором 7 значень (з задуму 00023).
--
-- Additive-only: результуючий дозволений набір = 8 значень (об'єднання: 3 зі 00016 + 5 зі
--   00023). Суперсет попереднього ефективного набору (2 знач. — перетин старих 3×7).
--   Жоден існуючий рядок не порушує новий CHECK. Відповідає API Freeze v1.0.
--
-- Migration Review:
--   • ідемпотентність: DROP IF EXISTS (обидва імені) + ADD;
--   • конфліктів немає (notification_type не має FK/інших CHECK);
--   • VIEW не залежать від імен constraint;
--   • тип: text, без змін типу;
--   • rollback відкочує до єдиного chk_notif_type (3 знач.) — стан 00016.

-- 1. Прибрати обидва можливі constraints (незалежно від поточного стану production)
ALTER TABLE public.notification_queue DROP CONSTRAINT IF EXISTS chk_notif_type;
ALTER TABLE public.notification_queue DROP CONSTRAINT IF EXISTS chk_notification_type;

-- 2. Єдиний canonical constraint.
--    Набір = ОБ'ЄДНАННЯ старих 3 (00016: +IncidentClosed) + нових 5 (00023) = 8 значень.
--    Тестове виявлення (Phase 1 testing 2026-07-08): міграція 00023 при «розширенні»
--    ВПУСТИЛА 'IncidentClosed' (був у chk_notif_type 00016, але відсутній у списку 00023).
--    7-значний варіант відтворив би цю прогалину → reject валідного типу IncidentClosed.
--    Тому тут 8 значень (істинний superset обох попередніх constraints).
ALTER TABLE public.notification_queue
    ADD CONSTRAINT chk_notification_type CHECK (notification_type IN (
        'IncidentCreated','IncidentEscalated','IncidentClosed',
        'IncidentMitigated','IncidentResolved','WeeklyDigest','TestAlert','KnowledgeVerified'
    ));
