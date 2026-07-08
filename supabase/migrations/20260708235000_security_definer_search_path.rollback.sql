-- Phase 2.2 C2 rollback: відновити попередній стан search_path для кожної функції.
-- До міграції:
--   - ecosystem_stats() мав search_path=public
--   - set_country_from_cf() мав search_path=public
--   - всі інші public SECURITY DEFINER функції мали proconfig=NULL (default search_path)

-- Функції, які мали явний search_path=public до міграції
ALTER FUNCTION public.ecosystem_stats() SET search_path = public;
ALTER FUNCTION public.set_country_from_cf() SET search_path = public;

-- Решта функцій: скидаємо до дефолту (proconfig=NULL)
ALTER FUNCTION public.add_incident_note(bigint, text, text) RESET search_path;
ALTER FUNCTION public.add_knowledge_reference(bigint, text, text, text, text) RESET search_path;
ALTER FUNCTION public.assign_incident_owner(bigint, text) RESET search_path;
ALTER FUNCTION public.auto_close_stale_incidents() RESET search_path;
ALTER FUNCTION public.can_create_knowledge_for_incident(bigint) RESET search_path;
ALTER FUNCTION public.can_edit_knowledge(bigint) RESET search_path;
ALTER FUNCTION public.create_knowledge_from_incident(bigint, text, text, text, text) RESET search_path;
ALTER FUNCTION public.get_knowledge_current_version(bigint) RESET search_path;
ALTER FUNCTION public.get_knowledge_history(bigint) RESET search_path;
ALTER FUNCTION public.get_knowledge_version_detail(bigint) RESET search_path;
ALTER FUNCTION public.knowledge_audit_trigger() RESET search_path;
ALTER FUNCTION public.match_knowledge_for_incident(bigint) RESET search_path;
ALTER FUNCTION public.match_knowledge_priority2(bigint) RESET search_path;
ALTER FUNCTION public.parse_version_list(text) RESET search_path;
ALTER FUNCTION public.promote_incident_candidates() RESET search_path;
ALTER FUNCTION public.promote_incident_candidates_for_event(uuid) RESET search_path;
ALTER FUNCTION public.refresh_knowledge_coverage() RESET search_path;
ALTER FUNCTION public.remove_knowledge_reference(bigint, text) RESET search_path;
ALTER FUNCTION public.require_not_archived(bigint) RESET search_path;
ALTER FUNCTION public.search_knowledge(text, text, text, text, integer, integer) RESET search_path;
ALTER FUNCTION public.tg_incident_refresh_coverage() RESET search_path;
ALTER FUNCTION public.tg_promote_after_failed() RESET search_path;
ALTER FUNCTION public.transition_incident(bigint, text, text, text) RESET search_path;
ALTER FUNCTION public.transition_knowledge(bigint, text, text, integer, text) RESET search_path;
ALTER FUNCTION public.update_knowledge_entry(bigint, integer, text, text, text, text, text, text, text, text, text) RESET search_path;
ALTER FUNCTION public.verify_knowledge_auto() RESET search_path;
