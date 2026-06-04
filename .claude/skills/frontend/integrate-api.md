# Skill: Integrate API

Location: frontend/features/{domain}/api/ and hooks/

API file pattern:
- apiFetch<T> wrapper with error handling
- Export named api object: { getAll, getById, create, update, delete }
- 204 No Content returns undefined, not JSON parse

Hooks pattern:
- useX() for queries: useQuery({ queryKey, queryFn })
- useCreateX(), useUpdateX(), useDeleteX() for mutations
- onSuccess: invalidateQueries
- QUERY_KEY as const array

Rules:
- Never call fetch directly in components
- API_BASE from NEXT_PUBLIC_API_URL env var
