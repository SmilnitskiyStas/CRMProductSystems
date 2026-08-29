import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { mobileCatalogSettingsApi, type SaveMobileCatalogSettings } from "../api/mobileCatalogSettings";

const key = ["mobile-catalog-publications"] as const;
export const useMobileCatalogSettings = () => useQuery({ queryKey: key, queryFn: mobileCatalogSettingsApi.list });
export const useMobileCatalogPublication = (id: string | null) => useQuery({ queryKey: [...key, id], queryFn: () => mobileCatalogSettingsApi.get(id!), enabled: !!id });
export function useSaveMobileCatalogSettings() { const client = useQueryClient(); return useMutation({ mutationFn: ({ id, body }: { id: string | null; body: SaveMobileCatalogSettings }) => id ? mobileCatalogSettingsApi.update(id, body) : mobileCatalogSettingsApi.create(body), onSuccess: () => client.invalidateQueries({ queryKey: key }) }); }
export function useUploadMobileCatalogBanner() { const client = useQueryClient(); return useMutation({ mutationFn: ({ id, file }: { id: string; file: File }) => mobileCatalogSettingsApi.uploadBanner(id, file), onSuccess: () => client.invalidateQueries({ queryKey: key }) }); }
export function useCatalogPublicationAction(action: "publish" | "archive" | "duplicate") { const client = useQueryClient(); return useMutation({ mutationFn: (id: string) => mobileCatalogSettingsApi[action](id), onSuccess: () => client.invalidateQueries({ queryKey: key }) }); }
export const useCatalogAnalytics = (id: string | null) => useQuery({ queryKey: [...key, id, "analytics"], queryFn: () => mobileCatalogSettingsApi.analytics(id!), enabled: !!id });
