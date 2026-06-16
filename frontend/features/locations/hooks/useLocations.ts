import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { locationsApi, type CreateLocationDto, type UpdateLocationDto } from "../api/locations";

export function useLocations() {
  return useQuery({
    queryKey: ["locations"],
    queryFn: () => locationsApi.getAll(),
  });
}

export function useLocation(id: string | null) {
  return useQuery({
    queryKey: ["locations", id],
    queryFn: () => locationsApi.getById(id!),
    enabled: !!id,
  });
}

export function useCreateLocation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateLocationDto) => locationsApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["locations"] }),
  });
}

export function useUpdateLocation(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateLocationDto) => locationsApi.update(id, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["locations"] }),
  });
}
