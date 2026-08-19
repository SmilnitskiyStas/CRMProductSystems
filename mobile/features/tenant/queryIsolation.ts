import type { QueryClient, QueryKey } from '@tanstack/react-query';

export function consumerQueryBelongsToTenant(queryKey: QueryKey, tenantId: string): boolean {
  if (queryKey[0] === 'consumer-content') return queryKey[2] === tenantId;
  if (queryKey[0] === 'loyalty' && ['consumer-code', 'history'].includes(String(queryKey[1]))) {
    return queryKey[2] === tenantId;
  }
  return false;
}

export async function clearTenantQueries(queryClient: QueryClient, tenantId: string): Promise<void> {
  const predicate = ({ queryKey }: { queryKey: QueryKey }) =>
    consumerQueryBelongsToTenant(queryKey, tenantId);
  await queryClient.cancelQueries({ predicate });
  queryClient.removeQueries({ predicate });
}
