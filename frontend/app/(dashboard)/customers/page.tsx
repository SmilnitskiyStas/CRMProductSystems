"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { CustomerTable } from "@/features/customers/components/CustomerTable";
import { CustomerForm } from "@/features/customers/components/CustomerForm";
import { CustomerDetail } from "@/features/customers/components/CustomerDetail";
import {
  useCustomers,
  useCreateCustomer,
  useUpdateCustomer,
  useDeleteCustomer,
} from "@/features/customers/hooks/useCustomers";
import type { Customer, CreateCustomerPayload, UpdateCustomerPayload } from "@/features/customers/types";
import { Btn } from "@/components/ui/Btn";

const PAGE_SIZE = 50;

export default function CustomersPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Customer | null>(null);
  const [selected, setSelected] = useState<Customer | null>(null);

  const { data, isLoading } = useCustomers(page, PAGE_SIZE, search);

  const createMutation = useCreateCustomer();
  const updateMutation = useUpdateCustomer();
  const deleteMutation = useDeleteCustomer();

  const customers   = data?.items     ?? [];
  const totalCount  = data?.totalCount ?? 0;
  const totalPages  = data?.totalPages ?? 1;

  // ── Handlers ─────────────────────────────────────────────────────────────────

  function handleCreate(payload: CreateCustomerPayload | UpdateCustomerPayload) {
    createMutation.mutate(payload as CreateCustomerPayload, {
      onSuccess: () => {
        toast.success("Клієнта створено");
        setFormOpen(false);
      },
      onError: (err) => toast.error(err.message),
    });
  }

  function handleUpdate(payload: CreateCustomerPayload | UpdateCustomerPayload) {
    if (!editing) return;
    updateMutation.mutate(
      { id: editing.id, data: payload as UpdateCustomerPayload },
      {
        onSuccess: () => {
          toast.success("Клієнта оновлено");
          setEditing(null);
          // If the drawer was open for this customer, update its ref
          if (selected?.id === editing.id) setSelected(null);
        },
        onError: (err) => toast.error(err.message),
      },
    );
  }

  function handleDelete(customer: Customer) {
    if (!confirm(`Видалити клієнта "${customer.name}"?`)) return;
    deleteMutation.mutate(customer.id, {
      onSuccess: () => {
        toast.success("Клієнта видалено");
        if (selected?.id === customer.id) setSelected(null);
      },
      onError: (err) => toast.error(err.message),
    });
  }

  function openEdit(customer: Customer) {
    setEditing(customer);
    setSelected(null);
  }

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 22,
        }}
      >
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Клієнти
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            База клієнтів — контакти, теги, історія замовлень
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={() => { setEditing(null); setFormOpen(true); }}>
          Додати клієнта
        </Btn>
      </div>

      {/* Table */}
      <CustomerTable
        customers={customers}
        totalCount={totalCount}
        page={page}
        totalPages={totalPages}
        search={search}
        isLoading={isLoading}
        onSearchChange={(v) => { setSearch(v); setPage(1); }}
        onPageChange={setPage}
        onRowClick={(c) => { setSelected(c); setEditing(null); }}
        onEdit={openEdit}
        onDelete={handleDelete}
      />

      {/* Create form */}
      {formOpen && !editing && (
        <CustomerForm
          isPending={createMutation.isPending}
          error={createMutation.error?.message ?? null}
          onClose={() => { setFormOpen(false); createMutation.reset(); }}
          onSubmit={handleCreate}
        />
      )}

      {/* Edit form */}
      {editing && (
        <CustomerForm
          customer={editing}
          isPending={updateMutation.isPending}
          error={updateMutation.error?.message ?? null}
          onClose={() => { setEditing(null); updateMutation.reset(); }}
          onSubmit={handleUpdate}
        />
      )}

      {/* Detail drawer */}
      {selected && (
        <CustomerDetail
          customer={selected}
          onClose={() => setSelected(null)}
          onEdit={() => openEdit(selected)}
        />
      )}
    </div>
  );
}
