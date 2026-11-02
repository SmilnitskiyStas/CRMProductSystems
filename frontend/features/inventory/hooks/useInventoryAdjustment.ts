"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, getSessionEpoch } from "@/lib/api";
import { stockApi } from "@/features/shelf/api/stock";
import { createInventoryAdjustment, inventoryPostingApi, type InventoryAdjustmentReceipt } from "../api/inventory-posting";
import { readAdjustment, saveAdjustment, type AdjustmentBatch, type AdjustmentIdentity, type SavedAdjustment } from "../lib/inventoryAdjustmentSession";

type ErrorKey = "invalid" | "storage" | "uncertain" | "conflict" | "disabled" | "forbidden" | "notFound";

export function useInventoryAdjustment(identity: AdjustmentIdentity, selected: AdjustmentBatch | null) {
  const qc = useQueryClient();
  const [saved, setSaved] = useState<SavedAdjustment | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [storageError, setStorageError] = useState(false);
  const [resumedBatch, setResumedBatch] = useState<AdjustmentBatch | null>(null);
  const [receiptBatch, setReceiptBatch] = useState<AdjustmentBatch | null>(null);
  const [quantity, setQuantity] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<ErrorKey | null>(null);
  const [busy, setBusy] = useState(false);
  const [receipt, setReceipt] = useState<InventoryAdjustmentReceipt | null>(null);
  const lock = useRef(false);
  const mounted = useRef(false);

  function load() {
    try { setSaved(readAdjustment(identity, window.sessionStorage)); setStorageError(false); }
    catch { setStorageError(true); }
    setLoaded(true);
  }
  useEffect(() => {
    mounted.current = true;
    try { setSaved(readAdjustment(identity, window.sessionStorage)); setStorageError(false); }
    catch { setStorageError(true); }
    setLoaded(true);
    return () => { mounted.current = false; };
  }, [identity.tenantId, identity.actorId]);

  const batch = selected || resumedBatch ? receiptBatch ?? saved?.batch ?? resumedBatch ?? selected : null;
  const context = useQuery({
    queryKey: ["inventory-adjustment-context", identity.tenantId, identity.actorId, batch?.id, batch?.storeId],
    queryFn: async () => {
      const [stock, status] = await Promise.all([stockApi.getById(batch!.id), inventoryPostingApi.status(batch!.storeId)]);
      if (stock.id !== batch!.id || stock.storeId !== batch!.storeId || status.locationId !== batch!.storeId)
        throw new Error("Adjustment scope changed.");
      return { stock, status };
    },
    enabled: loaded && !storageError && !!batch,
    retry: false, staleTime: 0, gcTime: 0, refetchOnWindowFocus: false, refetchOnReconnect: false,
  });

  function store(value: SavedAdjustment | null) {
    saveAdjustment(identity, value, window.sessionStorage);
    setSaved(value);
  }

  async function submit() {
    if (lock.current || !loaded || storageError || receipt || !batch || !context.data?.status.canAdjust || context.isFetching || context.isError) return;
    lock.current = true; setBusy(true); setError(null);
    const epoch = getSessionEpoch();
    const active = () => mounted.current && getSessionEpoch() === epoch;
    const wasUncertain = saved?.phase === "uncertain";
    let intent: SavedAdjustment;
    try {
      intent = saved ?? { version: 1, phase: "uncertain", batch: { id: batch.id, storeId: batch.storeId,
        productName: batch.productName, storeName: batch.storeName, batchNumber: batch.batchNumber, expiryDate: batch.expiryDate },
        command: createInventoryAdjustment({ requestId: crypto.randomUUID(), locationId: batch.storeId, batchId: batch.id,
          expectedQuantity: String(context.data.stock.quantity), quantity: quantity.replace(",", "."), reason }) };
    } catch { setError("invalid"); lock.current = false; setBusy(false); return; }
    try { store({ ...intent, phase: "uncertain" }); }
    catch { setError("storage"); lock.current = false; setBusy(false); return; }
    try {
      const result = await inventoryPostingApi.adjust(intent.command);
      if (!active()) return;
      if (result.tenantId !== identity.tenantId || result.requestId !== intent.command.requestId || result.batchId !== intent.command.batchId
        || result.quantityBefore !== intent.command.expectedQuantity || result.quantityAfter !== intent.command.quantity || !result.documentId)
        throw new Error("Unexpected adjustment receipt.");
      setReceiptBatch(batch); setReceipt(result);
      try { store(null); } catch { setError("storage"); }
      for (const prefix of ["stock", "stock-suggestions", "product-movements", "product-stock-history", "product-buffer", "dashboard"])
        void qc.invalidateQueries({ queryKey: [prefix] });
    } catch (failure) {
      if (!active()) return;
      const rejected = failure instanceof ApiError && [400, 403, 404, 409].includes(failure.status);
      const code = (failure instanceof ApiError ? failure.body as { code?: string } | undefined : undefined)?.code;
      if (rejected && !wasUncertain) {
        try { store({ ...intent, phase: "rejected" }); } catch { /* Keep the durable uncertain command. */ }
      }
      setError(!rejected || wasUncertain ? "uncertain" : code === "inventory_posting_disabled" ? "disabled"
        : failure.status === 409 ? "conflict" : failure.status === 403 ? "forbidden" : failure.status === 404 ? "notFound" : "invalid");
    } finally {
      lock.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  async function review() {
    if (lock.current || !loaded || storageError || !batch || saved?.phase === "uncertain") return;
    lock.current = true; setBusy(true); setError(null);
    const epoch = getSessionEpoch();
    try {
      const fresh = await context.refetch();
      if (!mounted.current || getSessionEpoch() !== epoch) return;
      if (fresh.error || !fresh.data) return;
      store(null); setReceipt(null); setQuantity(""); setReason("");
    } catch { setError("storage"); }
    finally { lock.current = false; if (mounted.current && getSessionEpoch() === epoch) setBusy(false); }
  }

  function close() {
    if (lock.current) return false;
    setResumedBatch(null); setReceiptBatch(null); setReceipt(null); setError(null); setQuantity(""); setReason("");
    return true;
  }

  return { batch, saved, loaded, storageError, load, resume: () => setResumedBatch(saved?.batch ?? null), quantity, setQuantity, reason, setReason,
    error, busy, receipt, context, submit, review, close };
}
