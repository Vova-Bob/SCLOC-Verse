-- Міграція 20260707030000: Phase 3A — DROP дубльованих/зайвих індексів
--
-- Контекст:
--   Phase 2 Database Cleanup Review (KB §16.5) + Pre-Implementation Forensic
--   (2026-07-07) ідентифікували 2 індекси як доведені дублікати/невикористання.
--
--   1) idx_app_installations_install_id (NON-UNIQUE btree на install_id):
--      дублює UNIQUE constraint app_installations_install_id_key на тій самій
--      колонці. EXPLAIN ANALYZE показав, що планувальник обирає NON-UNIQUE
--      (1267 scans) замість UNIQUE (18 scans). Після DROP UNIQUE виконає ту
--      саму роботу — пошук за install_id + гарантія унікальності.
--
--   2) idx_user_discord_guilds_user_id (btree на user_id):
--      дублює провідний стовпець UNIQUE (user_id, discord_guild_id).
--      Таблиця порожня (0 рядків), код DiscordGuildSyncService мертвий.
--
--   НЕ ВХОДИТЬ (DEFER): idx_app_installations_machine_id — хоча idx_scan=1,
--   EXPLAIN ANALYZE підтвердив, що планувальник використовує його при
--   WHERE machine_id. app_installations — діагностична таблиця; через рік
--   можуть бути десятки тисяч установок, індекс може знадобитись для
--   адмін-запитів «усі установки цієї машини». Економія зараз нульова,
--   ризик майбутній — DEFER (див. KB §16.5, Rejected #71).
--
-- Additive-only (DROP INDEX не порушує схематичний контракт).
-- Zero Regression: доведено Pre-Impl Forensic через EXPLAIN ANALYZE.

DROP INDEX IF EXISTS public.idx_app_installations_install_id;
DROP INDEX IF EXISTS public.idx_user_discord_guilds_user_id;
