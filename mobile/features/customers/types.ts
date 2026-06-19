export interface Customer {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
  tags: string[];
  totalOrders: number;
  totalSpent: number;
  createdAt: string;
}

export interface RecentTransaction {
  id: string;
  totalAmount: number;
  paymentType: string;
  createdAt: string;
  status: string;
}

export interface CustomerDetail extends Customer {
  recentTransactions: RecentTransaction[];
}

export interface CreateCustomerPayload {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
  tags?: string[];
}

export interface UpdateCustomerPayload {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
  tags?: string[];
}

export interface CustomersPage {
  items: Customer[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
