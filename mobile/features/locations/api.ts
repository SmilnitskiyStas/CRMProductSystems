import { apiClient } from '@/lib/api-client';
import type { WorkspaceLocation } from './types';

export async function getWorkspaceLocations(): Promise<WorkspaceLocation[]> {
  const { data } = await apiClient.get<WorkspaceLocation[]>('/locations');
  return Array.isArray(data) ? data.filter((location) => location.isActive) : [];
}
