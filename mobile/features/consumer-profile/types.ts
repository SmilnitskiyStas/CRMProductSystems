export interface ConsumerProfile {
  consumerAccountId: string;
  fullName: string;
  email: string | null;
  phone: string;
  registeredAt: string;
}

export interface ConsumerProfileChange {
  fieldName: 'full_name' | 'email' | 'phone' | string;
  oldValue: string | null;
  newValue: string | null;
  changedAt: string;
}
