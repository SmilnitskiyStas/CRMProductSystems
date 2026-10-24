let tail: Promise<unknown> = Promise.resolve();

/** Serializes credential writes with logout cleanup, including a new login during refresh. */
export function withSessionPersistence<T>(action: () => Promise<T>): Promise<T> {
  const result = tail.then(action, action);
  tail = result.catch(() => undefined);
  return result;
}
