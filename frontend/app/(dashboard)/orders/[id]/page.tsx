"use client";

import { useParams } from "next/navigation";
import { OrderCard } from "@/features/orders/components/OrderCard";

export default function OrderCardPage() {
  const { id } = useParams<{ id: string }>();
  return <OrderCard id={id} />;
}
