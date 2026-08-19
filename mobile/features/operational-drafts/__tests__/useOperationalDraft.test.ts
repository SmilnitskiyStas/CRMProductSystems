import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, renderHook, waitFor } from '@testing-library/react-native';
import { useEffect, useState } from 'react';
import {
  draftStorageKey,
  resetDraftQueuesForTests,
  saveOperationalDraft,
  type DraftOwner,
} from '../storage';
import { useOperationalDraft } from '../useOperationalDraft';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'));

beforeEach(async () => {
  resetDraftQueuesForTests();
  await AsyncStorage.clear();
});

function useTransferDraftForm(owner: DraftOwner) {
  const draft = useOperationalDraft(owner, 'transfer');
  const [notes, setNotes] = useState('');

  useEffect(() => {
    if (draft.restored?.payload.kind === 'transfer') {
      // Mirrors the production form's restore effect.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setNotes(draft.restored.payload.notes);
    }
  }, [draft.restored]);

  useEffect(() => {
    if (!draft.hydrated || !notes) return;
    void draft.persist({
      kind: 'transfer',
      // A draft is allowed before an assigned/source location is available.
      fromLocationId: '',
      toLocationId: '',
      notes,
      items: [],
    });
  }, [draft.hydrated, notes]); // eslint-disable-line react-hooks/exhaustive-deps

  return { ...draft, notes, setNotes };
}

async function waitForStoredMarker(key: string, marker: string): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if ((await AsyncStorage.getItem(key))?.includes(marker)) return;
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
  throw new Error(`Draft marker was not persisted for ${key}`);
}

it('fails closed immediately when the owner context changes', async () => {
  const first = { tenantId: 'tenant-a', userId: 'user-a' };
  await saveOperationalDraft({
    version: 1,
    owner: first,
    scope: 'create',
    payload: { kind: 'production', locationId: 'loc', recipeId: 'recipe', plannedQty: '2', notes: '' },
    submission: { status: 'conflict', message: 'old owner' },
    updatedAt: '2026-07-29T10:00:00Z',
  });

  const { result, rerender } = await renderHook(
    ({ owner }: { owner: typeof first }) => useOperationalDraft(owner, 'production'),
    { initialProps: { owner: first } },
  );
  await waitFor(() => expect(result.current.restored).not.toBeNull());

  await rerender({ owner: { tenantId: 'tenant-b', userId: 'user-b' } });
  expect(result.current.restored).toBeNull();
  expect(result.current.submission.status).toBe('idle');
});

it('persists an edited form across unmount and process-restart-equivalent remount', async () => {
  const owner = { tenantId: 'tenant-a', userId: 'user-a' };
  const firstMount = await renderHook(() => useTransferDraftForm(owner));
  await waitFor(() => expect(firstMount.result.current.hydrated).toBe(true));

  await act(async () => {
    firstMount.result.current.setNotes('cold restore marker');
  });
  const key = draftStorageKey(owner, 'transfer');
  await waitForStoredMarker(key, 'cold restore marker');

  await firstMount.unmount();
  resetDraftQueuesForTests();

  const restarted = await renderHook(() => useTransferDraftForm(owner));
  await waitFor(() => expect(restarted.result.current.notes).toBe('cold restore marker'));
  await restarted.unmount();
});

it('keeps the persisted form through a foreign-owner mount and restores it to its owner', async () => {
  const owner = { tenantId: 'tenant-a', userId: 'user-a' };
  const foreign = { tenantId: 'tenant-a', userId: 'user-b' };
  const firstMount = await renderHook(() => useTransferDraftForm(owner));
  await waitFor(() => expect(firstMount.result.current.hydrated).toBe(true));
  await act(async () => {
    firstMount.result.current.setNotes('owner-only marker');
  });
  await waitForStoredMarker(draftStorageKey(owner, 'transfer'), 'owner-only marker');
  await firstMount.unmount();

  const foreignMount = await renderHook(() => useTransferDraftForm(foreign));
  await waitFor(() => expect(foreignMount.result.current.hydrated).toBe(true));
  expect(foreignMount.result.current.notes).toBe('');
  await foreignMount.unmount();

  resetDraftQueuesForTests();
  const ownerReturn = await renderHook(() => useTransferDraftForm(owner));
  await waitFor(() => expect(ownerReturn.result.current.notes).toBe('owner-only marker'));
  await ownerReturn.unmount();
});
