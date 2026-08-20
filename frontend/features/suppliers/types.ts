export interface SupplierDto {
  id: string;
  name: string;
  edrpou: string | null;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  deliveryDays: number;
  hasSupplierPortal: boolean;
  returnPolicy: boolean;
  paymentTerms: string | null;
  notes: string | null;
  isActive: boolean;
}
