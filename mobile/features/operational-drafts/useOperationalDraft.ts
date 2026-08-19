import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import {
  clearOperationalDraft,
  loadOperationalDraft,
  saveOperationalDraft,
  type DraftOwner,
  type DraftSubmissionStatus,
  type OperationalDraft,
  type OperationalDraftKind,
  type OperationalDraftPayload,
} from './storage';

export function useOperationalDraft(
  owner: DraftOwner | null,
  kind: OperationalDraftKind,
  scope = 'create',
) {
  const contextKey = owner ? `${owner.tenantId}:${owner.userId}:${kind}:${scope}` : '';
  const [loaded, setLoaded] = useState<{ key: string; draft: OperationalDraft | null }>({ key: '', draft: null });
  const [submissionState, setSubmission] = useState<{
    key: string;
    value: { status: DraftSubmissionStatus; message?: string };
  }>({ key: '', value: { status: 'idle' } });
  const hydrated = useRef(false);
  const latestDraft = useRef<OperationalDraft | null>(null);
  const [isHydrated, setIsHydrated] = useState(false);

  useEffect(() => {
    let active = true;
    hydrated.current = false;
    latestDraft.current = null;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsHydrated(false);
    if (!owner) return;
    void loadOperationalDraft(owner, kind, scope).then((draft) => {
      if (!active) return;
      setLoaded({ key: contextKey, draft });
      latestDraft.current = draft;
      if (draft) setSubmission({ key: contextKey, value: draft.submission });
      hydrated.current = true;
      setIsHydrated(true);
    });
    return () => { active = false; };
  }, [owner?.tenantId, owner?.userId, kind, scope, contextKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const restored = loaded.key === contextKey ? loaded.draft : null;
  const submission = useMemo(() => submissionState.key === contextKey
    ? submissionState.value
    : { status: 'idle' as const }, [contextKey, submissionState]);

  const persist = useCallback(async (
    payload: OperationalDraftPayload,
    nextSubmission = submission,
  ) => {
    if (!owner || payload.kind !== kind) return;
    const nextDraft: OperationalDraft = {
      version: 1,
      owner,
      scope,
      payload,
      submission: nextSubmission,
      updatedAt: new Date().toISOString(),
    };
    latestDraft.current = nextDraft;
    await saveOperationalDraft(nextDraft);
  }, [kind, owner, scope, submission]);

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state !== 'active' && latestDraft.current) {
        void saveOperationalDraft(latestDraft.current);
      }
    });
    return () => subscription.remove();
  }, []);

  const transition = useCallback(async (next: { status: DraftSubmissionStatus; message?: string }, payload: OperationalDraftPayload) => {
    setSubmission({ key: contextKey, value: next });
    await persist(payload, next);
  }, [contextKey, persist]);

  const clear = useCallback(async () => {
    if (!owner) return;
    await clearOperationalDraft(owner, kind, scope);
    latestDraft.current = null;
    setLoaded({ key: contextKey, draft: null });
    setSubmission({ key: contextKey, value: { status: 'idle' } });
  }, [contextKey, kind, owner, scope]);

  return { restored, submission, hydrated: isHydrated, persist, transition, clear };
}
