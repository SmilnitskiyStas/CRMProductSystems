"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Button } from "@/components/ui/button";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

const productSchema = z.object({
  sku: z.string().min(1, "Required").max(100),
  name: z.string().min(1, "Required").max(200),
  description: z.string().max(1000).optional(),
  category: z.string().min(1, "Required").max(100),
  unit: z.string().min(1, "Required").max(50),
  costPrice: z.coerce.number().min(0, "Must be ≥ 0"),
  salePrice: z.coerce.number().min(0, "Must be ≥ 0"),
  reorderLevel: z.coerce.number().min(0, "Must be ≥ 0"),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof productSchema>;

const defaultValues: FormValues = {
  sku: "",
  name: "",
  description: "",
  category: "",
  unit: "",
  costPrice: 0,
  salePrice: 0,
  reorderLevel: 0,
  isActive: true,
};

interface Props {
  open: boolean;
  product: Product | null;
  isPending: boolean;
  onClose: () => void;
  onCreate: (payload: CreateProductPayload) => void;
  onUpdate: (id: string, payload: UpdateProductPayload) => void;
}

export function ProductForm({ open, product, isPending, onClose, onCreate, onUpdate }: Props) {
  const isEditing = product !== null;

  const form = useForm<FormValues>({
    resolver: zodResolver(productSchema),
    defaultValues,
  });

  useEffect(() => {
    form.reset(
      product
        ? {
            sku: product.sku,
            name: product.name,
            description: product.description ?? "",
            category: product.category,
            unit: product.unit,
            costPrice: product.costPrice,
            salePrice: product.salePrice,
            reorderLevel: product.reorderLevel,
            isActive: product.isActive,
          }
        : defaultValues,
    );
  }, [product, form]);

  const onSubmit = (values: FormValues) => {
    if (isEditing) {
      onUpdate(product.id, {
        name: values.name,
        description: values.description || undefined,
        category: values.category,
        unit: values.unit,
        costPrice: values.costPrice,
        salePrice: values.salePrice,
        reorderLevel: values.reorderLevel,
        isActive: values.isActive,
      });
    } else {
      onCreate({
        sku: values.sku,
        name: values.name,
        description: values.description || undefined,
        category: values.category,
        unit: values.unit,
        costPrice: values.costPrice,
        salePrice: values.salePrice,
        reorderLevel: values.reorderLevel,
      });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEditing ? "Edit product" : "Add product"}</DialogTitle>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <FormField
                control={form.control}
                name="sku"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>SKU</FormLabel>
                    <FormControl>
                      <Input {...field} disabled={isEditing} placeholder="PROD-001" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="unit"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Unit</FormLabel>
                    <FormControl>
                      <Input {...field} placeholder="kg, piece, box…" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Product name" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="category"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Category</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. Dairy, Produce, Beverages" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Description (optional)</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Short description" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="grid grid-cols-3 gap-4">
              <FormField
                control={form.control}
                name="costPrice"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Cost price</FormLabel>
                    <FormControl>
                      <Input {...field} type="number" step="0.01" min="0" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="salePrice"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Sale price</FormLabel>
                    <FormControl>
                      <Input {...field} type="number" step="0.01" min="0" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="reorderLevel"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Reorder at</FormLabel>
                    <FormControl>
                      <Input {...field} type="number" step="0.01" min="0" />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            {isEditing && (
              <FormField
                control={form.control}
                name="isActive"
                render={({ field }) => (
                  <FormItem className="flex items-center gap-3">
                    <FormLabel className="mb-0">Active</FormLabel>
                    <FormControl>
                      <Switch checked={field.value} onCheckedChange={field.onChange} />
                    </FormControl>
                  </FormItem>
                )}
              />
            )}

            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose}>
                Cancel
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending ? "Saving…" : isEditing ? "Save changes" : "Add product"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
