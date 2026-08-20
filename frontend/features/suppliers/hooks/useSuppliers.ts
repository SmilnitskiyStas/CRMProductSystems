import { useQuery } from "@tanstack/react-query";
import { suppliersApi } from "../api/suppliers";

export function useSuppliers() {
  return useQuery({
    queryKey: ["suppliers"],
    queryFn: () => suppliersApi.getAll(),
  });
}
