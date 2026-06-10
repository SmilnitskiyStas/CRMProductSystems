import { useQuery } from "@tanstack/react-query";
import { storesApi } from "../api/stores";

export function useStores() {
  return useQuery({
    queryKey: ["stores"],
    queryFn: () => storesApi.getAll(),
  });
}

export function useStore(id: string | null) {
  return useQuery({
    queryKey: ["stores", id],
    queryFn: () => storesApi.getById(id!),
    enabled: !!id,
  });
}
