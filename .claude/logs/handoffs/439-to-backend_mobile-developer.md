# TASK-439 historical contract clarification

**Date:** 2026-07-29  
**Status:** resolved — no backend change required

An initial reading of stale ADR wording suggested `/api/settings/modules` might be restricted to
enterprise admins. The current `ModulesSettingsController` disproved that assumption:

- the controller uses `[Authorize]`, not an enterprise-admin policy;
- it resolves the calling tenant from the authenticated `tenant_id` claim;
- it returns `businessType` and the active module list to every authenticated tenant staff role;
- identities without a tenant claim, including provider sessions, receive `403`.

TASK-439 resumed using this existing safe read contract. This file is retained only as an audit
trail explaining the temporary false alarm; there is no backend handoff or open contract blocker.
