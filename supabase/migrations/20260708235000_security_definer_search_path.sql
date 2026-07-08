-- Phase 2.2 C2: Security hardening — SET search_path для всіх public SECURITY DEFINER функцій.
-- Умова: лише ALTER FUNCTION ... SET search_path, без зміни тіла, власника, SECURITY DEFINER, сигнатури, GRANT, COST, VOLATILE/STABLE/IMMUTABLE, PARALLEL, LEAKPROOF.
-- Очікувана конфігурація: search_path = public, pg_catalog

ALTER FUNCTION public.add_incident_note(bigint, text, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.add_knowledge_reference(bigint, text, text, text, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.assign_incident_owner(bigint, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.auto_close_stale_incidents() SET search_path = public, pg_catalog;
ALTER FUNCTION public.can_create_knowledge_for_incident(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.can_edit_knowledge(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.create_knowledge_from_incident(bigint, text, text, text, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.ecosystem_stats() SET search_path = public, pg_catalog;
ALTER FUNCTION public.get_knowledge_current_version(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.get_knowledge_history(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.get_knowledge_version_detail(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.knowledge_audit_trigger() SET search_path = public, pg_catalog;
ALTER FUNCTION public.match_knowledge_for_incident(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.match_knowledge_priority2(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.parse_version_list(text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.promote_incident_candidates() SET search_path = public, pg_catalog;
ALTER FUNCTION public.promote_incident_candidates_for_event(uuid) SET search_path = public, pg_catalog;
ALTER FUNCTION public.refresh_knowledge_coverage() SET search_path = public, pg_catalog;
ALTER FUNCTION public.remove_knowledge_reference(bigint, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.require_not_archived(bigint) SET search_path = public, pg_catalog;
ALTER FUNCTION public.search_knowledge(text, text, text, text, integer, integer) SET search_path = public, pg_catalog;
ALTER FUNCTION public.set_country_from_cf() SET search_path = public, pg_catalog;
ALTER FUNCTION public.tg_incident_refresh_coverage() SET search_path = public, pg_catalog;
ALTER FUNCTION public.tg_promote_after_failed() SET search_path = public, pg_catalog;
ALTER FUNCTION public.transition_incident(bigint, text, text, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.transition_knowledge(bigint, text, text, integer, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.update_knowledge_entry(bigint, integer, text, text, text, text, text, text, text, text, text) SET search_path = public, pg_catalog;
ALTER FUNCTION public.verify_knowledge_auto() SET search_path = public, pg_catalog;
